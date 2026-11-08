using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Factory handoff coverage from the public catalogs, independent of private re-export tools.
    // This is native Editor/domain acceptance; it does not replace Player, LAN, listening or visual gameplay review.
    // Optional -factory-check Name runs only that check; unknown names fail instead of silently passing.
    public static class FactoryContentAcceptanceChecks
    {
        [Serializable] private sealed class Result {public string name;public bool success;public string detail;}
        [Serializable] private sealed class Report {public string unityVersion,utc,selected;public int passed,failed;public List<Result> checks=new();}
        public static void RunBatch()
        {
            var args=Environment.GetCommandLineArgs();int selected=Array.IndexOf(args,"-factory-check");
            var report=new Report {unityVersion=Application.unityVersion,utc=DateTime.UtcNow.ToString("O"),
                selected=selected<0?null:selected+1<args.Length?args[selected+1]:"<missing check name>"};
            Check(report,"FactoryContainerLockChecks",FactoryContainerLockChecks.Run);
            Check(report,"FactoryEnemyScalingChecks",FactoryEnemyScalingChecks.Run);
            Check(report,"MechanismOperationsChecks",MechanismOperationsChecks.Run);
            Check(report,"FactoryWorkshopMechanismChecks",FactoryWorkshopMechanismChecks.Run);
            Check(report,"FactoryMechanismChecks",FactoryMechanismChecks.Run);
            Check(report,"FactoryDoorKindsChecks",FactoryDoorKindsChecks.Run);
            Check(report,"FactoryToolStandChecks",FactoryToolStandChecks.Run);
            Check(report,"FactoryRoomContentChecks",FactoryRoomContentChecks.Run);
            Check(report,"MineChecks",MineChecks.Run);
            Check(report,"HazardChecks",HazardChecks.Run);
            Check(report,"TurretRoomChecks",TurretRoomChecks.Run);
            Check(report,"RaiderChecks",RaiderChecks.Run);
            Check(report,"CreatureChecks",CreatureChecks.Run);
            Check(report,"ContainerChecks",ContainerChecks.Run);
            Check(report,"ContainerLootChecks",ContainerLootChecks.Run);
            Check(report,"ContainerAppearanceChecks",ContainerAppearanceChecks.Run);
            Check(report,"MovableContainerChecks",MovableContainerChecks.Run);
            Check(report,"FactoryRadBarrelChecks",FactoryRadBarrelChecks.Run);
            Check(report,"FactoryCrawlerChecks",FactoryCrawlerChecks.Run);
            Check(report,"FactoryBoundaryChecks",()=>FactoryBoundaryChecks.Check(AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>()));
            Check(report,"FactoryFileCabinetChecks",FactoryFileCabinetChecks.Run);
            Check(report,"DoorBarrierChecks",DoorBarrierChecks.Run);
            Check(report,"DoorCombatChecks",DoorCombatChecks.Run);
            if(report.passed+report.failed==0)
            {
                report.failed=1;report.checks.Add(new Result {name=report.selected,detail="Unknown factory check"});
            }
            // Unity removes Temp during shutdown. Library keeps the report available after EditorApplication.Exit.
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/FactoryVerification/factory-content-acceptance.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log($"FACTORY_CONTENT_ACCEPTANCE: passed={report.passed}, failed={report.failed}; report={output}");
            EditorApplication.Exit(report.failed==0?0:1);
        }
        private static void Check(Report report,string name,Action run)
        {
            if(report.selected!=null && report.selected!=name)return;
            var result=new Result {name=name};
            try
            {
                FactoryContentSources.Clear();
                ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();PropCatalog.Load();
                run();result.success=true;result.detail="PASS";report.passed++;
                Debug.Log("FACTORY_CHECK_PASS: "+name);
            }
            catch(Exception error)
            {
                result.detail=error.ToString();report.failed++;
                Debug.LogError("FACTORY_CHECK_FAIL: "+name+": "+error);
            }
            finally {FactoryContentSources.Clear();report.checks.Add(result);}
        }
    }
}
