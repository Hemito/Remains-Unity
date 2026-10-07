using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Reviewed source records; all runtime installation remains in PhysicalContentInstaller.
    public static class FactoryWarehouseFurnitureAuthoring
    {
        public const string LocalPatch="base:factory-warehouse-40:physical-v2";
        public const string ConnectedPatch="base:factory-connected-46-18:physical-v8";
        public const string SourcePrefix="original:plant:40:";
        public readonly struct Placement
        {
            public readonly int Record,X,Y,Cells;
            public readonly string Code,Type;
            public readonly bool Container;
            public Placement(int record,string code,string type,int x,int y,bool container)
            {Record=record;Code=code;Type=type;X=x;Y=y;Cells=2;Container=container;}
            public string Id=>SourcePrefix+Code;
            public string RecordId=>SourcePrefix+"record:"+Record;
            public Vector2 Anchor=>FactorySourceCoordinates.Placement(X,Y,Cells);
        }
        private static readonly Placement[] records={
            new(58,"detVw68Tuzey1MqU","safe",44,11,true),
            new(73,"Z9yOwJPvLO0MySmN","couch",39,23,false),
            new(74,"EHzteZ0u3qpHCu8Q","locker",39,7,true),
            new(75,"WLoJrSYbx2hxtRHc","locker",6,15,true)};
        private static readonly System.Collections.Generic.IReadOnlyList<Placement> recordView=Array.AsReadOnly(records);
        public static System.Collections.Generic.IReadOnlyList<Placement> Records=>recordView;
        public static string[] NewIds(string level)=>records.Select(r=>level+":"+r.Id).ToArray();
        public static bool IsInstalled(RoomAuthoring room)=>RoomPhysicalContent.Read(room,"factory:0")?.patch=="factory:0:"+LocalPatch;
        public static string LootPath(string type)=>"Assets/Remains/RoomDrafts/Containers/warehouse40-"+type+"-loot.asset";
        public static ContainerLootTable CreateLoot(string type)
        {
            ContainerLootEntry Entry(string id,int count)=>new() {id=id,definition=id,minimum=count,maximum=count};
            return new ContainerLootTable {id="base:warehouse40-"+type,version="1",entries=type switch {
                "safe"=>new[]{Entry("caps",12),Entry("heal",1),Entry("p10",12)},
                "locker"=>new[]{Entry("caps",3),Entry("metal_comp",1)},
                _=>throw new InvalidOperationException("Мебель склада40: у типа нет утверждённой таблицы контейнера: "+type)}};
        }

        [MenuItem("Remains/Контент/Установить мебель склада40")]
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();PropCatalog.Load();
            var data=FactoryWarehouseSource.Load();ValidateRecords(data);
            var localRoom=FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>();
            var connectedRoom=FactoryWarehouseSource.Asset<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>();
            var local=RoomContentBatch.Read(localRoom,"factory:0");var connected=RoomContentBatch.Read(connectedRoom,"factory:0");
            if(connected.physical.patch=="factory:0:"+ConnectedPatch)
            {Verify();Debug.Log("Мебель склада40 уже установлена: четыре ID проверены без замены авторских данных.");return;}
            bool resumed=local.physical.patch=="factory:0:"+LocalPatch;
            Require(connected.physical.patch=="factory:0:"+FactoryWarehouseConnectionAuthoring.PhysicalPatch &&
                (resumed || local.physical.patch=="factory:0:"+FactoryWarehouseDraft.PhysicalPatch),"Неизвестная физическая стадия; ручные данные не заменены.");
            Require(local.doors.doors.Length==4 && connected.doors.doors.Length==19 && local.npcs.actors.Length==9 && connected.npcs.actors.Length==22,
                "Нужна полная прежняя стадия склада с девятью NPC и двумя установленными metal doors.");
            ValidateReport(localRoom,resumed,data);FactoryWarehouseFurnitureChecks.CheckPredecessor(resumed?BeforeFurniture(local.physical):local.physical,false);
            // A nested module can finish saving before the root manifest. Only the complete
            // exact four-record stage can be resumed; no partial marker/report is repaired.
            var predecessor=resumed?RemoveExactAddition(connected.physical,true):StateJson.Copy(connected.physical);
            FactoryWarehouseHistoryProjection.BeforeWarehouse(predecessor);
            var expansion=FactoryExpansionAuthoring.Extend(predecessor.patch,predecessor.expansion,NewIds(predecessor.level));
            string before=Snapshot(connectedRoom.CreateLayout(),connected,resumed?predecessor:connected.physical);
            var definitions=FactoryWarehouseFurnitureArtworkAuthoring.Install();
            Require(definitions?.Length==3 && Same(definitions.Select(a=>a.CreateDefinition().id),new[]{"couch","safe","locker"}),"Ресурсный авторинг не вернул три явных типа.");
            var catalog=EntityAssetCatalog.Load();string oldActors=catalog.ActorsJson();
            var oldProps=catalog.CreateProps().Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
            foreach(var definition in definitions) catalog.AddProp(definition);
            Require(catalog.ActorsJson()==oldActors && oldProps.All(p=>catalog.CreateProps().Any(q=>q.id==p.id && StateJson.ToJson(q)==p.json)),"Регистрация изменила прежние определения.");
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();PropCatalog.Load();
            var icons=new InventoryArtwork();
            foreach(string type in new[]{"safe","locker"})
            {foreach(var entry in CreateLoot(type).entries) icons.Require(entry.definition);EnsureLoot(type);}
            if(!resumed)
            {
                var root=PrefabUtility.LoadPrefabContents(FactoryWarehouseDraft.Path);
                try
                {
                    var room=root.GetComponent<RoomAuthoring>();var prior=RoomContentBatch.Read(room,"factory:0");
                    ValidateReport(room,false,data);FactoryWarehouseFurnitureChecks.CheckPredecessor(prior.physical,false);
                    string retained=Snapshot(room.CreateLayout(),prior,prior.physical);
                    var step=FactoryExpansionAuthoring.Extend(prior.physical.patch,prior.physical.expansion,NewIds(prior.physical.level));
                    foreach(var record in records)
                    {
                        Require(!room.GetComponentsInChildren<RoomPhysicalPlacement>(true).Any(p=>p.Read(room).id==record.Id),"Частичная расстановка не заменена: "+record.Id);
                        var asset=FactoryWarehouseSource.Asset<PropAsset>(FactoryWarehouseFurnitureArtworkAuthoring.AssetPath(record.Type));
                        var marker=new GameObject(asset.CreateDefinition().name+" · "+record.Code);marker.transform.SetParent(root.transform,false);marker.transform.localPosition=record.Anchor;
                        RoomPhysicalPlacement placement;
                        if(record.Container) {var container=marker.AddComponent<RoomContainerPlacement>();container.SetLoot(EnsureLoot(record.Type));placement=container;}
                        else placement=marker.AddComponent<RoomPropPlacement>();
                        placement.Configure(record.Id,asset);
                        var preview=(GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab.gameObject,marker.transform);
                        preview.transform.localPosition=asset.Prefab.VisualAnchor==PropVisualAnchor.BodyCenter?Vector3.up*(asset.CreateDefinition().height*.5f):Vector3.zero;
                    }
                    var content=root.GetComponent<RoomPhysicalContent>();content.Configure(LocalPatch);content.ConfigureExpansion(step.previousPatch,step.additions,step.earlierVersions);
                    var report=root.GetComponent<RoomImportAudit>().Report;var reportRecords=StateJson.Copy(report.Records);
                    foreach(var record in records)
                    {var entry=reportRecords.Single(r=>r.recordId==record.RecordId);entry.installed=true;entry.reason=Decision(record);}
                    report.Configure(FactoryWarehouseSource.SourceId,FactoryWarehouseSource.SourceHash,reportRecords);EditorUtility.SetDirty(report);
                    var actual=RoomContentBatch.Read(room,"factory:0");
                    Require(Snapshot(room.CreateLayout(),actual,BeforeFurniture(actual.physical))==retained,"Дополнение изменило прежнюю геометрию/арт/объекты/двери/NPC/прочий контент.");
                    FactoryWarehouseFurnitureChecks.CheckLocal(room);
                    Require(PrefabUtility.SaveAsPrefabAsset(root,FactoryWarehouseDraft.Path)!=null,"Unity не сохранил модуль склада.");AssetDatabase.SaveAssetIfDirty(report);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            var whole=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=whole.GetComponent<RoomAuthoring>();var actual=RoomContentBatch.Read(room,"factory:0");
                var retained=RemoveExactAddition(actual.physical,true);
                Require(Snapshot(room.CreateLayout(),actual,retained)==before,"Nested дополнение изменило прежний связный уровень/потеряло наследование.");
                var content=whole.GetComponent<RoomPhysicalContent>();content.Configure(ConnectedPatch);content.ConfigureExpansion(expansion.previousPatch,expansion.additions,expansion.earlierVersions);
                FactoryWarehouseFurnitureChecks.CheckConnected(room);
                Require(PrefabUtility.SaveAsPrefabAsset(whole,ConnectedFactoryAuthoring.Path)!=null,"Unity не сохранил связный завод.");
            }
            finally {PrefabUtility.UnloadPrefabContents(whole);}
            Verify();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Мебель склада40: 53/57 source records;18 props,19 общих контейнеров,19 таблиц. enc1×2/msp/doctor остаются pending; замки/мины/разрушение мебели не реализованы.");
        }
        public static void Verify()
        {
            FactoryWarehouseFurnitureArtworkAuthoring.Check();
            FactoryWarehouseFurnitureChecks.CheckLocal(FactoryWarehouseSource.Asset<GameObject>(FactoryWarehouseDraft.Path).GetComponent<RoomAuthoring>());
            FactoryWarehouseFurnitureChecks.CheckConnected(FactoryWarehouseSource.Asset<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>());
        }
        private static ContainerLootAsset EnsureLoot(string type)
        {
            string path=LootPath(type);var expected=CreateLoot(type);var saved=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(path);
            if(saved!=null) {Require(StateJson.ToJson(saved.Read())==StateJson.ToJson(expected),"Авторская таблица отличается, автоматическая замена запрещена: "+path);return saved;}
            Require(AssetDatabase.LoadMainAssetAtPath(path)==null,"Путь таблицы занят: "+path);
            var asset=ScriptableObject.CreateInstance<ContainerLootAsset>();asset.Configure(expected);AssetDatabase.CreateAsset(asset,path);return asset;
        }
        internal static void ValidateRecords(FactoryWarehouseSource.Data data)
        {
            foreach(var record in records)
            {
                var raw=data.source.room.records.Single(r=>r.recordId==record.RecordId);var xml=XElement.Parse(raw.contentXml);
                Require(raw.tag=="obj" && xml.Attributes().Count()==4 && FactoryWarehouseSource.Value(raw,"id")==record.Type &&
                    FactoryWarehouseSource.Value(raw,"code")==record.Code && FactoryWarehouseSource.Number(raw,"x")==record.X && FactoryWarehouseSource.Number(raw,"y")==record.Y &&
                    Vector2.Distance(FactoryWarehouseSource.Anchor(data,raw),record.Anchor)<.00001f,"Изменены исходные type/code/cell/size/overrides: "+record.RecordId);
            }
        }
        internal static void ValidateReport(RoomAuthoring room,bool installed,FactoryWarehouseSource.Data data)
        {
            ValidateRecords(data);var report=room.GetComponent<RoomImportAudit>()?.Report;
            Require(report!=null && report.SourceId==FactoryWarehouseSource.SourceId && report.Records.Length==57 &&
                new SerializedObject(report).FindProperty("sourceHash").stringValue==FactoryWarehouseSource.SourceHash,"Нет полного исходного журнала57.");
            foreach(var record in records)
            {
                var entry=report.Records.Single(r=>r.recordId==record.RecordId);var raw=data.source.room.records.Single(r=>r.recordId==record.RecordId);
                Require(entry.code==record.Code && entry.kind==record.Type && entry.sourceX==record.X && entry.sourceY==record.Y && entry.sourceData==raw.contentXml &&
                    entry.installed==installed && !string.IsNullOrWhiteSpace(entry.reason),"Изменены исходная запись/решение: "+record.RecordId);
            }
            var remaining=new[]{33,34,70,72}.Select(r=>SourcePrefix+"record:"+r).ToArray();
            foreach(var entry in report.Records)
            {
                var raw=data.source.room.records.Single(r=>r.recordId==entry.recordId);bool added=records.Any(r=>r.RecordId==entry.recordId);
                Require(raw.tag=="obj" && entry.kind==FactoryWarehouseSource.Value(raw,"id") && entry.code==FactoryWarehouseSource.Value(raw,"code") &&
                    entry.sourceX==FactoryWarehouseSource.Number(raw,"x") && entry.sourceY==FactoryWarehouseSource.Number(raw,"y") && entry.sourceData==raw.contentXml &&
                    entry.installed==(added?installed:!remaining.Contains(entry.recordId)) && !string.IsNullOrWhiteSpace(entry.reason),"Изменён retained исходный journal/flags: "+entry.recordId);
            }
            Require(report.Records.Count(r=>r.installed)==(installed?53:49),"Нет полной стадии49/53 исходного журнала.");
        }
        private static string Decision(Placement record)=>"Сохранены source type/code/нижний anchor; явный SO "+record.Type+" и исходная графика. "+
            (record.Type=="couch"?"Физический диван без содержимого. ":record.Type=="locker"?"Закреплённый wall1 общий шкаф, NoBody/noTK/noSupport. ":"Физический общий сейф. ")+"Фиксированный initial loot Unity; замки/мины/навыки/исходное сопротивление/разрушение мебели остаются вне этапа. Original runtime не проверен.";

        // Detached exact authored-stage fixture, never a migration of a live save.
        public static RoomContentBatch BeforeFurniture(RoomContentBatch source,string projectRoot=null)
        {if(source==null)return null;var result=StateJson.Copy(source);result.physical=BeforeFurniture(source.physical,projectRoot);return result;}
        public static PhysicalLayout BeforeFurniture(PhysicalLayout source,string projectRoot=null)
        {
            if(source==null)return null;
            Require(source.props!=null && source.containers!=null && source.loot!=null && !string.IsNullOrWhiteSpace(source.level) && !string.IsNullOrWhiteSpace(source.patch),"Неполная историческая проекция.");
            bool local=source.patch==source.level+":"+LocalPatch,connected=source.patch==source.level+":"+ConnectedPatch;
            if(!local && !connected)
            {Require(!source.props.Cast<PropState>().Concat(source.containers).Any(p=>p!=null && NewIds(source.level).Contains(p.id)) &&
                !source.loot.Any(b=>b!=null && NewIds(source.level).Contains(b.container)),"Прежняя/неизвестная версия скрывает новое мебельное ID: "+source.patch);return StateJson.Copy(source);}
            FactoryWarehouseFurnitureChecks.CheckNewRecords(source,connected?115.2f:0);
            Require(source.props.Length==(connected?30:18) && source.containers.Length==(connected?55:19) && source.loot.Length==(connected?53:19),"Неполное дополнение v8/v2.");
            var added=NewIds(source.level);var expansion=source.expansion;
            Require(expansion?.previousPatch==source.level+":"+(connected?FactoryWarehouseConnectionAuthoring.PhysicalPatch:FactoryWarehouseDraft.PhysicalPatch) &&
                Same(expansion.additions,added) && expansion.earlierVersions?.Length==(connected?6:0),"Изменена новая cumulative chain.");
            var result=RemoveExactAddition(source,connected);result.patch=expansion.previousPatch;
            if(!connected)result.expansion=null;
            else
            {
                var counts=new[]{37,45,49,64,65,67};
                for(int i=0;i<expansion.earlierVersions.Length;i++)
                {
                    var step=expansion.earlierVersions[i];
                    Require(step!=null && step.previousPatch==source.level+":base:factory-connected-46-18:physical-v"+(6-i) && step.additions!=null &&
                        step.additions.Length==counts[i] && step.additions.Distinct(StringComparer.Ordinal).Count()==step.additions.Length && added.All(id=>step.additions.Contains(id)),
                        "Неполный/повторный cumulative predecessor: index="+i);
                }
                var steps=expansion.earlierVersions;
                result.expansion=new PhysicalExpansion {previousPatch=steps[0].previousPatch,additions=steps[0].additions.Where(id=>!added.Contains(id)).ToArray(),
                    earlierVersions=steps.Skip(1).Select(s=>new ContentExpansionStep {previousPatch=s.previousPatch,additions=s.additions.Where(id=>!added.Contains(id)).ToArray()}).ToArray()};
            }
            FactoryWarehouseFurnitureChecks.CheckPredecessor(result,connected,projectRoot);return result;
        }
        internal static PhysicalLayout RemoveExactAddition(PhysicalLayout source,bool connected)
        {
            FactoryWarehouseFurnitureChecks.CheckNewRecords(source,connected?115.2f:0);
            Require(source.props.Length==(connected?30:18) && source.containers.Length==(connected?55:19) && source.loot.Length==(connected?53:19),"Неполная nested стадия четырёх новых ID.");
            var result=StateJson.Copy(source);var added=NewIds(source.level);
            result.props=result.props.Where(p=>!added.Contains(p.id)).ToArray();result.containers=result.containers.Where(p=>!added.Contains(p.id)).ToArray();result.loot=result.loot.Where(b=>!added.Contains(b.container)).ToArray();return result;
        }
        private sealed class AuthoringSnapshot {public RoomLayout geometry;public RoomContentBatch content;}
        internal static string Snapshot(RoomLayout geometry,RoomContentBatch batch,PhysicalLayout physical)
        {var copy=StateJson.Copy(batch);copy.physical=StateJson.Copy(physical);return StateJson.ToJson(new AuthoringSnapshot {geometry=geometry,content=copy});}
        internal static bool Same(System.Collections.Generic.IEnumerable<string> a,System.Collections.Generic.IEnumerable<string> b)=>a!=null && b!=null && a.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(b.OrderBy(id=>id,StringComparer.Ordinal));
        internal static void Require(bool valid,string message) {if(!valid)throw new InvalidOperationException("Мебель склада40: "+message);}
    }
}
