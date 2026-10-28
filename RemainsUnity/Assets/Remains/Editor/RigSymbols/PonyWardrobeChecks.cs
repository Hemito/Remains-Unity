using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class PonyWardrobeChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var actor=new ActorState {id="wardrobe-check",actorType="pony",anatomy="pony",player=true,connected=true,appearance=new CharacterAppearance {version=1},weapon=null};
            var definitions=CosmeticCatalog.Current.Definitions.Where(d=>d.Slot=="armor.body").OrderBy(d=>d.Id,StringComparer.Ordinal).ToArray();
            foreach(var definition in definitions)InventoryStacks.Add(actor,SharedInventory.Create(definition.Id,1));
            var view=(PonyVisual)ActorVisualFactory.Create(actor);var cameraRoot=new GameObject("Проверка гардероба");var camera=cameraRoot.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.5f;camera.transform.position=new Vector3(0,.7f,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.21f);
            string output=Path.GetFullPath("../.reference/rig-symbols/wardrobe");Directory.CreateDirectory(output);
            var render=new RenderTexture(256,256,24);var image=new Texture2D(256,256,TextureFormat.RGBA32,false);var sheet=new Texture2D(7*256,3*256,TextureFormat.RGBA32,false);
            float lighting=Shader.GetGlobalFloat("_WorldLightEnabled");var previous=RenderTexture.active;int samples=0,blinks=0,styles=0;
            try
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",0);camera.targetTexture=render;
                for(int index=0;index<definitions.Length;index++)
                {
                    var definition=definitions[index];var item=actor.inventory.Single(i=>i.definition==definition.Id);
                    Require(CosmeticEquipment.TryEquip(actor,item.id,false,out var reason),"Не надет "+definition.Id+": "+reason);
                    actor.id="wardrobe:"+definition.Id;actor.appearance.headStyleId=CharacterAppearance.OriginalHeadStyleId;actor.pose="idle";actor.climbing=false;actor.facing=1;
                    view.Bind(actor);var plan=view.CurrentDrawPlan;
                    foreach(var clip in PonyAnimationData.Library.clips)for(int f=0;f<clip.frames.Length;f++)foreach(int facing in new[]{1,-1})
                    {
                        actor.pose=clip.id;actor.climbing=clip.id=="climb" && f==0;actor.vy=0;actor.facing=facing;
                        float time=(!clip.nonLooping && !clip.playIntro && f>=clip.loopStartFrame)?(f-clip.loopStartFrame+.1f)/clip.fps:(f+.1f)/clip.fps;
                        view.DrawPose(time,true);Require(view.DrawnClip==clip.id && view.DrawnFrame==f,"Сбой sample "+definition.Id+"/"+clip.id+"/"+f);
                        Require(view.DrawnPartCount==plan.Frames(clip.id)[f].Length && view.TryGetDrawnBounds(out _),"Потеря рисунка "+definition.Id+"/"+clip.id);
                        samples++;
                    }
                    actor.pose="idle";actor.climbing=false;actor.facing=1;view.DrawPose(0,true);
                    Capture(definition.Id.Replace("armor.",""));sheet.SetPixels((index%7)*256,(2-index/7)*256,256,256,image.GetPixels());
                    view.AdvanceNestedAnimation(9.85f);view.DrawPose(0);
                    Require(view.DrawnEyeClosed,"Не сработало independent моргание "+definition.Id);
                    var eyes=PonyNestedEyeFrames.Prepare(plan.Frames("idle")[0]);
                    Require(view.DrawnPartCount==eyes.Closed.Length,"Моргание потеряло шлем/части "+definition.Id);blinks++;
                    Capture(definition.Id.Replace("armor.","")+"-blink");
                    view.AdvanceNestedAnimation(.2f);view.DrawPose(0);Require(!view.DrawnEyeClosed,"Не открылся глаз "+definition.Id);
                    foreach(var head in PonyHeadCatalog.Styles)
                    {
                        actor.appearance.headStyleId=head.id;view.Bind(actor);view.DrawPose(0,true);
                        var shape=view.CurrentDrawPlan.Frames("idle")[0].SingleOrDefault(p=>p.sourceShape==PonyHeadRenderResolver.SourceShape);
                        if(shape!=null && head.id!=CharacterAppearance.OriginalHeadStyleId)
                            Require(shape.material.mainTexture==GameAssets.Load<Texture2D>(head.texture),"Шлем сбросил форму головы: "+definition.Id+"/"+head.id);
                        styles++;
                    }
                }
                sheet.Apply();File.WriteAllBytes(Path.Combine(output,"contact.png"),sheet.EncodeToPNG());
                File.WriteAllText(Path.Combine(output,"index.html"),"<!doctype html><meta charset='utf-8'><title>Гардероб Remains: Unity</title><style>body{background:#20252c;color:#eee;font:16px sans-serif}main{display:grid;grid-template-columns:repeat(4,256px);gap:16px}figure{margin:0}img{width:256px}figcaption{text-align:center}</style><h1>21 комплект: исходная графика 1.04</h1><p>Названия — ID комплектов. Шлемы входят в комплект. Защита и способности пока не подключены.</p><main>"+string.Concat(definitions.Select(d=>"<figure><img src='"+d.Id.Replace("armor.","")+".png'><figcaption>"+d.Id+"</figcaption></figure>"))+"</main>");
                Debug.Log($"Гардероб: PASS — комплектов={definitions.Length}, actual samples={samples}, independent blink={blinks}, head combinations={styles}; 31 source node variants, PNG/contact/html в {output}.");
            }
            finally{Shader.SetGlobalFloat("_WorldLightEnabled",lighting);RenderTexture.active=previous;camera.targetTexture=null;render.Release();Object.DestroyImmediate(render);Object.DestroyImmediate(image);Object.DestroyImmediate(sheet);Object.DestroyImmediate(cameraRoot);Object.DestroyImmediate(view.gameObject);}
            void Capture(string name){camera.Render();RenderTexture.active=render;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException("Гардероб: "+reason);}
        public static void RunBatch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}
