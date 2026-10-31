using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class PonyAnimationChecks
    {
        public static void RunBatch()
        {
            int exit=0;PonyPoseDocument document=null,roundtrip=null;
            try
            {
                PonyPoseMathChecks.Run();document=PonyAnimationArtwork.CreateDocument();
                var output=Path.GetFullPath("../.reference/pony-animation-editor");Directory.CreateDirectory(output);
                var bare=PonyAnimationArtwork.Prepare();var armored=PonyAnimationArtwork.Prepare("armor.battle");
                var rest=PonyPoseMath.Rest(document);var matrices=bare.Matrices(document,rest);
                float identityError=0;
                for(int n=0;n<matrices.Length;n++)for(int c=0;c<16;c++)identityError=Mathf.Max(identityError,Mathf.Abs(matrices[n][c]-bare.Frame.Nodes[n].transform.Matrix[c]));
                Require(identityError<.00001f,"Исходная стойка сдвигает рисунок: "+identityError);
                var anchors=PonyPoseMath.Worlds(document,rest);int head=Array.FindIndex(document.joints,j=>j.id=="head");
                PonyPoseMath.Reanchor(document,head,PonyPoseMath.Position(anchors[head])+new Vector2(.03f,.02f));
                var calibrated=PonyPoseMath.Worlds(document,PonyPoseMath.Rest(document));
                for(int n=0;n<anchors.Length;n++)if(n!=head)Require(Vector2.Distance(PonyPoseMath.Position(anchors[n]),PonyPoseMath.Position(calibrated[n]))<.00001f,"Настройка сустава сдвигает соседние точки");
                foreach(var delta in PonyPoseMath.SocketDeltas(document,PonyPoseMath.Rest(document)).Values)
                    for(int c=0;c<16;c++)Require(Mathf.Abs(delta[c]-Matrix4x4.identity[c])<.00001f,"Настройка сустава сдвигает рисунок");
                PonyPoseMath.Reanchor(document,head,PonyPoseMath.Position(anchors[head]));rest=PonyPoseMath.Rest(document);
                var values=PonyPoseMath.ClonePose(rest);var limb=PonyAnimationArtwork.Limbs(document).First(l=>document.joints[l.End].id=="frontHoofNear");
                var worlds=PonyPoseMath.Worlds(document,values);var target=PonyPoseMath.Position(worlds[limb.End])+new Vector2(.13f,.22f);
                var reached=PonyPoseMath.SolveTwoBone(document,values,limb.Root,limb.Middle,limb.End,target,1);
                Require(Vector2.Distance(target,reached)<.0001f,"Копыто не достигло доступной цели");
                document.SetKey(.5f,values);document.SetKey(1,rest);document.loop=true;
                Undo.RecordObject(document,"Тест изменения позы");var before=document.ToJson();
                var changed=PonyPoseMath.ClonePose(values);PonyPoseMath.MoveJoint(document,changed,0,PonyPoseMath.Position(worlds[0])+new Vector2(.2f,0));document.SetKey(.5f,changed);
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();Require(document.ToJson()==before,"Отмена не восстановила ключи");
                roundtrip=PonyPoseDocument.FromJson(document.ToJson());
                Require(roundtrip.keys.Length==3 && roundtrip.joints.Length==21,"Потеря ключей или суставов при JSON");
                float wardrobeError=0,compileError=0;
                foreach(float time in new[]{0,.25f,.5f,.75f,1})
                {
                    var sample=roundtrip.Sample(time);var deltas=PonyPoseMath.SocketDeltas(roundtrip,sample);var a=bare.Matrices(roundtrip,sample);var b=armored.Matrices(roundtrip,sample);
                    var compiled=armored.CompileFrame(roundtrip,sample);
                    for(int n=0;n<b.Length;n++)for(int c=0;c<16;c++)
                    {
                        wardrobeError=Mathf.Max(wardrobeError,Mathf.Abs(b[n][c]-(deltas[armored.Frame.Nodes[n].socket]*armored.Frame.Nodes[n].transform.Matrix)[c]));
                        compileError=Mathf.Max(compileError,Mathf.Abs(compiled.nodes[n].transform.Matrix[c]-b[n][c]));
                    }
                    foreach(var matrix in a)for(int c=0;c<16;c++)Require(float.IsFinite(matrix[c]),"Неконечная матрица рисунка");
                }
                Require(wardrobeError<.00001f && compileError<.00005f,"Потеря преобразования одежды или наклона");
                string rig=PonyAnimationExport.RigJson(roundtrip,bare);var parsed=JObject.Parse(rig);
                var clip=JsonUtility.FromJson<PonyRigClip>(parsed["clip"].ToString());
                Require(clip.frames.Length==31 && clip.frames.All(f=>f.nodes.Length==17),"Потеря кадров при экспорте PonyRigClip");
                var frame=bare.CompileFrame(roundtrip,roundtrip.Sample(.5f));
                for(int n=0;n<17;n++)for(int c=0;c<16;c++)Require(Mathf.Abs(clip.frames[15].nodes[n].transform.Matrix[c]-frame.nodes[n].transform.Matrix[c])<.00005f,"Экспорт меняет позу");
                File.WriteAllText(Path.Combine(output,"example-keys.json"),roundtrip.ToJson());File.WriteAllText(Path.Combine(output,"example-rig.json"),rig);
                const string examplePath="Assets/Remains/Editor/PonyAnimation/HoofLift.asset";
                if(!File.Exists(examplePath))
                {
                    var example=Object.Instantiate(roundtrip);example.hideFlags=HideFlags.None;example.clipId="hoof_lift";
                    AssetDatabase.CreateAsset(example,examplePath);AssetDatabase.SaveAssetIfDirty(example);
                }
                var saved=AssetDatabase.LoadAssetAtPath<PonyPoseDocument>(examplePath);Require(saved!=null,"Не загружается пример Unity asset");saved.Validate();
                Require(saved.keys.Length==3 && saved.joints.Length==21,"Unity asset потерял ключи или суставы");
                using(var preview=new PonyAnimationPreview())
                {
                    var camera=new Rect(-1.1f,-.18f,2.5f,2);
                    foreach(var art in new[]{bare,armored})for(int f=0;f<=30;f++)
                    {
                        preview.Render(art,art.Matrices(roundtrip,roundtrip.Sample(f/30f)),camera,625,500);
                        preview.WritePng(Path.Combine(output,(art.Outfit.Length==0?"bare":"armored")+"-"+f.ToString("D2")+".png"));
                    }
                }
                File.WriteAllText(Path.Combine(output,"report.json"),new JObject { ["status"]="PASS",["joints"]=21,["sockets"]=17,["keys"]=3,["renderedImages"]=62,
                    ["restMatrixError"]=identityError,["wardrobeMatrixError"]=wardrobeError,["compileMatrixError"]=compileError,
                    ["ikTargetError"]=Vector2.Distance(target,reached),["undo"]=true,["jointCalibration"]=true,["nativeAsset"]=true,["sourceJsonRoundtrip"]=true,["rigJsonRoundtrip"]=true,
                    ["limits"]="Editor-only; no desktop UI automation, Player or gameplay integration"}.ToString());
                File.WriteAllText(Path.Combine(output,"index.html"),"<!doctype html><meta charset='utf-8'><title>Редактор анимаций пони</title><style>body{background:#252a32;color:white;font:18px sans-serif;margin:24px}img{max-width:49%}</style><h1>Проба управления копытом: исходная стойка → сгиб → исходная стойка</h1><p>Новые ключи поверх существующей графики, один скелет для тела и брони. Сам редактор открывается в Unity: Remains → Пони → Редактор анимаций.</p><button id='p'>Пауза / просмотр</button><input id='t' type='range' min='0' max='30' value='0'><div><img id='a'><img id='b'></div><script>let f=0,play=true;const t=document.getElementById('t');function draw(){a.src='bare-'+String(f).padStart(2,'0')+'.png';b.src='armored-'+String(f).padStart(2,'0')+'.png';t.value=f}t.oninput=()=>{play=false;f=+t.value;draw()};p.onclick=()=>play=!play;setInterval(()=>{if(play){f=(f+1)%31;draw()}},33);draw()</script>");
                Debug.Log($"Редактор анимаций пони: PASS; 21 сустав, 17 гнёзд, IK/FK, Undo, JSON, экспорт 31 кадра, броня; rest error={identityError}, affine error={compileError}; {output}");
            }
            catch(Exception problem){Debug.LogException(problem);exit=1;}
            finally {if(document!=null)Object.DestroyImmediate(document);if(roundtrip!=null)Object.DestroyImmediate(roundtrip);}
            EditorApplication.Exit(exit);
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException("Редактор анимаций: "+message);}
    }
}
