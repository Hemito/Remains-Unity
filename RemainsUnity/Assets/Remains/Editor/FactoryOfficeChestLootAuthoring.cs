using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Initial content only. Saved inventories are never inspected or changed by this editor command.
    public static class FactoryOfficeChestLootAuthoring
    {
        public const string SourceId="original:plant:53:KGFzJ42eGXsViOgQ";
        public const string AssetPath="Assets/Remains/RoomDrafts/Containers/office53-chest-loot.asset";
        public static ContainerLootTable CreateLoot()
        {
            ContainerLootEntry Entry(string id,int count)=>new() {id=id,definition=id,minimum=count,maximum=count};
            return new ContainerLootTable {id="base:office53-chest",version="1",
                entries=new[]{Entry("caps",12),Entry("p10",12),Entry("pin",1),Entry("metal_comp",1)}};
        }
        public static ContainerLootAsset Asset()
        {
            var asset=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(AssetPath);
            if(asset==null || StateJson.ToJson(asset.Read())!=StateJson.ToJson(CreateLoot()))
                throw new InvalidOperationException("Сундук офисов53: нет точной таблицы base:office53-chest/v1; авторские данные не заменены: "+AssetPath);
            return asset;
        }
        // Recognize exactly this metadata addition, never an arbitrary extra binding/count.
        internal static int BindingCount(PhysicalLayout source)
        {
            string id=source.level+":"+SourceId;
            var bindings=source.loot.Where(b=>b.container==id).ToArray();
            if(bindings.Length==0) return 0;
            if(bindings.Length!=1 || StateJson.ToJson(bindings[0].table)!=StateJson.ToJson(CreateLoot()) ||
                source.containers.Count(c=>c.id==id && c.definition=="chest" && c.items.Count==0)!=1)
                throw new InvalidOperationException("Сундук офисов53: изменена таблица/версия/начальное содержимое: "+id);
            return 1;
        }
        // Historical test projection only: old stages intentionally had no chest initial binding.
        internal static PhysicalLayout WithoutBinding(PhysicalLayout source)
        {
            BindingCount(source);var result=StateJson.Copy(source);
            result.loot=result.loot.Where(b=>b.container!=source.level+":"+SourceId).ToArray();return result;
        }
        public static void Check(RoomAuthoring room)
        {
            var source=RoomPhysicalContent.Read(room,"factory:0");
            if(BindingCount(source)!=1) throw new InvalidOperationException("Сундук офисов53: новый авторский binding отсутствует в "+room.name);
            var marker=Marker(room);var serialized=new SerializedObject(marker);
            if(serialized.FindProperty("loot").objectReferenceValue!=Asset())
                throw new InvalidOperationException("Сундук офисов53: marker не ссылается на единственную таблицу: "+room.name);
        }
        [MenuItem("Remains/Контент/Установить начальный лут сундука офисов53")]
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();PropCatalog.Load();WeaponCatalog.Load();
            var asset=Asset();var icons=new InventoryArtwork();
            foreach(var entry in CreateLoot().entries) icons.Require(entry.definition);
            var officeAsset=Require(FactoryOfficeDraft.Path);var connectedAsset=Require(ConnectedFactoryAuthoring.Path);
            var officeRoom=officeAsset.GetComponent<RoomAuthoring>();var connectedRoom=connectedAsset.GetComponent<RoomAuthoring>();
            var officeMarker=Marker(officeRoom);var connectedMarker=Marker(connectedRoom);
            if(RoomPhysicalContent.Read(officeRoom,"factory:0").patch!="factory:0:"+FactoryOfficeFurnitureAuthoring.OfficePatch ||
                RoomPhysicalContent.Read(connectedRoom,"factory:0").patch!="factory:0:"+FactoryOfficeFurnitureAuthoring.ConnectedPatch ||
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(connectedMarker)!=officeMarker)
                throw new InvalidOperationException("Сундук офисов53: updater требует существующие office-v3/root-v6 и исходный nested marker; частичная/ручная замена не выполнена.");
            var overrideProperty=new SerializedObject(connectedMarker).FindProperty("loot");
            if(overrideProperty.prefabOverride && overrideProperty.objectReferenceValue!=asset)
                throw new InvalidOperationException("Сундук офисов53: ручной loot override связного уровня не заменён: "+ConnectedFactoryAuthoring.Path);
            string officeBefore=Snapshot(officeRoom),connectedBefore=Snapshot(connectedRoom);
            var office=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
            try
            {
                var room=office.GetComponent<RoomAuthoring>();var marker=Marker(room);var prior=marker.ReadLoot();
                if(prior==null)
                {
                    marker.SetLoot(asset);
                    Check(room);AssertUnchanged(officeBefore,room);
                    if(PrefabUtility.SaveAsPrefabAsset(office,FactoryOfficeDraft.Path)==null)
                        throw new InvalidOperationException("Сундук офисов53: Unity не сохранил initial binding.");
                    AssetDatabase.SaveAssets();
                }
                else Check(room); // Idempotent; a different existing table was rejected by Snapshot/BindingCount.
            }
            finally {PrefabUtility.UnloadPrefabContents(office);}
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=connected.GetComponent<RoomAuthoring>();Check(room);AssertUnchanged(connectedBefore,room);
                // Inheritance from the existing office prefab supplies the binding; no nested override is authored.
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Сундук офисов53: сохранён только initial binding caps×12, p10×12, pin×1, metal_comp×1. Стабильный ID, office-v3/root-v6 и прежние авторские данные сохранены; существующие миры и инвентари этой командой не меняются.");
        }
        private static RoomContainerPlacement Marker(RoomAuthoring room)
        {
            var markers=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(room.Included).Where(m=>m.Read(room).id==SourceId).ToArray();
            if(markers.Length!=1 || markers[0].Definition.CreateDefinition().id!="chest" || markers[0].ReadInitialItems().Length!=0)
                throw new InvalidOperationException("Сундук офисов53: нужен один прежний marker32/chest с пустыми initialItems: "+room.name);
            return markers[0];
        }
        private sealed class AuthoringSnapshot
        {
            public RoomLayout geometry;
            public RoomContentBatch batch;
        }
        internal static string Snapshot(RoomLayout geometry,RoomContentBatch batch)
        {
            var detached=StateJson.Copy(batch);detached.physical=WithoutBinding(detached.physical);
            return StateJson.ToJson(new AuthoringSnapshot {geometry=geometry,batch=detached});
        }
        private static string Snapshot(RoomAuthoring room)=>Snapshot(room.CreateLayout(),RoomContentBatch.Read(room,"factory:0"));
        internal static void AssertUnchanged(string before,RoomLayout geometry,RoomContentBatch batch)
        {
            if(before!=Snapshot(geometry,batch)) throw new InvalidOperationException("Сундук офисов53: binding изменил прежние объекты/начальные предметы/геометрию/NPC/двери/механизмы/версии.");
        }
        private static void AssertUnchanged(string before,RoomAuthoring room)=>AssertUnchanged(before,room.CreateLayout(),RoomContentBatch.Read(room,"factory:0"));
        private static GameObject Require(string path)=>AssetDatabase.LoadAssetAtPath<GameObject>(path)??
            throw new InvalidOperationException("Сундук офисов53: отсутствует существующий prefab: "+path);
    }
}
