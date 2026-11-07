using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorProjectedShadowChecks
    {
        public static void Run()=>RunTimingChecks();
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
                var flash=new SpriteEmission(sprite,
                    Matrix4x4.Translate(lights[0].Position),8);
                const string id="flash:stable-shot-1";
                presentation.PrepareFlash(level,actors,weapons,id,flash);
                var entries=(System.Collections.IDictionary)typeof(ActorShadowPresentation)
                    .GetField("entries",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presentation);
                object entry=entries[(WorldVisualKind.Actor,"timing")];
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
                CheckBodyStack(level,layout,weapons,presentation,sprite);
                Debug.Log("Время проекции тени: PASS — первый кадр вспышки виден при queued static, лампы плавно сменяются по ID.");
            }
            finally
            {
                presentation?.Dispose();
                foreach(var obj in new UnityEngine.Object[]{actorObject,runnerObject,room,sprite,texture})
                    if(obj!=null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        private static void CheckBodyStack(FactoryLevelView level,RoomLayout layout,InteractionVisuals visuals,
            ActorShadowPresentation presentation,Sprite flashSprite)
        {
            PropCatalog.Load();var artwork=new PropArtwork();
            var wood=PropCatalog.Get("woodbox");var ammo=PropCatalog.Get("ammobox");
            var stack=new[]{(wood,Vector2.zero),(wood,new Vector2(.08f,wood.height)),(ammo,new Vector2(.16f,2*wood.height))};
            // The hub checks ran above. Isolate the stack in a clear optical area so no
            // existing wall masks one of the three independently drawn game sprites.
            var shadowSources=(ActorShadowSources)typeof(FactoryLevelView)
                .GetField("shadowSources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(level);
            shadowSources.Configure(new float[120,90],Vector2.zero,.1f,Matrix4x4.identity,Array.Empty<ActorShadowSource>(),0);
            var start=new Vector2(5,2);
            var fromLight=start+new Vector2(-2,2*wood.height+ammo.height+.4f);
            level.SetViewArea(new Rect(start-new Vector2(4,1),new Vector2(8,6)));
            var bodies=new PropView[stack.Length];
            var states=new PropState[stack.Length];
            for(int i=0;i<bodies.Length;i++)
            {
                var (definition,offset)=stack[i];string id="stack-"+i;
                Vector2 position=start+offset;
                var view=artwork.CreateView(definition.id,id,definition.width);
                view.transform.SetParent(visuals.transform);
                var state=new PropState{id=id,definition=definition.id,level=layout.id,x=position.x,y=position.y,
                    width=definition.width,height=definition.height};
                states[i]=state;view.DrawMotion(state,"local",false,0);
                var field=typeof(InteractionVisuals).GetField(i==2?"containers":"props",BindingFlags.Instance|BindingFlags.NonPublic);
                ((Dictionary<string,PropView>)field.GetValue(visuals)).Add(id,view);bodies[i]=view;
            }
            var actors=new Dictionary<string,ActorVisual>();
            var flash=new SpriteEmission(flashSprite,Matrix4x4.Translate(fromLight),8);
            const string light="flash:crate-stack";
            var emissions=new[]{flash};var ids=new[]{light};
            presentation.PrepareFlash(level,actors,visuals,light,flash);
            presentation.Present(level,actors,visuals,emissions,ids);
            var masks=new List<ProjectedLightShadow>();presentation.CollectProjectedLightShadows(masks);
            if(masks.Count!=3 || masks.Any(mask=>mask.SourceId!=light || mask.Opacity<=0))
                throw new InvalidOperationException($"Для двух деревянных и одного ящика боеприпасов получено {masks.Count} теней.");
            for(int i=0;i<masks.Count;i++)
                if(masks[i].WorldBounds.center.x<=bodies[i].Artwork.bounds.center.x+.05f)
                    throw new InvalidOperationException("Проекция ящика не направлена от источника.");
            if(!masks[0].WorldBounds.Overlaps(masks[1].WorldBounds) || !masks[1].WorldBounds.Overlaps(masks[2].WorldBounds))
                throw new InvalidOperationException("Стопка не проверяет перекрывающиеся проекции.");
            var original=masks.ToArray();
            presentation.Present(level,actors,visuals,emissions,ids);presentation.CollectProjectedLightShadows(masks);
            if(!masks.Select(mask=>mask.Revision).SequenceEqual(original.Select(mask=>mask.Revision)))
                throw new InvalidOperationException("Неподвижная стопка перестроила маски.");
            states[1].x+=.4f;bodies[1].DrawMotion(states[1],"local",false,0);
            presentation.Present(level,actors,visuals,emissions,ids);presentation.CollectProjectedLightShadows(masks);
            if(masks.Count!=3 || masks[1].WorldBounds.center.x<=original[1].WorldBounds.center.x+.3f || masks[1].Revision<=original[1].Revision)
                throw new InvalidOperationException("Проекция осталась на старом месте после движения ящика.");
            bodies[2].SetVisible(false);
            presentation.Present(level,actors,visuals,emissions,ids);presentation.CollectProjectedLightShadows(masks);
            if(masks.Count!=2 || masks.Any(mask=>mask.Mask==original[2].Mask))
                throw new InvalidOperationException("Скрытый ящик оставил проекцию.");
            Debug.Log($"Object projected shadows PASS: two woodbox props and ammobox container, source {fromLight}, overlap, cache and visibility.");
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
                foreach(WorldVisualCategory category in Enum.GetValues(typeof(WorldVisualCategory)))
                    if(WorldVisualOrderPolicy.FirstOrder(category)<=WorldVisualOrderPolicy.ActorShadowOrder)
                        throw new InvalidOperationException("Тень рисуется поверх категории: "+category);
                var layout=RoomLayout.Load("hub");room=AuthoredRoomVisual.Create("hub",layout);
                var view=room.GetComponent<FactoryLevelView>();view.Focus(new Vector2(10,5));
                var sources=new List<ActorShadowSource>();Vector2 actor=default;
                var lampIds=new HashSet<string>(view.LampIds,StringComparer.Ordinal);string lamp=null;
                // Windows are shadow sources too; the broken-lamp case needs a lamp.
                for(float y=2;y<(float)layout.top-1 && lamp==null;y+=3)
                    for(float x=2;x<(float)layout.right-1 && lamp==null;x+=4)
                        {actor=new Vector2(x,y);view.SelectActorShadowSources(actor,sources);lamp=sources.Find(source=>lampIds.Contains(source.Id)).Id;}
                if(lamp==null) throw new InvalidOperationException("Ни одна настоящая лампа хаба не выбрана для проекции тени.");
                long before=view.ShadowSourceRevision;
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
        public override void ApplyHeldAppearance(HeldPropColorSource colors)=>throw new InvalidOperationException("Диагностическое представление времени теней не поддерживает удержание.");
        public SpriteRenderer Artwork;
        public override string ActorId=>"timing";
        public override void Bind(ActorState actor) { }
        public override void AppendLightOccluders(List<LightOccluder> output)=>AppendSpriteOccluder(Artwork,output);
    }
}
