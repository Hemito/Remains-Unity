using System;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit source records and independent Unity content; no source placement/loot generator.
    public static class FactoryOfficeFurnitureAuthoring
    {
        public const string OfficePatch="base:factory-office-53:physical-v3";
        public const string ConnectedPatch="base:factory-connected-46-18:physical-v6";
        internal const string SourcePrefix="original:plant:53:";
        internal sealed class FurnitureRecord
        {
            public readonly int record,x,y,cells;
            public readonly string code,type;
            public readonly bool container;
            public FurnitureRecord(int record,string code,string type,int x,int y,int cells,bool container)
            {this.record=record;this.code=code;this.type=type;this.x=x;this.y=y;this.cells=cells;this.container=container;}
            public string Id=>SourcePrefix+code;
            public string RecordId=>SourcePrefix+"record:"+record;
            public Vector2 Position=>FactorySourceCoordinates.Placement(x,y,cells);
            public string AssetPath=>FactoryOfficeFurnitureArtworkAuthoring.AssetPath(type);
            public string LootPath=>"Assets/Remains/RoomDrafts/Containers/office53-"+type+"-loot.asset";
        }
        internal static readonly FurnitureRecord[] Records={
            new(42,"no27T8YFjTx8ZVz4","table",24,15,2,true),
            new(43,"Qyc8bJzHANJlny9s","table",42,15,2,true),
            new(45,"HaoIW4PYZAYl0YKb","trash",19,10,1,true),
            new(53,"D08uoqM6f1EaCJfJ","table1",36,15,2,false),
            new(54,"TGISVy91GeyHglBk","table1",37,10,2,false),
            new(55,"mA7CG0NX9srOOsJv","table1",24,10,2,false),
            new(58,"B37xGIBuhSjLOfSc","table",29,10,2,true),
            new(59,"HLHr8wbz490ZTI9b","bookcase",45,10,2,true)};

        [MenuItem("Remains/Контент/Установить мебель офисов53")]
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();PropCatalog.Load();
            var officeAsset=Require<GameObject>(FactoryOfficeDraft.Path);
            var connectedAsset=Require<GameObject>(ConnectedFactoryAuthoring.Path);
            var prior=RoomContentBatch.Read(connectedAsset.GetComponent<RoomAuthoring>(),"factory:0");
            var local=RoomPhysicalContent.Read(officeAsset.GetComponent<RoomAuthoring>(),"factory:0");
            if(prior.physical.patch=="factory:0:"+ConnectedPatch)
            {
                FactoryOfficeFurnitureChecks.Run();
                Debug.Log("Мебель офисов53 уже сохранена; проверены восемь ID без замены авторских данных.");return;
            }
            bool resumed=local.patch=="factory:0:"+OfficePatch;
            if(!resumed && local.patch!="factory:0:"+FactoryOfficeLootAuthoring.OfficePatch)
                throw new InvalidOperationException("Мебель офисов53: неизвестная прежняя физическая версия: "+local.patch);
            ValidateSource(officeAsset.GetComponent<RoomImportAudit>().Report,resumed);
            if(resumed) FactoryOfficeFurnitureChecks.CheckOffice(officeAsset.GetComponent<RoomAuthoring>());
            else FactoryOfficeLootChecks.CheckOfficeStage(officeAsset.GetComponent<RoomAuthoring>());
            // After an interrupted office save, nested instances already contain the new records.
            // Only this explicit, verified resume stage may separate them from the old root manifest.
            var predecessor=resumed?RemoveNewRecords(prior.physical):prior.physical;
            var expansion=CreateExpansion(predecessor,true);
            string geometry=StateJson.ToJson(connectedAsset.GetComponent<RoomAuthoring>().CreateLayout());
            string other=OtherContent(prior);
            var newIds=Records.Select(r=>"factory:0:"+r.Id).ToArray();
            var retainedProps=prior.physical.props.Where(p=>!newIds.Contains(p.id)).Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
            var retainedContainers=prior.physical.containers.Where(p=>!newIds.Contains(p.id)).Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
            var retainedLoot=prior.physical.loot.Where(p=>!newIds.Contains(p.container)).Select(p=>(p.container,json:StateJson.ToJson(p))).ToArray();
            var definitions=FactoryOfficeFurnitureArtworkAuthoring.Install();
            if(definitions.Length!=4 || !definitions.Select(d=>d.CreateDefinition().id).OrderBy(id=>id).SequenceEqual(new[]{"bookcase","table","table1","trash"}))
                throw new InvalidOperationException("Мебель офисов53: ресурсный контракт не вернул четыре явных типа.");
            var catalog=EntityAssetCatalog.Load();string actors=catalog.ActorsJson();
            var oldProps=catalog.CreateProps().Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
            foreach(var definition in definitions) catalog.AddProp(definition);
            if(catalog.ActorsJson()!=actors || oldProps.Any(p=>catalog.CreateProps().All(q=>q.id!=p.id || StateJson.ToJson(q)!=p.json)))
                throw new InvalidOperationException("Мебель офисов53: регистрация изменила прежние определения каталога.");
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();PropCatalog.Load();
            var icons=new Remains.Presentation.InventoryArtwork();
            foreach(var entry in Records.Where(r=>r.container))
            {
                foreach(var item in CreateLoot(entry.type).entries) icons.Require(item.definition);
                LootAsset(entry);
            }
            if(!resumed)
            {
                var office=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
                try
                {
                    var room=office.GetComponent<RoomAuthoring>();var report=office.GetComponent<RoomImportAudit>().Report;
                    var records=StateJson.Copy(report.Records);ValidateSource(report,false);
                    var before=RoomPhysicalContent.Read(room,"factory:0");
                    var officeExpansion=CreateExpansion(before,false);
                    foreach(var entry in Records)
                    {
                        if(office.GetComponentsInChildren<RoomPhysicalPlacement>(true).Any(p=>p.Read(room).id==entry.Id))
                            throw new InvalidOperationException("Мебель офисов53: частичная расстановка не заменена: "+entry.Id);
                        var asset=Require<PropAsset>(entry.AssetPath);var data=asset.CreateDefinition();
                        var marker=new GameObject(data.name+" · "+entry.code);marker.transform.SetParent(office.transform,false);
                        marker.transform.localPosition=entry.Position;
                        RoomPhysicalPlacement placement;
                        if(entry.container)
                        {
                            var container=marker.AddComponent<RoomContainerPlacement>();container.SetLoot(LootAsset(entry));placement=container;
                        }
                        else placement=marker.AddComponent<RoomPropPlacement>();
                        placement.Configure(entry.Id,asset);
                        var preview=(GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab.gameObject,marker.transform);
                        preview.transform.localPosition=data.hasPhysicalBody?Vector3.up*(data.height*.5f):Vector3.zero;
                        var record=records.Single(r=>r.recordId==entry.RecordId);record.installed=true;
                        record.reason="Исходные type/code/anchor сохранены. "+(entry.container?"Общий контейнер с явной самостоятельной таблицей Unity. ":"Переносимый физический стол без инвентаря. ")+
                            (data.hasPhysicalBody?"Тело, масса и графическая регистрация из отдельного SO. ":"Закреплённый настенный книжный шкаф без физического тела/массы/опоры. ")+
                            "Замки/навыки/lurk и исходный условный генератор содержимого не реализованы. См. FACTORY_OFFICE_FURNITURE_SPEC.md.";
                    }
                    var content=office.GetComponent<RoomPhysicalContent>();content.Configure(OfficePatch);
                    content.ConfigureExpansion(officeExpansion.previousPatch,officeExpansion.additions,officeExpansion.earlierVersions);
                    string hash=new SerializedObject(report).FindProperty("sourceHash").stringValue;
                    report.Configure(report.SourceId,hash,records);EditorUtility.SetDirty(report);
                    FactoryOfficeFurnitureChecks.CheckOffice(room);
                    if(PrefabUtility.SaveAsPrefabAsset(office,FactoryOfficeDraft.Path)==null)
                        throw new InvalidOperationException("Мебель офисов53: Unity не сохранил офисный модуль.");
                    AssetDatabase.SaveAssets();
                }
                finally {PrefabUtility.UnloadPrefabContents(office);}
            }
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=connected.GetComponent<RoomAuthoring>();var actual=RoomContentBatch.Read(room,"factory:0");
                if(actual.physical.props.Length!=12 || actual.physical.containers.Length!=36 || actual.physical.loot.Length!=33 ||
                    retainedProps.Any(p=>actual.physical.props.All(q=>q.id!=p.id || StateJson.ToJson(q)!=p.json)) ||
                    retainedContainers.Any(p=>actual.physical.containers.All(q=>q.id!=p.id || StateJson.ToJson(q)!=p.json)) ||
                    retainedLoot.Any(p=>actual.physical.loot.All(q=>q.container!=p.container || StateJson.ToJson(q)!=p.json)) ||
                    StateJson.ToJson(room.CreateLayout())!=geometry || OtherContent(actual)!=other)
                    throw new InvalidOperationException("Мебель офисов53: дополнение изменило прежние объекты/лут/геометрию/NPC/двери/механизмы либо nested prefab не обновился.");
                var content=connected.GetComponent<RoomPhysicalContent>();content.Configure(ConnectedPatch);
                content.ConfigureExpansion(expansion.previousPatch,expansion.additions,expansion.earlierVersions);
                FactoryOfficeFurnitureChecks.Check(room);
                ConnectedFactoryChecks.Check(room);FactoryOfficeConnectionChecks.Check(room);FactoryOfficeNpcChecks.Check(room);
                if(PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path)==null)
                    throw new InvalidOperationException("Мебель офисов53: Unity не сохранил связный завод.");
                AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            FactoryOfficeFurnitureChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Мебель офисов53: сохранены 8 исходных записей; офис 6 props/21 контейнер, связный завод 12 props/36 контейнеров. 9 записей офиса остаются неподключёнными. Полная исходная механика/loot и native маршрут этим сообщением не подтверждаются.");
        }
        internal static void ValidateSource(RoomImportReportAsset report,bool installed)
        {
            if(report.SourceId!="original:plant:53" || report.Records.Length!=46 ||
                new SerializedObject(report).FindProperty("sourceHash").stringValue!="e008b7795261a45fe627eaa921ad764ba0492365fc5e91b93b097251a57767d8")
                throw new InvalidOperationException("Мебель офисов53: изменился исходный журнал.");
            foreach(var entry in Records)
            {
                var r=report.Records.SingleOrDefault(r=>r.recordId==entry.RecordId);
                if(r==null || r.code!=entry.code || r.kind!=entry.type || r.sourceX!=entry.x || r.sourceY!=entry.y || r.installed!=installed)
                    throw new InvalidOperationException("Мебель офисов53: изменены исходная запись или решение: "+entry.RecordId);
                var xml=XElement.Parse(r.sourceData);
                if(xml.Name!="obj" || xml.Attributes().Count()!=4 || (string)xml.Attribute("id")!=entry.type || (string)xml.Attribute("code")!=entry.code ||
                    (int?)xml.Attribute("x")!=entry.x || (int?)xml.Attribute("y")!=entry.y)
                    throw new InvalidOperationException("Мебель офисов53: изменены source XML/overrides: "+entry.RecordId);
            }
        }
        // Detached current-stage fixture for historical regressions. It never changes a saved room/world.
        internal static PhysicalLayout RemoveNewRecords(PhysicalLayout source)
        {
            var result=StateJson.Copy(source);var ids=Records.Select(r=>source.level+":"+r.Id).ToArray();
            result.props=result.props.Where(p=>!ids.Contains(p.id)).ToArray();
            result.containers=result.containers.Where(p=>!ids.Contains(p.id)).ToArray();
            result.loot=result.loot.Where(b=>!ids.Contains(b.container)).ToArray();return result;
        }
        internal static ContentExpansion CreateExpansion(PhysicalLayout prior,bool connected)
        {
            string prefix=prior.level+":";
            string expected=connected?FactoryOfficeLootAuthoring.ConnectedPatch:FactoryOfficeLootAuthoring.OfficePatch;
            if(prior.patch!=prefix+expected || prior.props.Length!=(connected?9:3) || prior.containers.Length!=(connected?31:16) || prior.loot.Length!=(connected?28:15))
                throw new InvalidOperationException("Мебель офисов53: неизвестный или неполный predecessor: "+prior.patch);
            if(connected) FactoryContentCheckFixtures.BeforeOffice(prior);
            else
            {
                var oldIds=FactoryOfficeLootAuthoring.Containers.Select(c=>prefix+c.Id).ToArray();
                if(prior.expansion?.previousPatch!=prefix+FactoryOfficeDraft.PhysicalPatch || prior.expansion.earlierVersions?.Length!=0 ||
                    !Same(prior.expansion.additions,oldIds) || !Same(prior.containers.Where(c=>oldIds.Contains(c.id)).Select(c=>c.id),oldIds))
                    throw new InvalidOperationException("Мебель офисов53: потеряна прежняя офисная цепочка v1→v2.");
            }
            string Local(string id)=>id.StartsWith(prefix,StringComparison.Ordinal)?id.Substring(prefix.Length):throw new InvalidOperationException("Чужой уровень дополнения: "+id);
            var additions=Records.Select(r=>r.Id).ToArray();
            var earlier=new[]{(ContentExpansionStep)prior.expansion}.Concat(prior.expansion.earlierVersions).ToArray();
            return new ContentExpansion {previousPatch=Local(prior.patch),additions=additions,
                earlierVersions=earlier.Select(s=>new ContentExpansionStep {previousPatch=Local(s.previousPatch),additions=s.additions.Select(Local).Concat(additions).ToArray()}).ToArray()};
        }
        internal static ContainerLootTable CreateLoot(string type)
        {
            ContainerLootEntry Entry(string id,int count)=>new() {id=id,definition=id,minimum=count,maximum=count};
            var entries=type switch {
                "table"=>new[]{Entry("caps",3),Entry("pin",1)},
                "trash"=>new[]{Entry("metal_comp",1)},
                // Independently fixed quantity; simple source valuables, no skill/use behavior.
                "bookcase"=>new[]{Entry("lbook",1)},
                _=>throw new InvalidOperationException("Мебель офисов53: нет явно выбранной таблицы контейнера: "+type)};
            return new ContainerLootTable {id="base:office53-"+type,version="1",entries=entries};
        }
        private static ContainerLootAsset LootAsset(FurnitureRecord entry)
        {
            var expected=CreateLoot(entry.type);var existing=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(entry.LootPath);
            if(existing!=null)
            {
                if(StateJson.ToJson(existing.Read())!=StateJson.ToJson(expected))
                    throw new InvalidOperationException("Мебель офисов53: авторская таблица отличается, автоматическая замена запрещена: "+entry.LootPath);
                return existing;
            }
            if(AssetDatabase.LoadMainAssetAtPath(entry.LootPath)!=null) throw new InvalidOperationException("Путь таблицы мебели занят: "+entry.LootPath);
            var asset=ScriptableObject.CreateInstance<ContainerLootAsset>();asset.Configure(expected);AssetDatabase.CreateAsset(asset,entry.LootPath);return asset;
        }
        private static string OtherContent(RoomContentBatch b)=>StateJson.ToJson(new {b.npcs,b.doors,b.mechanisms,b.travelPoints});
        private static bool Same(System.Collections.Generic.IEnumerable<string> a,System.Collections.Generic.IEnumerable<string> b)=>a.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(b.OrderBy(id=>id,StringComparer.Ordinal));
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Мебель офисов53: отсутствует сохранённый ассет: "+path);
    }
}
