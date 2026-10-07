using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationConnectionAuthoring
    {
        public const float Offset=153.6f,Right=192;
        public const string Connector="Опора верхнего прохода из склада в комнату27";
        public const string Frame="Рамка двух проходов из склада в комнату27";
        public const string PhysicalPatch="base:factory-connected-46-18:physical-v9";
        public const string DoorPatch="base:factory-connected-46-18:doors-v6";
        public const string NpcPatch="base:factory-connected-46-18:npcs-v6";
        [MenuItem("Remains/Контент/Подключить комнату27 к заводу")]
        public static void Connect()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();WeaponCatalog.Load();Anatomy.Load();PropCatalog.Load();ActorTypes.Load();
            var data=FactoryContinuationConnectionData.Load();
            var saved=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryContinuationDraft.Path);
            if(saved==null) throw new InvalidOperationException("Комната27 ещё не сохранена: "+FactoryContinuationDraft.Path);
            FactoryContinuationChecks.Check(saved.GetComponent<RoomAuthoring>());
            FactoryContinuationObjectsChecks.Check(saved.GetComponent<RoomAuthoring>());
            FactoryContinuationNpcChecks.Check(saved.GetComponent<RoomAuthoring>());
            var root=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var before=room.CreateLayout(false);var prior=RoomContentBatch.Read(room,"factory:0");
                if(prior.physical.patch=="factory:0:"+PhysicalPatch)
                {FactoryContinuationConnectionChecks.Check(room);Debug.Log("Комната27 уже подключена; сохранённые данные проверены без замены.");return;}
                if(Mathf.Abs(before.right-Offset)>.001f || root.transform.Find(Connector)!=null || root.GetComponentsInChildren<RoomAuthoring>(true).Length!=5 ||
                    prior.physical.patch!="factory:0:"+FactoryWarehouseFurnitureAuthoring.ConnectedPatch ||
                    prior.doors.patch!="factory:0:"+FactoryWarehouseMetalDoorsPlacement.ConnectedPatch ||
                    prior.npcs.patch!="factory:0:"+FactoryWarehouseConnectionAuthoring.NpcPatch)
                    throw new InvalidOperationException("Комната27: изменены прежние модули/границы/версии v8/v5/v5; автоматическая замена запрещена.");
                FactoryWarehouseConnectionChecks.Check(room);FactoryBoundaryAuthoring.ValidateDerivedContour(room);
                var preview=FactoryContinuationConnectionGeometry.Preview(room,saved.GetComponent<RoomAuthoring>());
                var retained=root.GetComponentsInChildren<RoomAuthoring>(true).Where(r=>r!=room)
                    .Select(r=>(module:r,layout:StateJson.ToJson(r.CreateLayout()),content:StateJson.ToJson(RoomContentBatch.Read(r,"factory:0")),
                        position:r.transform.localPosition,rotation:r.transform.localRotation,scale:r.transform.localScale)).ToArray();
                var addedRoot=(GameObject)PrefabUtility.InstantiatePrefab(saved,root.transform);addedRoot.transform.localPosition=new Vector3(Offset,0,0);
                UnityEngine.Object.DestroyImmediate(root.transform.Find(FactoryBoundaryAuthoring.GroupName).gameObject);
                before.right=Right;room.Configure(before);root.name="Завод · пять соединённых комнат · один уровень";
                FactoryBoundaryAuthoring.BuildSurfaces(room,new[]{data.upperBridge},Connector,"FactoryContinuationBridgeArtwork");
                FactoryBoundaryAuthoring.BuildSurfaces(room,data.seamClosures,Frame,"FactoryContinuationSeamFrameArtwork");
                FactoryBoundaryAuthoring.Build(room,"FactoryExteriorContinuationArtwork");
                FactoryContinuationConnectionGeometry.CheckBuilt(preview,room);
                var added=RoomContentBatch.Read(addedRoot.GetComponent<RoomAuthoring>(),"factory:0");
                var physical=FactoryExpansionAuthoring.Extend(prior.physical.patch,prior.physical.expansion,
                    added.physical.props.Cast<PropState>().Concat(added.physical.containers).Select(p=>p.id).ToArray());
                var p=root.GetComponent<RoomPhysicalContent>();p.Configure(PhysicalPatch);p.ConfigureExpansion(physical.previousPatch,physical.additions,physical.earlierVersions);
                var d=root.GetComponent<RoomDoorContent>();d.Configure(DoorPatch);
                d.ConfigureExpansion(FactoryExpansionAuthoring.Extend(prior.doors.patch,prior.doors.expansion,added.doors.doors.Select(e=>e.id).ToArray()));
                var n=root.GetComponent<RoomNpcContent>();n.Configure(NpcPatch);
                n.ConfigureExpansion(FactoryExpansionAuthoring.Extend(prior.npcs.patch,prior.npcs.expansion,added.npcs.actors.Select(e=>e.id).ToArray()));
                foreach(var old in retained)
                    if(StateJson.ToJson(old.module.CreateLayout())!=old.layout || StateJson.ToJson(RoomContentBatch.Read(old.module,"factory:0"))!=old.content ||
                        old.module.transform.localPosition!=old.position || old.module.transform.localRotation!=old.rotation || old.module.transform.localScale!=old.scale)
                        throw new InvalidOperationException("Подключение комнаты27 изменило прежний модуль: "+old.module.name);
                FactoryContinuationConnectionChecks.Check(room);ConnectedFactoryChecks.Check(room);FactoryOfficeConnectionChecks.Check(room);FactoryWarehouseConnectionChecks.Check(room);
                if(PrefabUtility.SaveAsPrefabAsset(root,ConnectedFactoryAuthoring.Path)==null) throw new InvalidOperationException("Unity не сохранил подключение комнаты27.");
                AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("Комната27 подключена: два прохода, единый уровень до x=192; прежние модули сохранены, контент устанавливается добавочно.");
        }
    }
}
