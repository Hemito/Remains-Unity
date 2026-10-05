using System;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcAmmunitionAuthoring
    {
        public static void InstallAndBuild()
        {
            const string path="Assets/Remains/RoomDrafts/FactoryEntryRaiderInventory.asset";
            var inventory=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(path);
            if(inventory==null)
            {
                inventory=ScriptableObject.CreateInstance<ContainerLootAsset>();
                var source=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(FactoryWorkshopNpcAuthoring.LootPath);
                if(source==null) throw new InvalidOperationException("Нет явно заданного инвентаря рейдера цеха.");
                var table=source.Read();table.id="base:draft:factory-entry-raider";inventory.Configure(table);AssetDatabase.CreateAsset(inventory,path);
            }
            var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>("Assets/Remains/RoomDrafts/FactoryRaiderSpawn.asset");
            if(spawn==null) throw new InvalidOperationException("Нет начального состояния входного рейдера.");
            spawn.SetInventory(inventory);EditorUtility.SetDirty(spawn);AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
