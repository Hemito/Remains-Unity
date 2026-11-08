using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // TD14 + existing TD06 contracts only. Controlled publication/I/O/replay fixtures do not prove power-loss,
    // cross-peer atomicity or host migration. All disk fixtures belong to this invocation's unique directory.
    public static class ContentSaveAcceptanceChecks
    {
        [Serializable] private sealed class Result {public string name,detail;public bool success;}
        [Serializable] private sealed class Report
        {
            public string utc,unityVersion,platform,fixtureDirectory;
            public bool fixturePreserved;
            public int passed,failed;
            public List<Result> checks=new();
        }
        public static void RunBatch()
        {
            var report=new Report {utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                platform=Application.platform.ToString(),fixtureDirectory=Path.Combine(Path.GetTempPath(),"remains-content-save-acceptance-"+Guid.NewGuid().ToString("N"))};
            Directory.CreateDirectory(report.fixtureDirectory);
            Check(report,"RoomContentPublicationChecks",RoomContentPublicationChecks.Run);
            Check(report,"EntryPreparationChecks",EntryPreparationChecks.Run);
            Check(report,"EntryGeometryChecks",EntryGeometryChecks.Run);
            Check(report,"RecoveryDurabilityChecks",()=>RecoveryDurabilityChecks.RunAt(Path.Combine(report.fixtureDirectory,"durability")));
            Check(report,"OrderedSaveWriteChecks",OrderedSaveWriteChecks.Run);
            Check(report,"LocalRecoveryPairChecks",()=>LocalRecoveryPairChecks.RunAt(Path.Combine(report.fixtureDirectory,"local-pair")));
            if(report.failed==0)Directory.Delete(report.fixtureDirectory,true);
            else report.fixturePreserved=true;
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/FactoryVerification/content-save-acceptance.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log($"CONTENT_SAVE_ACCEPTANCE: passed={report.passed}, failed={report.failed}, ownFixturesPreserved={report.fixturePreserved}; report={output}");
            EditorApplication.Exit(report.failed==0?0:1);
        }
        private static void Check(Report report,string name,Action run)
        {
            var result=new Result {name=name};
            try
            {
                FactoryContentSources.Clear();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();PropCatalog.Load();
                run();result.success=true;result.detail="PASS";report.passed++;
                Debug.Log("CONTENT_SAVE_CHECK_PASS: "+name);
            }
            catch(Exception error)
            {
                result.detail=error.ToString();report.failed++;
                Debug.LogError("CONTENT_SAVE_CHECK_FAIL: "+name+": "+error);
            }
            finally {FactoryContentSources.Clear();report.checks.Add(result);}
        }
    }
}
