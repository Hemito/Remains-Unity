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
    public static class FactoryWarehouseMetalDoorsPlacement
    {
        public const string LocalPatch="base:factory-warehouse-40:doors-v2";
        public const string ConnectedPatch="base:factory-connected-46-18:doors-v5";
        public const string Prefix="original:plant:40:";
        public readonly struct Placement
        {
            public readonly int Record,X,Y;public readonly string Code;
            public string Id=>Prefix+Code;public string RecordId=>Prefix+"record:"+Record;
            public Vector2 Anchor=>FactorySourceCoordinates.Placement(X,Y,1);
            public Placement(int record,int x,int y,string code) {Record=record;X=x;Y=y;Code=code;}
        }
        public static readonly Placement[] Records={new(64,30,15,"e3o3yL9JmJjPLZ1t"),new(71,36,23,"rd8DsY9aYG3qkDra")};
        public static bool IsInstalled(RoomAuthoring room)=>RoomDoorContent.Read(room,"factory:0")?.patch=="factory:0:"+LocalPatch;

        [MenuItem("Remains/Контент/Установить две металлические двери склада")]
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();PropCatalog.Load();ActorTypes.Load();Anatomy.Load();
            var source=FactoryWarehouseSource.Load();ValidateRecords(source);
            var warehouse=FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>();
            var connected=FactoryWarehouseSource.Asset<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>();
            var local=RoomDoorContent.Read(warehouse,"factory:0");var prior=RoomContentBatch.Read(connected,"factory:0");
            if(prior.doors.patch=="factory:0:"+ConnectedPatch)
            {BeforeMetalDoors(prior.doors);RepairInterruptedReport(source);FactoryWarehouseConnectionChecks.Check(connected);Verify();return;}
            bool resumed=local.patch=="factory:0:"+LocalPatch;
            Require(prior.doors.patch=="factory:0:"+FactoryWarehouseConnectionAuthoring.DoorPatch &&
                (resumed || local.patch=="factory:0:"+FactoryWarehouseDraft.DoorPatch),"Неизвестная версия; существующие авторские данные не заменены.");
            // A saved nested module may already contain the two new doors after an interrupted editor command.
            // Only this explicit resume branch accepts v4 with those exact additions; normal projections reject it.
            var predecessor=StateJson.Copy(prior.doors);
            if(resumed)
            {
                CheckNewDoors(predecessor,115.2f);Require(predecessor.doors.Length==19,"Неполный nested resume содержит не19 дверей.");
                predecessor.doors=predecessor.doors.Where(d=>!NewIds(predecessor.level).Contains(d.id)).ToArray();
            }
            FactoryWarehouseHistoryProjection.BeforeWarehouse(predecessor);
            RepairInterruptedReport(source);
            FactoryWarehouseChecks.Check(warehouse);FactoryWarehouseNpcChecks.Check(warehouse);ValidateReport(warehouse,resumed,source);
            string geometry=StateJson.ToJson(connected.CreateLayout()),other=Other(prior);
            var retained=predecessor.doors.Select(d=>(d.id,json:StateJson.ToJson(d))).ToArray();
            var expansion=FactoryExpansionAuthoring.Extend(predecessor.patch,predecessor.expansion,NewIds(predecessor.level));
            FactoryWarehouseMetalDoorAuthoring.Prepare();
            if(!resumed)
            {
                var root=PrefabUtility.LoadPrefabContents(FactoryWarehouseDraft.Path);
                try
                {
                    var room=root.GetComponent<RoomAuthoring>();var before=RoomContentBatch.Read(room,"factory:0");
                    string localGeometry=StateJson.ToJson(room.CreateLayout()),localOther=Other(before);
                    var old=before.doors.doors.Select(d=>(d.id,json:StateJson.ToJson(d))).ToArray();
                    Require(before.doors.patch=="factory:0:"+FactoryWarehouseDraft.DoorPatch && before.doors.doors.Length==2 && before.doors.expansion==null,
                        "Изменена legacy стадия склада перед добавлением.");
                    ValidateReport(room,false,source);
                    foreach(var record in Records)
                    {
                        Require(!room.GetComponentsInChildren<RoomDoorAuthoring>(true).Any(p=>p.Read(room,"factory:0").id==record.Id),"Частичная расстановка не заменена: "+record.Id);
                        var prefab=FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseMetalDoorAuthoring.PrefabPath);
                        var marker=(GameObject)PrefabUtility.InstantiatePrefab(prefab,room.transform);
                        marker.name="Металлическая дверь склада · "+record.Code;marker.transform.localPosition=record.Anchor;
                        marker.AddComponent<RoomDoorAuthoring>().Configure(record.Id,false,FactoryWarehouseMetalDoorAuthoring.SurfaceTexture);
                    }
                    var content=root.GetComponent<RoomDoorContent>();content.Configure(LocalPatch);
                    content.ConfigureExpansion(FactoryExpansionAuthoring.Extend(before.doors.patch,null,NewIds(before.doors.level)));
                    var report=root.GetComponent<RoomImportAudit>().Report;var records=StateJson.Copy(report.Records);
                    foreach(var record in Records)
                    {
                        var entry=records.Single(r=>r.recordId==record.RecordId);entry.installed=true;
                        entry.reason=DecisionReason(record,true);
                    }
                    report.Configure(FactoryWarehouseSource.SourceId,FactoryWarehouseSource.SourceHash,records);EditorUtility.SetDirty(report);
                    var after=RoomContentBatch.Read(room,"factory:0");
                    Require(StateJson.ToJson(room.CreateLayout())==localGeometry && Other(after)==localOther &&
                        old.All(d=>after.doors.doors.Any(a=>a.id==d.id && StateJson.ToJson(a)==d.json)),"Добавление изменило geometry/art/physical/NPC/mechanisms/travel или две прежние двери склада.");
                    FactoryWarehouseChecks.Check(room);CheckLocal(room);
                    Require(PrefabUtility.SaveAsPrefabAsset(root,FactoryWarehouseDraft.Path)!=null,"Unity не сохранил модуль склада.");AssetDatabase.SaveAssetIfDirty(report);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            var connectedRoot=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=connectedRoot.GetComponent<RoomAuthoring>();var actual=RoomContentBatch.Read(room,"factory:0");
                Require(actual.doors.doors.Length==19 && StateJson.ToJson(room.CreateLayout())==geometry && Other(actual)==other &&
                    retained.All(d=>actual.doors.doors.Any(a=>a.id==d.id && StateJson.ToJson(a)==d.json)),"Nested дополнение изменило geometry/art/прочий контент либо прежнюю дверь.");
                var content=connectedRoot.GetComponent<RoomDoorContent>();content.Configure(ConnectedPatch);content.ConfigureExpansion(expansion);
                FactoryWarehouseConnectionChecks.Check(room);VerifyRoom(room);
                Require(PrefabUtility.SaveAsPrefabAsset(connectedRoot,ConnectedFactoryAuthoring.Path)!=null,"Unity не сохранил связный завод.");
            }
            finally {PrefabUtility.UnloadPrefabContents(connectedRoot);}
            Verify();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Склад40: добавлены ровно две металлические двери64/71;49/57 записей, локально4/root19 дверей. Сохранённые миры обновляются cumulative v4→v5 без refill/reset/resurrection. Замки/структурное сопротивление/original runtime остаются явным долгом.");
        }
        public static void Verify()
        {
            FactoryWarehouseMetalDoorAuthoring.Verify();
            var warehouse=FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>();
            FactoryWarehouseChecks.Check(warehouse);FactoryWarehouseNpcChecks.Check(warehouse);CheckLocal(warehouse);
            var connected=FactoryWarehouseSource.Asset<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>();
            FactoryWarehouseConnectionChecks.Check(connected);VerifyRoom(connected);
        }
        public static void VerifyRoom(RoomAuthoring room)
        {
            var doors=RoomDoorContent.Read(room,"factory:0");Require(doors.patch=="factory:0:"+ConnectedPatch,"Нет root doors-v5.");
            BeforeMetalDoors(doors);
        }
        public static void CheckLocal(RoomAuthoring room)
        {
            var doors=RoomDoorContent.Read(room,"factory:0");
            Require(doors.patch=="factory:0:"+LocalPatch && doors.doors.Length==4 && doors.expansion?.previousPatch=="factory:0:"+FactoryWarehouseDraft.DoorPatch &&
                Same(doors.expansion.additions,NewIds(doors.level)) && doors.expansion.earlierVersions?.Length==0,"Нет точных четырёх дверей/local cumulative v1→v2.");
            CheckNewDoors(doors,0);ValidateReport(room,true,FactoryWarehouseSource.Load());
            var terrain=room.CreateLayout(false);
            foreach(var id in NewIds(doors.level))
            {
                var door=doors.doors.Single(d=>d.id==id);
                Require(!terrain.platforms.Any(s=>!s.oneWay && Mathf.Min(s.x+s.width,door.Bounds.xMax)-Mathf.Max(s.x,door.x)>.0001f &&
                    Mathf.Min(s.y+s.height,door.Bounds.yMax)-Mathf.Max(s.y,door.y)>.0001f),"Новый барьер пересекает retained source wall: "+id);
                var marker=room.GetComponentsInChildren<RoomDoorAuthoring>(true).Single(m=>room.Included(m) && m.Read(room,doors.level).id==id.Substring(doors.level.Length+1));
                var view=marker.GetComponent<DoorView>();
                Require(view!=null && view.Definition==FactoryWarehouseMetalDoorAuthoring.Verify() && view.Artwork.sprite==view.Definition.Closed &&
                    view.transform.localRotation==Quaternion.identity && view.transform.localScale==Vector3.one &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject)==FactoryWarehouseMetalDoorAuthoring.PrefabPath,
                    "Новая source дверь использует чужой nested prefab/Transform/рисунок: "+id);
            }
        }
        // Detached historical test fixture only. Never install this layout into a live world.
        public static DoorLayout BeforeMetalDoors(DoorLayout source,string projectRoot=null)
        {
            if(source==null) return null;
            Require(source.doors!=null && !string.IsNullOrWhiteSpace(source.level),"Неполная дверная проекция.");
            var added=NewIds(source.level);
            if(source.patch!=source.level+":"+ConnectedPatch)
            {
                Require(!source.doors.Any(d=>d!=null && added.Contains(d.id)),"Новая дверь скрыта в прежней/неизвестной версии: "+source.patch);
                return StateJson.Copy(source);
            }
            Require(source.doors.Length==19 && source.doors.All(d=>d!=null && d.level==source.level) && source.doors.Select(d=>d.id).Distinct(StringComparer.Ordinal).Count()==19 &&
                source.expansion?.previousPatch==source.level+":"+FactoryWarehouseConnectionAuthoring.DoorPatch && Same(source.expansion.additions,added) &&
                source.expansion.earlierVersions?.Length==3,"Неполный root19/cumulative doors-v5.");
            CheckNewDoors(source,115.2f);
            var result=StateJson.Copy(source);result.doors=result.doors.Where(d=>!added.Contains(d.id)).ToArray();result.patch=result.level+":"+FactoryWarehouseConnectionAuthoring.DoorPatch;
            var steps=source.expansion.earlierVersions;
            for(int i=0;i<steps.Length;i++)
                Require(steps[i]?.previousPatch==source.level+":base:factory-connected-46-18:doors-v"+(3-i) && steps[i].additions!=null &&
                    steps[i].additions.Distinct(StringComparer.Ordinal).Count()==steps[i].additions.Length && added.All(id=>steps[i].additions.Contains(id)),"Новая cumulative chain потеряла ID/версию: index="+i);
            result.expansion=new ContentExpansion {previousPatch=steps[0].previousPatch,additions=steps[0].additions.Where(id=>!added.Contains(id)).ToArray(),
                earlierVersions=steps.Skip(1).Select(s=>new ContentExpansionStep {previousPatch=s.previousPatch,additions=s.additions.Where(id=>!added.Contains(id)).ToArray()}).ToArray()};
            FactoryWarehouseHistoryProjection.BeforeWarehouse(result,projectRoot);return result;
        }
        internal static void CheckNewDoors(DoorLayout layout,float offset)
        {
            foreach(var record in Records)
            {
                var door=layout.doors.SingleOrDefault(d=>d?.id==layout.level+":"+record.Id);
                Require(door!=null,"Нет exact новой двери: "+record.Id);door.Validate();Vector2 anchor=record.Anchor+Vector2.right*offset;
                Require(door.definition==FactoryWarehouseMetalDoorAuthoring.DefinitionId && door.surfaceTexture==FactoryWarehouseMetalDoorAuthoring.SurfaceTexture && door.interactive &&
                    door.integrity?.maxHealth==1000 && door.interaction!=null && door.InteractionBounds==door.Bounds &&
                    Mathf.Abs(door.x-(anchor.x-.4f))<=.00001f && Mathf.Abs(door.y-anchor.y)<=.00001f && door.width==.8f && door.height==1.6f,
                    "Новая дверь не совпадает с exact source/profile registration: "+door.id+", actual="+StateJson.ToJson(door));
            }
        }
        internal static string[] NewIds(string level)=>Records.Select(r=>level+":"+r.Id).ToArray();
        // Native prefab/report saves are separate. Recover only their two proven interruption states,
        // validating a detached report against the actual saved module before changing the durable journal.
        private static void RepairInterruptedReport(FactoryWarehouseSource.Data source)
        {
            var prefab=PrefabUtility.LoadPrefabContents(FactoryWarehouseDraft.Path);
            try
            {
                var room=prefab.GetComponent<RoomAuthoring>();var audit=prefab.GetComponent<RoomImportAudit>();var report=audit.Report;
                ValidateReportIdentity(report);
                var layout=RoomDoorContent.Read(room,"factory:0");bool installed=layout.patch=="factory:0:"+LocalPatch;
                Require(installed || layout.patch=="factory:0:"+FactoryWarehouseDraft.DoorPatch,"Нельзя восстановить report для неизвестной module версии.");
                Require(layout.doors.Length==(installed?4:2),"Нельзя восстановить report для частичной placement группы.");
                if(!installed) Require(!layout.doors.Any(d=>NewIds(layout.level).Contains(d.id)),"Новые placements скрыты в прежней module версии.");
                var flags=Records.Select(r=>report.Records.Single(p=>p.recordId==r.RecordId).installed).ToArray();
                Require(flags.Distinct().Count()==1,"Смешанные64/71 import flags не соответствуют целому interrupted save; данные не исправлены.");
                if(flags[0]==installed) return;
                var repaired=RepairDecisions(report.Records,installed);var detached=UnityEngine.Object.Instantiate(report);
                try
                {
                    detached.Configure(FactoryWarehouseSource.SourceId,FactoryWarehouseSource.SourceHash,repaired);audit.Configure(detached);
                    FactoryWarehouseChecks.Check(room);FactoryWarehouseNpcChecks.Check(room);ValidateReport(room,installed,source);
                }
                finally {audit.Configure(report);UnityEngine.Object.DestroyImmediate(detached);}
                // The original source/hash/GUID and the other55 entries remain independent inputs.
                var original=StateJson.Copy(report.Records);
                try
                {
                    report.Configure(FactoryWarehouseSource.SourceId,FactoryWarehouseSource.SourceHash,repaired);EditorUtility.SetDirty(report);AssetDatabase.SaveAssetIfDirty(report);
                }
                catch
                {report.Configure(FactoryWarehouseSource.SourceId,FactoryWarehouseSource.SourceHash,original);EditorUtility.SetDirty(report);throw;}
                Debug.Log($"Склад40: восстановлен только журнал64/71 после прерванной native установки; actual module={layout.patch}, flags={flags[0]}→{installed}. Source XML/GUID/geometry/остальные55records не заменены.");
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
        }
        internal static RoomSourceRecord[] RepairDecisions(RoomSourceRecord[] source,bool installed)
        {
            Require(source?.Length==57 && source.All(r=>r!=null) && source.Select(r=>r.recordId).Distinct(StringComparer.Ordinal).Count()==57,"Нет полного independent report для interrupted save.");
            Require(Records.All(record=>source.Count(r=>r.recordId==record.RecordId)==1),"Нет exact двух source записей для interrupted save.");
            Require(Records.Select(record=>source.Single(r=>r.recordId==record.RecordId).installed).Distinct().Count()==1,
                "Смешанные64/71 import flags не соответствуют целому interrupted save; данные не исправлены.");
            var result=StateJson.Copy(source);
            foreach(var record in Records)
            {
                var actual=result.Single(r=>r.recordId==record.RecordId);
                Require(actual.kind=="door2" && actual.code==record.Code && actual.sourceX==record.X && actual.sourceY==record.Y,"Нельзя восстановить чужую source запись: "+record.RecordId);
                actual.installed=installed;actual.reason=DecisionReason(record,installed);
            }
            return result;
        }
        private static string DecisionReason(Placement record,bool installed)=>!installed?
            "Металлическая door2 пока не установлена: восстановлено исходное pending решение после interrupted native save. Source lock/structural resistance не интерпретируются.":
            "Исходные door2/code/cell/нижний центр сохранены; новый warehouse-metal-door, HP1000, три native кадра и исходные metal sounds. Закреплена, начально закрыта. "+
            (record.Record==71?"lock0 — явное исходное отсутствие замка. ":"Незапертое начало — фиксированная политика Unity; исходная вероятностная lock metadata не воспроизводится. ")+
            "Структурное сопротивление thre100, взлом, два исходных пути разрушения и original runtime не подтверждены; используется текущий явный Unity DoorImpactDamage.";
        private static void ValidateRecords(FactoryWarehouseSource.Data data)
        {
            foreach(var record in Records)
            {
                var raw=data.source.room.records.Single(r=>r.recordId==record.RecordId);
                Require(raw.tag=="obj" && FactoryWarehouseSource.Value(raw,"id")=="door2" && FactoryWarehouseSource.Value(raw,"code")==record.Code &&
                    FactoryWarehouseSource.Number(raw,"x")==record.X && FactoryWarehouseSource.Number(raw,"y")==record.Y &&
                    Vector2.Distance(FactoryWarehouseSource.Anchor(data,raw),record.Anchor)<.00001f &&
                    (record.Record==71?raw.attributes.TryGetValue("lock",out string value)&&value=="0":!raw.attributes.ContainsKey("lock")),"Изменена исходная door2 запись: "+record.RecordId);
            }
        }
        private static void ValidateReport(RoomAuthoring room,bool installed,FactoryWarehouseSource.Data source)
        {
            ValidateRecords(source);var report=room.GetComponent<RoomImportAudit>().Report;report.Validate();
            ValidateReportIdentity(report);
            foreach(var r in Records)
            {
                var record=report.Records.Single(p=>p.recordId==r.RecordId);var raw=source.source.room.records.Single(p=>p.recordId==r.RecordId);
                Require(record.kind=="door2" && record.code==r.Code && record.sourceX==r.X && record.sourceY==r.Y && record.sourceData==raw.contentXml &&
                    record.installed==installed && !string.IsNullOrWhiteSpace(record.reason),"Изменены XML/ID/import decision: "+r.RecordId);
            }
        }
        private static void ValidateReportIdentity(RoomImportReportAsset report)=>Require(report.SourceId==FactoryWarehouseSource.SourceId && report.Records.Length==57 &&
            new SerializedObject(report).FindProperty("sourceHash").stringValue==FactoryWarehouseSource.SourceHash,"Изменён полный source audit склада.");
        private static string Other(RoomContentBatch batch)=>StateJson.ToJson(batch.physical)+"\n"+StateJson.ToJson(batch.npcs)+"\n"+StateJson.ToJson(batch.mechanisms)+"\n"+StateJson.ToJson(batch.travelPoints);
        private static bool Same(string[] a,string[] b)=>a!=null && b!=null && a.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(b.OrderBy(id=>id,StringComparer.Ordinal));
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Расстановка металлических дверей склада: "+reason);}
    }
}
