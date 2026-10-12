using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorProjectedShadowChecks
    {
        public static void RunTimingBatch()
        {
            try {RunTimingChecks();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunTimingChecks()
        {
            GameObject room=null,actorObject=null,runnerObject=null;Texture2D texture=null;Sprite sprite=null;
            ActorShadowPresentation presentation=null;
            try
            {
                ActorCastShadowAuthoring.Check();
                var layout=RoomLayout.Load("hub");room=AuthoredRoomVisual.Create("hub",layout);
                var level=room.GetComponent<FactoryLevelView>();level.Focus(new Vector2(10,5));
                var lights=new List<ActorShadowSource>();Vector2 position=default;
                for(float y=2;y<(float)layout.top-1 && lights.Count==0;y+=3)
                    for(float x=2;x<(float)layout.right-1 && lights.Count==0;x+=4)
                    {position=new Vector2(x,y);level.SelectActorShadowSources(position,lights);}
                if(lights.Count==0) throw new InvalidOperationException("Нет доступной лампы для проверки первого кадра вспышки.");
                texture=new Texture2D(1,1,TextureFormat.RGBA32,false,true);texture.SetPixel(0,0,Color.white);texture.Apply();
                sprite=Sprite.Create(texture,new Rect(0,0,1,1),new Vector2(.5f,.5f),1);
                actorObject=new GameObject("Актёр проверки времени тени");actorObject.transform.position=position;
                var renderer=actorObject.AddComponent<SpriteRenderer>();renderer.sprite=sprite;
                var actor=actorObject.AddComponent<ActorShadowTimingVisual>();actor.Artwork=renderer;
                runnerObject=new GameObject("Проверка времени тени");var weapons=runnerObject.AddComponent<InteractionVisuals>();
                presentation=new ActorShadowPresentation(weapons,runnerObject.transform);
                var actors=new Dictionary<string,ActorVisual>{{"timing",actor}};
                var flash=new RoomRadianceField.SpriteEmission(sprite,
                    Matrix4x4.Translate(lights[0].Position),8);
                const string id="flash:stable-shot-1";
                presentation.PrepareFlash(level,actors,weapons,id,flash);
                var entries=(System.Collections.IDictionary)typeof(ActorShadowPresentation)
                    .GetField("entries",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presentation);
                object entry=entries["timing"];
                entry.GetType().GetField("Queued").SetValue(entry,true);
                presentation.Present(level,actors,weapons,new[]{flash},new[]{id});
                var sources=(List<ActorShadowSource>)entry.GetType().GetField("Sources").GetValue(entry);
                var view=(ActorCastShadowView)entry.GetType().GetField("View").GetValue(entry);
                if(!sources.Exists(source=>source.Id==id && source.Strength>0) || view.ActiveSourceCount==0)
                    throw new InvalidOperationException("Первый кадр вспышки потерял тень при ожидающем обновлении статичных источников.");
                var display=new List<ActorShadowSource>{new ActorShadowSource("old",Vector2.zero,.1f,.5f)};
                var target=new[]{new ActorShadowSource("new",Vector2.one,.1f,.5f)};
                ActorShadowPresentation.BlendLampSources(display,target,.01f);
                if(display.Count!=2 || !display.Exists(source=>source.Id=="old" && source.Strength>0) ||
                    !display.Exists(source=>source.Id=="new" && source.Strength>0 && source.Strength<.5f))
                    throw new InvalidOperationException("Лампы переключаются скачком вместо перехода по стабильным ID.");
                ActorShadowPresentation.BlendLampSources(display,target,.08f);
                if(display.Count!=1 || display[0].Id!="new" || display[0].Strength!=.5f)
                    throw new InvalidOperationException("Плавная смена ламп не достигает нового состояния.");
                ActorShadowPresentation.BlendLampSources(display,new[]{new ActorShadowSource("new",new Vector2(3,1),.1f,.5f)},.01f);
                if(Mathf.Abs(display[0].Position.x-1.25f)>.0001f)
                    throw new InvalidOperationException("Эффективный центр частично видимой лампы меняется скачком.");
                Debug.Log("Время проекции тени: PASS — первый кадр вспышки виден при queued static, лампы плавно сменяются по ID.");
            }
            finally
            {
                presentation?.Dispose();
                foreach(var obj in new UnityEngine.Object[]{actorObject,runnerObject,room,sprite,texture})
                    if(obj!=null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        // Incremental player delivery after the full PrototypeBuild/ContractChecks
        // succeeded, for a final scoped shadow-only correction; not a substitute for admission.
        public static void BuildVerifiedPlayer()
        {
            try
            {
                ActorShadowSourceChecks.Run();
                var scenes=EditorBuildSettings.scenes.Where(scene=>scene.enabled).Select(scene=>scene.path).ToArray();
                if(scenes.Length!=1) throw new InvalidOperationException("Ожидалась одна проверенная стартовая сцена.");
                string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Builds/Windows/Remains.exe"));
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=output,
                    target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
                if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Не собрана итоговая версия проецируемых теней: "+report.summary.result);
                Debug.Log("Итоговый плеер проецируемых теней: PASS — "+output);
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunBatch()
        {
            GameObject room=null;
            try
            {
                ActorCastShadowAuthoring.Check();ActorShadowSourceChecks.Run();
                LampShadowChecks.Run();MuzzleFlashLightingChecks.Run();BeamTransportChecks.Run();
                foreach(WorldVisualCategory category in Enum.GetValues(typeof(WorldVisualCategory)))
                    if(WorldVisualOrderPolicy.FirstOrder(category)<=WorldVisualOrderPolicy.ActorShadowOrder)
                        throw new InvalidOperationException("Тень рисуется поверх категории: "+category);
                var layout=RoomLayout.Load("hub");room=AuthoredRoomVisual.Create("hub",layout);
                var view=room.GetComponent<FactoryLevelView>();view.Focus(new Vector2(10,5));
                var sources=new List<ActorShadowSource>();Vector2 actor=default;
                for(float y=2;y<(float)layout.top-1 && sources.Count==0;y+=3)
                    for(float x=2;x<(float)layout.right-1 && sources.Count==0;x+=4)
                        {actor=new Vector2(x,y);view.SelectActorShadowSources(actor,sources);}
                if(sources.Count==0) throw new InvalidOperationException("Ни одна настоящая лампа хаба не выбрана для проекции тени.");
                string lamp=sources[0].Id;long before=view.ShadowSourceRevision;
                view.SyncLamps(new[]{lamp},1,new[]{lamp});view.SelectActorShadowSources(actor,sources);
                if(view.ShadowSourceRevision<=before || sources.Exists(source=>source.Id==lamp))
                    throw new InvalidOperationException("Разбитая лампа хаба сохранила свою проецируемую тень: "+lamp);
                Debug.Log($"Проецируемые тени: PASS — настоящий хаб, удаление разбитой лампы {lamp}, порядок, неизменное поле, вспышки и балки.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(room!=null) UnityEngine.Object.DestroyImmediate(room);}
        }
    }
    // Editor-only fixture: the supplied sprite is the actual drawn pose.
    public sealed class ActorShadowTimingVisual : ActorVisual
    {
        public SpriteRenderer Artwork;
        public override string ActorId=>"timing";
        public override void Bind(ActorState actor) { }
        public override void AppendLightOccluders(List<RoomRadianceField.LightOccluder> output)=>AppendSpriteOccluder(Artwork,output);
    }
}
