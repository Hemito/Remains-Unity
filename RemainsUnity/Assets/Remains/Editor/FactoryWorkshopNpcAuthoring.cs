using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWorkshopNpcAuthoring
    {
        public const string SpawnPath="Assets/Remains/RoomDrafts/FactoryWorkshopRaiderSpawn.asset";
        public const string LootPath="Assets/Remains/RoomDrafts/FactoryWorkshopRaiderInventory.asset";
        public const string PreviousPatch="base:factory-connected-46-18:npcs-v1";
        public const string Patch="base:factory-connected-46-18:npcs-v2";
        public static void InstallAndBuild()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            if(AssetDatabase.LoadMainAssetAtPath(SpawnPath)!=null || AssetDatabase.LoadMainAssetAtPath(LootPath)!=null)
                throw new InvalidOperationException("Встреча цеха уже создана; существующие определения не заменены.");
            var loot=ScriptableObject.CreateInstance<ContainerLootAsset>();
            loot.Configure(new ContainerLootTable {id="base:draft:factory-workshop-raider",version="1",entries=new[]{
                new ContainerLootEntry {id="weapon",definition="pistol",minimum=1,maximum=1,weaponState=new WeaponItemState {rounds=6}},
                new ContainerLootEntry {id="ammunition",definition="p10",minimum=4,maximum=12},
                new ContainerLootEntry {id="currency",definition="caps",minimum=2,maximum=10}}});
            AssetDatabase.CreateAsset(loot,LootPath);
            var spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();spawn.Configure("raider","raider",140,"pistol");spawn.SetInventory(loot);
            AssetDatabase.CreateAsset(spawn,SpawnPath);
            string[] additions;
            var workshop=PrefabUtility.LoadPrefabContents(FactoryWorkshopDraft.Path);
            try
            {
                var report=workshop.GetComponent<RoomImportAudit>().Report;
                var records=report.Records.Where(r=>r.kind=="enl2").ToArray();
                if(records.Length!=5 || records.Any(r=>r.installed) || workshop.GetComponent<RoomNpcContent>()!=null)
                    throw new InvalidOperationException("Не найдены пять неподключённых обычных мест появления врагов цеха.");
                additions=records.Select(r=>"original:plant:18:"+r.code).ToArray();
                foreach(var record in records)
                {
                    var marker=new GameObject("Рейдер · "+record.code);marker.transform.SetParent(workshop.transform,false);
                    marker.transform.localPosition=FactorySourceCoordinates.Placement(record.sourceX,record.sourceY,2);
                    var placement=marker.AddComponent<RoomActorPlacement>();placement.Configure("original:plant:18:"+record.code,"raider",-1,false,null);placement.SetSpawn(spawn);
                    record.installed=true;record.reason="Метка обычного врага; явно выбранная фиксированная рейдерская встреча Unity, единый инвентарь тела. См. FACTORY_ENCOUNTER_SPEC.md.";
                }
                // enl1 has its own unimplemented creature; it must remain visible in the audit.
                foreach(var record in report.Records.Where(r=>r.kind=="enl1"))
                    record.reason="Метка мелкого ползучего: нужны отдельные тип, ресурсы, анатомия и поведение; рейдером не заменяется.";
                workshop.AddComponent<RoomNpcContent>().Configure("base:factory-workshop-18:npcs-v1");
                EditorUtility.SetDirty(report);PrefabUtility.SaveAsPrefabAsset(workshop,FactoryWorkshopDraft.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(workshop);}
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var content=connected.GetComponent<RoomNpcContent>();content.Configure(Patch);content.ConfigureExpansion(PreviousPatch,additions);
                PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            AssetDatabase.SaveAssets();FactoryWorkshopNpcChecks.Run();RoomAuthoringScenes.Verify();PrototypeBuild.Build();
        }
    }
}
