using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class LightQueueAcceptanceChecks
    {
        [Serializable] public sealed class Case {public string name,status,detail;}
        [Serializable] public sealed class Report {public string revision,utc,device;public List<Case> cases=new();}
        public static void RunBatch()
        {
            Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();PonyVisual.Prepare();
            var report=new Report {revision=Environment.GetEnvironmentVariable("REMAINS_TEST_REVISION"),utc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName};
            int failed=0;
            void Check(string name,Action action)
            {
                var item=new Case{name=name,status="PASS",detail=""};
                try {action();}catch(Exception error){failed++;item.status="FAIL";item.detail=error.GetType().Name+": "+error.Message;}
                report.cases.Add(item);Debug.Log("LIGHT_QUEUE_CASE "+name+" "+item.status+" "+item.detail);
            }
            Check("world light and explosion",WorldLightChecks.Run);
            Check("horn brightness and occlusion",HornLightChecks.Run);
            Check("transition door",TransitionDoorLightChecks.Run);
            Check("room light authoring",RoomLightAuthoringChecks.Run);
            Check("room light preparation",RoomLightingPreparationChecks.Run);
            Check("incremental light publication",RoomLightingUpdateChecks.Run);
            Check("actor shadow source",ActorShadowSourceChecks.Run);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Verification/light-queue"));Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"results.json"),JsonUtility.ToJson(report,true));
            Debug.Log("LIGHT_QUEUE_SUMMARY total="+report.cases.Count+" failed="+failed);EditorApplication.Exit(failed==0?0:1);
        }
    }
}
