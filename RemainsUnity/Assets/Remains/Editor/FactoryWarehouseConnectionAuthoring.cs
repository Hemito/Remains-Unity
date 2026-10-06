using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWarehouseConnectionAuthoring
    {
        public const float Offset=115.2f,Right=153.6f;
        public const string Connector="Опора прохода из офисов в склад";
        public const string Frame="Рамка единственного прохода в склад";
        public const string PhysicalPatch="base:factory-connected-46-18:physical-v7";
        public const string DoorPatch="base:factory-connected-46-18:doors-v4";
        public const string NpcPatch="base:factory-connected-46-18:npcs-v5";
        internal static FactoryModuleConnectionData Data()=>FactoryModuleConnectionData.Load(
            "FactoryWarehouse40",40,"base:factory-warehouse-40",Offset,Right);

        [MenuItem("Remains/Контент/Подключить склад40 к заводу")]
        public static void Connect()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();WeaponCatalog.Load();Anatomy.Load();PropCatalog.Load();ActorTypes.Load();
            var data=Data();var warehouse=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWarehouseDraft.Path);
            if(warehouse==null) throw new InvalidOperationException("Склад40 ещё не сохранён: "+FactoryWarehouseDraft.Path);
            FactoryWarehouseChecks.Check(warehouse.GetComponent<RoomAuthoring>());
            FactoryWarehouseNpcChecks.Check(warehouse.GetComponent<RoomAuthoring>());
            var root=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var before=room.CreateLayout(false);
                var prior=RoomContentBatch.Read(room,"factory:0");
                if(prior.physical.patch=="factory:0:"+PhysicalPatch)
                {FactoryWarehouseConnectionChecks.Check(room);Debug.Log("Склад40 уже подключён; авторские данные проверены и сохранены.");return;}
                if(Mathf.Abs(before.right-Offset)>.001f || root.transform.Find(Connector)!=null ||
                    root.GetComponentsInChildren<RoomAuthoring>(true).Length!=4 ||
                    prior.physical.patch!="factory:0:"+FactoryOfficeFurnitureAuthoring.ConnectedPatch ||
                    prior.doors.patch!="factory:0:base:factory-connected-46-18:doors-v3" ||
                    prior.npcs.patch!="factory:0:"+FactoryOfficeNpcAuthoring.ConnectedPatch)
                    throw new InvalidOperationException("Склад40: изменены прежние модули/границы или версии v6/v3/v4; автоматическая замена запрещена.");
                FactoryBoundaryAuthoring.ValidateDerivedContour(room);
                var oldModules=root.GetComponentsInChildren<RoomAuthoring>(true).Where(r=>r!=room)
                    .Select(r=>(module:r,layout:StateJson.ToJson(r.CreateLayout()),content:StateJson.ToJson(RoomContentBatch.Read(r,"factory:0")),
                        position:r.transform.localPosition,rotation:r.transform.localRotation,scale:r.transform.localScale)).ToArray();
                var module=(GameObject)PrefabUtility.InstantiatePrefab(warehouse,root.transform);
                module.transform.localPosition=new Vector3(Offset,0,0);
                UnityEngine.Object.DestroyImmediate(root.transform.Find(FactoryBoundaryAuthoring.GroupName).gameObject);
                before.right=Right;room.Configure(before);root.name="Завод · вход, цех, офисы и склад · один уровень";
                FactoryBoundaryAuthoring.BuildSurfaces(room,new[]{data.connector},Connector,"FactoryWarehouseConnectorArtwork");
                FactoryBoundaryAuthoring.BuildSurfaces(room,data.seamClosures,Frame,"FactoryWarehouseSeamFrameArtwork");
                FactoryBoundaryAuthoring.Build(room,"FactoryExteriorWarehouseArtwork");
                var added=RoomContentBatch.Read(module.GetComponent<RoomAuthoring>(),"factory:0");
                var physical=FactoryExpansionAuthoring.Extend(prior.physical.patch,prior.physical.expansion,
                    added.physical.props.Cast<PropState>().Concat(added.physical.containers).Select(p=>p.id).ToArray());
                var physicalContent=root.GetComponent<RoomPhysicalContent>();physicalContent.Configure(PhysicalPatch);
                physicalContent.ConfigureExpansion(physical.previousPatch,physical.additions,physical.earlierVersions);
                var doors=root.GetComponent<RoomDoorContent>();doors.Configure(DoorPatch);
                doors.ConfigureExpansion(FactoryExpansionAuthoring.Extend(prior.doors.patch,prior.doors.expansion,added.doors.doors.Select(d=>d.id).ToArray()));
                var npcs=root.GetComponent<RoomNpcContent>();npcs.Configure(NpcPatch);
                npcs.ConfigureExpansion(FactoryExpansionAuthoring.Extend(prior.npcs.patch,prior.npcs.expansion,added.npcs.actors.Select(a=>a.id).ToArray()));
                foreach(var old in oldModules)
                    if(StateJson.ToJson(old.module.CreateLayout())!=old.layout || StateJson.ToJson(RoomContentBatch.Read(old.module,"factory:0"))!=old.content ||
                        old.module.transform.localPosition!=old.position || old.module.transform.localRotation!=old.rotation || old.module.transform.localScale!=old.scale)
                        throw new InvalidOperationException("Подключение склада изменило прежний вложенный модуль: "+old.module.name);
                FactoryWarehouseConnectionChecks.Check(room);ConnectedFactoryChecks.Check(room);FactoryOfficeConnectionChecks.Check(room);
                if(PrefabUtility.SaveAsPrefabAsset(root,ConnectedFactoryAuthoring.Path)==null)
                    throw new InvalidOperationException("Unity не сохранил подключение склада40.");
                AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("Склад40 подключён: один уровень до x=153,6; старые модули не изменены, новые ID устанавливаются добавочно.");
        }
    }
}
