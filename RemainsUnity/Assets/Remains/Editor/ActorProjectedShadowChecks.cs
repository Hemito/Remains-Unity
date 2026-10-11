using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorProjectedShadowChecks
    {
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
}
