using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryOfficeConnectionAuthoring
    {
        public const float Offset=76.8f,Right=115.2f;
        public const string Connector="Рамка прохода из цеха в офисы";
        public const string Frame="Закрытые участки стыка вне выбранного прохода";
        public const string PhysicalPatch="base:factory-connected-46-18:physical-v4";
        public const string DoorPatch="base:factory-connected-46-18:doors-v2";
        public static void Connect()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            WeaponCatalog.Load();Anatomy.Load();
            var connection=FactoryOfficeConnectionData.Load();
            var office=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryOfficeDraft.Path);
            if(office==null) throw new InvalidOperationException("Офисный модуль ещё не сохранён: "+FactoryOfficeDraft.Path);
            var root=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var before=room.CreateLayout(false);
                if(Mathf.Abs(before.right-Offset)>.001f || root.transform.Find(Connector)!=null)
                    throw new InvalidOperationException("Офисное расширение уже установлено либо границы изменены; авторские правки не заменены.");
                FactoryBoundaryAuthoring.ValidateDerivedContour(room);
                var prior=RoomContentBatch.Read(room,"factory:0");
                if(prior.physical.patch!="factory:0:"+FactoryToolStandAuthoring.ConnectedPatch ||
                    prior.doors.patch!="factory:0:base:factory-connected-46-18:doors-v1")
                    throw new InvalidOperationException("Перед подключением офисов нужны проверенные физические объекты v3 и двери v1.");
                var module=(GameObject)PrefabUtility.InstantiatePrefab(office,root.transform);
                module.transform.localPosition=new Vector3(Offset,0,0);
                UnityEngine.Object.DestroyImmediate(root.transform.Find(FactoryBoundaryAuthoring.GroupName).gameObject);
                before.right=Right;room.Configure(before);root.name="Завод · вход, цех и офисы · один уровень";
                // Explicit connector reviewed against the two source grids; no inferred opening or runtime geometry.
                FactoryBoundaryAuthoring.BuildSurfaces(room,new[]{connection.connector},Connector,"FactoryOfficeConnectorArtwork");
                FactoryBoundaryAuthoring.BuildSurfaces(room,connection.seamClosures,
                    Frame,"FactoryOfficeSeamFrameArtwork");
                FactoryBoundaryAuthoring.Build(room,"FactoryExteriorOfficesArtwork");
                var added=RoomContentBatch.Read(module.GetComponent<RoomAuthoring>(),"factory:0");
                var physicalIds=added.physical.props.Cast<PropState>().Concat(added.physical.containers).Select(p=>p.id).ToArray();
                var physical=FactoryExpansionAuthoring.Extend(prior.physical.patch,prior.physical.expansion,physicalIds);
                root.GetComponent<RoomPhysicalContent>().Configure(PhysicalPatch);
                root.GetComponent<RoomPhysicalContent>().ConfigureExpansion(physical.previousPatch,physical.additions,physical.earlierVersions);
                var doorIds=added.doors.doors.Select(d=>d.id).ToArray();
                if(doorIds.Length>0)
                {
                    root.GetComponent<RoomDoorContent>().Configure(DoorPatch);
                    root.GetComponent<RoomDoorContent>().ConfigureExpansion(FactoryExpansionAuthoring.Extend(prior.doors.patch,prior.doors.expansion,doorIds));
                }
                FactoryOfficeConnectionChecks.Check(room);ConnectedFactoryChecks.Check(room);
                PrefabUtility.SaveAsPrefabAsset(root,ConnectedFactoryAuthoring.Path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("Офисы подключены к цеху через средний проход: один уровень до x=115,2, прежние ID сохранены, контур замкнут, новые объекты устанавливаются добавочно.");
        }
    }
}
