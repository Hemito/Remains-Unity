using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Diagnostic composition only. No prefab/scene/save/network/runtime renderer is changed.
    public static class PonyRigPilotPreview
    {
        private const int Size=420;
        private sealed class Draw {public PonyRigPilotArt.Piece Part;public Matrix4x4 Matrix;public int Rank;public string Socket;public float Brightness;}
        public static void RunBatch()
        {
            int exit=0;float oldLight=Shader.GetGlobalFloat("_WorldLightEnabled");PonyRigPilotArt art=null;PonyRigPilotStage stage=null;
            try
            {
                string source=Argument("-rigSource")??Path.GetFullPath("../.reference/rig-pilot/source");
                string output=Argument("-rigOutput")??Path.GetFullPath("../.reference/rig-pilot/preview");Directory.CreateDirectory(output);
                var rig=new GodotRigPilotData(source);art=new PonyRigPilotArt(rig);stage=new PonyRigPilotStage();
                float identityError=0,attachmentError=0;int drawReferences=0;
                var bounds=new Bounds();bool hasBounds=false;var captures=new List<(string Id,int Frame)>();
                var export=new JObject { ["schema"]=1,["sourceCommit"]=GodotRigPilotData.SourceCommit,["fps"]=30,["rootScale"]=rig.RootScale,
                    ["mode"]="delta retarget of source sibling nodes onto own registered rest artwork; no source events or nested face/tail playback",
                    ["bindings"]=JObject.FromObject(GodotRigPilotData.SocketBones),["clips"]=new JArray()};
                foreach(var clip in rig.Clips.Values)
                {
                    var frames=new JArray();
                    for(int f=0;f<clip.Frames.Length;f++)
                    {
                        captures.Add((clip.Id,f));var nodes=new JArray();
                        foreach(var socket in GodotRigPilotData.SocketBones)
                        {
                            int b=rig.Indices[socket.Value];var matrix=clip.Frames[f].Delta[b];
                            if(clip.Id=="body_stay")for(int i=0;i<16;i++)identityError=Math.Max(identityError,Math.Abs(matrix[i]-Matrix4x4.identity[i]));
                            nodes.Add(new JObject { ["socket"]=socket.Key,["library"]=PonyRigPilotArt.ArtworkLibrary(socket.Key,clip.Frames[f].Libraries[b]),
                                ["brightnessGain"]=clip.Frames[f].Brightness[b],["matrix"]=new JArray(matrix.m00,matrix.m10,matrix.m01,matrix.m11,matrix.m03,matrix.m13)});
                        }
                        frames.Add(new JObject {["nodes"]=nodes,["coverInFront"]=clip.Frames[f].CoverInFront});
                        foreach(bool worn in new[]{false,true})foreach(var draw in Draws(rig,art,clip,f,worn,1))
                        {
                            drawReferences++;foreach(var vertex in draw.Part.Mesh.vertices)
                            {var point=draw.Matrix.MultiplyPoint3x4(vertex);if(!hasBounds){bounds=new Bounds(point,Vector3.zero);hasBounds=true;}else bounds.Encapsulate(point);}
                        }
                    }
                    ((JArray)export["clips"]).Add(new JObject {["id"]=clip.Id,["loop"]=clip.Loop,["context"]=clip.Context,["order"]=JArray.FromObject(clip.Order),["frames"]=frames});
                }
                if(identityError>1e-5f)throw new InvalidDataException("Пилот rig: rest delta не единичная: "+identityError);
                // A sleeve and its body node must use precisely the same full affine matrix, including skew and reflection.
                foreach(var clip in rig.Clips.Values)for(int f=0;f<clip.Frames.Length;f++)foreach(int facing in new[]{1,-1})
                {
                    var naked=Draws(rig,art,clip,f,false,facing);var suited=Draws(rig,art,clip,f,true,facing);
                    foreach(var item in suited.Where(d=>d.Part.Name.Contains("/wear:")))
                        if(!naked.Any(n=>n.Socket==item.Socket && SameMatrix(n.Matrix,item.Matrix)))throw new InvalidDataException("Пилот: одежда движется отдельной матрицей от своего узла тела "+item.Part.Name);
                }
                float half=Math.Max(Math.Abs(bounds.min.x),Math.Abs(bounds.max.x))+.12f;bounds.SetMinMax(new Vector3(-half,bounds.min.y-.12f,-1),new Vector3(half,bounds.max.y+.12f,1));
                // Square viewport uses a fixed common envelope for every pose, suit and facing.
                float extent=Math.Max(bounds.size.x,bounds.size.y)*.5f;bounds=new Bounds(bounds.center,new Vector3(extent*2,extent*2,2));
                Shader.SetGlobalFloat("_WorldLightEnabled",0);
                foreach(var capture in captures)foreach(bool worn in new[]{false,true})foreach(int facing in new[]{1,-1})
                {
                    var image=Render(Draws(rig,art,rig.Clips[capture.Id],capture.Frame,worn,facing),bounds,stage,rig.Clips[capture.Id],capture.Frame,facing);
                    try {File.WriteAllBytes(Path.Combine(output,Name(capture.Id,capture.Frame,worn,facing)),image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
                }
                File.WriteAllText(Path.Combine(output,"rig.json"),export.ToString(Formatting.Indented));
                var report=new JObject {["sourceCommit"]=GodotRigPilotData.SourceCommit,["sourceBones"]=20,["boundSockets"]=GodotRigPilotData.SocketBones.Count,
                    ["uniqueMeshes"]=art.MeshCount,["symbolVariants"]=art.Bundles.Count,["poseFrames"]=captures.Count,["renderedImages"]=captures.Count*4,
                    ["drawReferences"]=drawReferences,["skewedKeys"]=rig.SkewedKeys,["interpolatedSamples"]=rig.InterpolatedSamples,["hierarchyMatrixMaxError"]=rig.HierarchyMatrixMaxError,
                    ["restMatrixMaxError"]=identityError,["restMeshMaxError"]=art.RestError,["wearableRestMaxError"]=art.WearableRestError,
                    ["sleeveMatrixMismatch"]=attachmentError,["outfit"]=PonyRigPilotArt.Outfit,
                    ["limits"]="Editor-only retarget; own bare/suit artwork. Source inner face/tail, arbitrary events, wings and gameplay/hitboxes not imported. Ladder cycle/scrub, ledge ranges and cover-layer recipes are explicit preview drivers; stage props are schematic. Per-bone RGB brightness imported, alpha unchanged. No original-runtime parity claim."};
                File.WriteAllText(Path.Combine(output,"report.json"),report.ToString(Formatting.Indented));
                var hashes=new JObject();
                foreach(string file in new[]{"index.json","hierarchy.json"}.Concat(GodotRigPilotData.SourceIds.Select(id=>id+".json")))
                {using var sha=SHA256.Create();hashes[file]=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(source,file)))).Replace("-","").ToLowerInvariant();}
                File.WriteAllText(Path.Combine(output,"source-hashes.json"),hashes.ToString(Formatting.Indented));
                Contact(output);Contact(output,true);Html(output,rig);Debug.Log("Пилот гнёзд готов: "+report.ToString(Formatting.None));
            }
            catch(Exception error){Debug.LogException(error);exit=1;}
            finally{stage?.Dispose();art?.Dispose();Shader.SetGlobalFloat("_WorldLightEnabled",oldLight);}
            EditorApplication.Exit(exit);
        }
        private static bool SameMatrix(Matrix4x4 a,Matrix4x4 b){for(int i=0;i<16;i++)if(a[i]!=b[i])return false;return true;}
        private static List<Draw> Draws(GodotRigPilotData rig,PonyRigPilotArt art,GodotRigPilotData.Clip clip,int frame,bool worn,int facing)
        {
            var result=new List<Draw>();var mirror=Matrix4x4.Scale(new Vector3(facing,1,1));
            foreach(var socket in GodotRigPilotData.SocketBones)
            {
                int b=rig.Indices[socket.Value];var state=clip.Frames[frame];string library=PonyRigPilotArt.ArtworkLibrary(socket.Key,state.Libraries[b]);
                var bundle=art.Bundles[socket.Key+"/"+library];
                string drawBone=socket.Key=="pipbuckNear"?"pip1":socket.Key=="pipbuckFar"?"pip2":socket.Value;
                int rank=Array.IndexOf(clip.Order,drawBone);if(rank<0)throw new InvalidDataException("Нет draw bone "+drawBone);
                var matrix=mirror*state.Delta[b];
                foreach(var part in bundle.Body)if(!worn || part.BodyVisibleWhenWorn)result.Add(new Draw {Part=part,Matrix=matrix,Rank=rank,Socket=socket.Key,Brightness=state.Brightness[b]});
                if(worn)foreach(var part in bundle.Worn)result.Add(new Draw {Part=part,Matrix=matrix,Rank=rank,Socket=socket.Key,Brightness=state.Brightness[b]});
            }
            result.Sort((a,b)=>a.Rank!=b.Rank?a.Rank.CompareTo(b.Rank):WearableDrawPlan.ComparePath(a.Part.Order,b.Part.Order));return result;
        }
        private static Texture2D Render(List<Draw> draws,Bounds bounds,PonyRigPilotStage stage,GodotRigPilotData.Clip clip,int frame,int facing)
        {
            var target=new RenderTexture(Size,Size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var command=new CommandBuffer {name="Пилот гнёзд: зарегистрированные символы"};var previous=RenderTexture.active;
            try
            {
                command.SetRenderTarget(target);command.ClearRenderTarget(false,true,new Color(.13f,.14f,.17f,1));
                command.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(bounds.min.x,bounds.max.x,bounds.min.y,bounds.max.y,-10,10),false));
                stage.Draw(command,clip,frame,facing,false);
                var block=new MaterialPropertyBlock();
                foreach(var draw in draws)
                {
                    var tint=draw.Part.Tint;var dimmed=new Color(tint.r*draw.Brightness,tint.g*draw.Brightness,tint.b*draw.Brightness,tint.a);
                    if(dimmed.a!=tint.a)throw new InvalidDataException("Пилот: яркость изменила alpha "+draw.Socket);
                    block.Clear();block.SetTexture("_MainTex",draw.Part.Material.mainTexture);block.SetColor("_Color",dimmed);
                    block.SetFloat("_Emission",draw.Part.Slot=="magic"?1:0);block.SetFloat("_ArtworkVisibilityEnabled",0);
                    command.DrawMesh(draw.Part.Mesh,draw.Matrix,draw.Part.Material,0,0,block);
                }
                stage.Draw(command,clip,frame,facing,true);
                Graphics.ExecuteCommandBuffer(command);RenderTexture.active=target;
                var image=new Texture2D(Size,Size,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();return image;
            }
            finally{command.Release();RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);}
        }
        private static string Name(string id,int f,bool worn,int facing)=>$"{id}-{f:D2}-{(worn?"pip":"bare")}-{(facing==1?"right":"left")}.png";
        private static void Contact(string output,bool extra=false)
        {
            var rows=extra?new[]{("ladder_up",0),("ladder_up",5),("edge_stand_front",10),("edge_stand_back",10),("hide1",15),("hide2",25),("hide3",25)}:
                new[]{("body_stay",0),("body_trot",0),("body_trot",4),("body_trot",8),("body_trot",12),("body_sit_down",3),("body_sit",0)};
            var sheet=new Texture2D(Size*4,Size*rows.Length,TextureFormat.RGBA32,false);
            try
            {
                for(int row=0;row<rows.Length;row++)for(int col=0;col<4;col++)
                {
                    var image=new Texture2D(2,2);try {image.LoadImage(File.ReadAllBytes(Path.Combine(output,Name(rows[row].Item1,rows[row].Item2,col%2==1,col<2?1:-1))));sheet.SetPixels32(col*Size,(rows.Length-1-row)*Size,Size,Size,image.GetPixels32());}finally{Object.DestroyImmediate(image);}
                }
                sheet.Apply();File.WriteAllBytes(Path.Combine(output,extra?"contact-extra.png":"contact.png"),sheet.EncodeToPNG());
            }
            finally{Object.DestroyImmediate(sheet);}
        }
        private static void Html(string output,GodotRigPilotData rig)
        {
            var counts=string.Join(",",rig.Clips.Select(p=>"'"+p.Key+"':"+p.Value.Frames.Length));
            var loops=string.Join(",",rig.Clips.Select(p=>"'"+p.Key+"':"+(p.Value.Loop?"true":"false")));
            var contexts=string.Join(",",rig.Clips.Select(p=>"'"+p.Key+"':'"+p.Value.Context+"'"));
            var html=new StringBuilder("<!doctype html><meta charset=utf-8><title>Пилот гнёзд</title><style>body{background:#17191f;color:#eee;font:17px sans-serif;margin:24px}img{width:24%;max-width:420px}button,select,input{font:inherit;margin:8px}</style><h1>Пилот сборного пони</h1><p>Движение из GitLab rig; свои рисунки тела и костюма. Столбцы: тело / костюм ПипБака / тело влево / костюм влево.</p><select id=clip>");
            foreach(var clip in rig.Clips)html.Append("<option value='"+clip.Key+"'"+(clip.Key=="ladder_up"?" selected":"")+">"+PonyRigPilotSets.Labels[clip.Key]+"</option>");
            html.Append("</select><button id=play>Пауза / воспроизведение</button><input id=frame type=range min=0 value=0><span id=label></span><p id=hint></p><div><img id=a><img id=b><img id=c><img id=d></div><p>Лестница, край и ящик нарисованы схематично для проверки слоёв. Физика и обнаружение опоры здесь не работают. При укрытии ящик перекрывает персонажа; яркость меняет RGB, а не прозрачность.</p><p>Вложенная мимика/хвост, произвольные события источника и gameplay пока не перенесены. В игру не включено.</p><p><a href=contact.png>Базовые позы</a> · <a href=contact-extra.png>Лестница, край, укрытия</a> · <a href=report.json>Проверки</a> · <a href=rig.json>Данные адаптера</a></p><script>const counts={"+counts+"},loops={"+loops+"},contexts={"+contexts+"};const clip=document.getElementById('clip'),slider=document.getElementById('frame');let n=0,playing=true,last=0;function draw(){const id=clip.value;slider.max=counts[id]-1;slider.value=n;document.getElementById('label').textContent=(n+1)+' / '+counts[id];document.getElementById('hint').textContent=contexts[id]==='edge'?'Ползунок задаёт степень свисания: это выбор позы, а не цикл движения.':contexts[id]==='box'?'Вход → удержание укрытия 0,5 с → выход. Проверка перестановки ящика и персонажа.':contexts[id]==='ladder'?'Движение вверх/вниз использует цикл шагов; стояние на лестнице — отдельный кадр.':'';['bare-right','pip-right','bare-left','pip-left'].forEach((v,i)=>document.getElementById('abcd'[i]).src=id+'-'+String(n).padStart(2,'0')+'-'+v+'.png')}clip.onchange=()=>{n=contexts[clip.value]==='edge'?6:0;playing=loops[clip.value];draw()};slider.oninput=()=>{playing=false;n=+slider.value;draw()};document.getElementById('play').onclick=()=>playing=!playing;function tick(t){if(playing&&t-last>=1000/30){last=t;if(n+1<counts[clip.value])n++;else if(loops[clip.value])n=0;else playing=false;draw()}requestAnimationFrame(tick)}draw();requestAnimationFrame(tick)</script>");
            File.WriteAllText(Path.Combine(output,"index.html"),html.ToString());
        }
        private static string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null;}
    }
}
