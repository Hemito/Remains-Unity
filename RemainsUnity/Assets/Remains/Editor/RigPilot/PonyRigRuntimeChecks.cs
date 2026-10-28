using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class PonyRigRuntimeChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            var actor=new ActorState {id="rig-runtime-check",actorType="pony",anatomy="pony",pose="idle",facing=1,player=true,connected=true,
                appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion},
                inventory=new List<ItemStack> {new() {id="rig-pip",definition="armor.pip",count=1},new() {id="rig-antirad",definition="armor.antirad",count=1}}};
            var view=(PonyVisual)ActorVisualFactory.Create(actor);var occluders=new List<LightOccluder>();int checkedFrames=0;float error=0;
            try
            {
                foreach(string outfit in new[]{"bare","rig-pip","rig-antirad"})
                {
                    if(outfit!="bare" && !CosmeticEquipment.TryEquip(actor,outfit,false,out var reason))throw new InvalidOperationException(reason);
                    view.Bind(actor);int transforms=view.GetComponentsInChildren<Transform>(true).Length;
                    var plan=view.CurrentDrawPlan;var rig=PonyRigDrawPlan.For(plan);
                    foreach(var clip in PonyAnimationData.Library.clips)
                    for(int f=0;f<clip.frames.Length;f++)foreach(int facing in new[]{1,-1})
                    {
                        actor.pose=clip.id;actor.facing=facing;actor.down=false;actor.climbing=clip.id=="climb" && f==0;actor.vy=0;
                        float time=(!clip.nonLooping && !clip.playIntro && f>=clip.loopStartFrame)?(f-clip.loopStartFrame+.1f)/clip.fps:(f+.1f)/clip.fps;
                        actor.poseTime=time;
                        view.DrawPose(time,true);
                        Require(view.DrawnFrame==f && view.DrawnClip==clip.id,"Неверный игровой sample "+clip.id+"/"+f);
                        var parts=plan.Frames(clip.id)[f];var frame=rig.Get(clip.id,f);
                        var actual=view.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled).OrderBy(r=>r.sortingOrder).ToArray();
                        Require(actual.Length==parts.Length,"Потеря частей "+clip.id);
                        for(int p=0;p<parts.Length;p++)
                        {
                            Require(actual[p].GetComponent<MeshFilter>().sharedMesh==parts[p].mesh,"Подмена artwork "+clip.id);
                            foreach(var point in parts[p].points)
                            {
                                var expected=view.transform.TransformPoint(frame.Nodes[frame.PartNodes[p]].transform.Matrix.MultiplyPoint3x4(point));
                                error=Math.Max(error,(actual[p].transform.TransformPoint(point)-expected).magnitude);
                            }
                        }
                        occluders.Clear();view.AppendLightOccluders(occluders);
                        Require(occluders.Count==parts.Count(p=>!PonyMagicRenderRoles.Contains(p)),"Тени не совпали с составом тела/одежды "+clip.id);
                        var expectedBounds=frame.Bounds;expectedBounds.center=new Vector3(actor.BodyPosition.x+expectedBounds.center.x*facing,actor.BodyPosition.y+expectedBounds.center.y,expectedBounds.center.z);
                        Require(view.TryGetDrawnBounds(out var bounds) && Near(bounds,expectedBounds),"Границы куклы расходятся с банком "+clip.id+"/"+f+": actual="+bounds+", expected="+expectedBounds);
                        if(PonyMotionFrames.Sample(actor,time)==f)Require(Near(bounds,PonyRenderSelection.WorldBounds(actor)),"Границы куклы расходятся с камерой "+clip.id);
                        Require(view.GetComponentsInChildren<Transform>(true).Length==transforms,"Покадровое создание гнёзд "+clip.id);
                        checkedFrames++;
                    }
                    Capture(view,actor,outfit);
                }
                Require(error<.00005f,"Renderer теряет affine: "+error);
                // Real frame draws after preparation: geometry/node count must stay constant and managed allocations must not scale with samples.
                actor.pose="walk";actor.climbing=false;actor.down=false;view.DrawPose(0,true);
                int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
                long before=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();
                for(int f=0;f<1000;f++)view.DrawPose(f/30f,true);
                timer.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Require(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes,"Пересоздание геометрии при проигрывании");
                Debug.Log($"Игровая кукла: 1000 смен кадров antirad={timer.Elapsed.TotalMilliseconds:F1} мс, managed={allocated} байт; это Editor, без замера всего завода/Player.");
                Debug.Log($"Игровая кукла: PASS — {checkedFrames} кадров/направлений, без одежды/pip/antirad, общий mesh, полные affine, bounds и состав теней; max error={error:G6}. Геометрия ещё из старого банка; LAN и новые механики укрытия не проверялись.");
            }
            finally{Object.DestroyImmediate(view.gameObject);}
        }
        private static bool Near(Bounds a,Bounds b)=>(a.min-b.min).sqrMagnitude<1e-8 && (a.max-b.max).sqrMagnitude<1e-8;
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException("Проверка игровой куклы: "+reason);}
        private static void Capture(PonyVisual view,ActorState actor,string outfit)
        {
            string output=Path.GetFullPath("../.reference/rig-pilot/game");Directory.CreateDirectory(output);
            var go=new GameObject("Камера проверки куклы");var camera=go.AddComponent<Camera>();
            var target=new RenderTexture(512,512,24);var image=new Texture2D(512,512,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;float lighting=Shader.GetGlobalFloat("_WorldLightEnabled");
            try
            {
                camera.orthographic=true;camera.orthographicSize=1.5f;camera.transform.position=new Vector3(0,.7f,-10);
                camera.backgroundColor=new Color(.16f,.18f,.21f);camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;
                Shader.SetGlobalFloat("_WorldLightEnabled",0);
                foreach(string id in new[]{"idle","walk","climb","crouch","edgeStandFront07","run","jump"})
                {
                    actor.pose=id;actor.climbing=false;actor.facing=1;view.DrawPose(.2f,true);camera.Render();
                    RenderTexture.active=target;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,outfit+"-"+id+".png"),image.EncodeToPNG());
                }
            }
            finally{Shader.SetGlobalFloat("_WorldLightEnabled",lighting);RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
        }
        public static void RunBatch()
        {
            try{Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
