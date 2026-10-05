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
    // Four explicit source records. Loot is independent Unity content, not the original generator.
    public static class FactoryOfficeLootAuthoring
    {
        public const string OfficePatch="base:factory-office-53:physical-v2";
        public const string ConnectedPatch="base:factory-connected-46-18:physical-v5";
        internal const string SourcePrefix="original:plant:53:";
        internal sealed class ContainerRecord
        {
            public readonly int record,x,y,cells;
            public readonly string code,type;
            public ContainerRecord(int record,string code,string type,int x,int y,int cells)
            {this.record=record;this.code=code;this.type=type;this.x=x;this.y=y;this.cells=cells;}
            public string Id=>SourcePrefix+code;
            public string RecordId=>SourcePrefix+"record:"+record;
            public Vector2 Position=>FactorySourceCoordinates.Placement(x,y,cells);
            public string AssetPath=>"Assets/Remains/RoomDrafts/Containers/"+type+".asset";
            public string LootPath=>"Assets/Remains/RoomDrafts/Containers/office53-"+type+"-loot.asset";
        }
        internal static readonly ContainerRecord[] Containers={
            new(38,"Bf6ZE9xBKE8ABZ5b","explbox",1,23,1),
            new(39,"IB0Ok16gZlifX67F","weapbox",45,23,2),
            new(40,"iX0lf6vnsIAfRQqE","medbox",44,22,1),
            new(41,"jfV1kHAk9SCKk0h5","instr1",19,5,2)};

        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var officeAsset=Require<GameObject>(FactoryOfficeDraft.Path);
            var connectedAsset=Require<GameObject>(ConnectedFactoryAuthoring.Path);
            var prior=RoomContentBatch.Read(connectedAsset.GetComponent<RoomAuthoring>(),"factory:0");
            var local=RoomPhysicalContent.Read(officeAsset.GetComponent<RoomAuthoring>(),"factory:0");
            if(prior.physical.patch=="factory:0:"+ConnectedPatch)
            {
                FactoryOfficeLootChecks.Run();
                Debug.Log("Четыре офисных контейнера уже сохранены; повторная команда проверила данные без замены авторских правок.");return;
            }
            var expansion=CreateExpansion(prior.physical);
            bool resumed=local.patch=="factory:0:"+OfficePatch;
            if(!resumed && local.patch!="factory:0:"+FactoryOfficeDraft.PhysicalPatch)
                throw new InvalidOperationException("Офисы53: неизвестная физическая версия; авторские данные не заменены: "+local.patch);
            ValidateSource(officeAsset.GetComponent<RoomImportAudit>().Report,resumed);
            FactoryOfficeLootChecks.CheckOfficeStage(officeAsset.GetComponent<RoomAuthoring>());
            string geometry=StateJson.ToJson(connectedAsset.GetComponent<RoomAuthoring>().CreateLayout());
            string other=OtherContent(prior);
            var retainedProps=prior.physical.props.Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
            var newIds=Containers.Select(c=>"factory:0:"+c.Id).ToArray();
            var retainedContainers=prior.physical.containers.Where(c=>!newIds.Contains(c.id)).Select(c=>(c.id,json:StateJson.ToJson(c))).ToArray();
            var retainedLoot=prior.physical.loot.Where(c=>!newIds.Contains(c.container)).Select(c=>(c.container,json:StateJson.ToJson(c))).ToArray();

            // Resource authoring is guarded and idempotent; existing assets are verified, never overwritten.
            var definitions=FactoryOfficeLootArtworkAuthoring.Install();
            if(definitions.Length!=4 || !definitions.Select(d=>d.CreateDefinition().id).OrderBy(id=>id).SequenceEqual(Containers.Select(c=>c.type).OrderBy(id=>id)))
                throw new InvalidOperationException("Офисы53: ресурсный контракт не вернул четыре явных типа контейнера.");
            var catalog=EntityAssetCatalog.Load();string oldActors=catalog.ActorsJson();
            var oldProps=catalog.CreateProps().Select(p=>(p.id,json:StateJson.ToJson(p))).ToArray();
            foreach(var definition in definitions) catalog.AddProp(definition);
            if(catalog.ActorsJson()!=oldActors || oldProps.Any(p=>catalog.CreateProps().All(q=>q.id!=p.id || StateJson.ToJson(q)!=p.json)))
                throw new InvalidOperationException("Офисы53: регистрация контейнеров изменила прежний каталог.");
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();PropCatalog.Load();
            foreach(var entry in Containers) LootAsset(entry);
            if(!resumed)
            {
                var office=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
                try
                {
                    var room=office.GetComponent<RoomAuthoring>();var report=office.GetComponent<RoomImportAudit>().Report;
                    ValidateSource(report,false);var records=StateJson.Copy(report.Records);
                    foreach(var entry in Containers)
                    {
                        if(office.GetComponentsInChildren<RoomContainerPlacement>(true).Any(p=>p.Read(room).id==entry.Id))
                            throw new InvalidOperationException("Офисы53: частичная расстановка не заменена: "+entry.Id);
                        var asset=Require<PropAsset>(entry.AssetPath);var data=asset.CreateDefinition();
                        var marker=new GameObject(data.name+" · "+entry.code);marker.transform.SetParent(office.transform,false);
                        marker.transform.localPosition=entry.Position;
                        var placement=marker.AddComponent<RoomContainerPlacement>();placement.Configure(entry.Id,asset);placement.SetLoot(LootAsset(entry));
                        var preview=(GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab.gameObject,marker.transform);
                        preview.transform.localPosition=data.hasPhysicalBody?Vector3.up*(data.height*.5f):Vector3.zero;
                        var record=records.Single(r=>r.recordId==entry.RecordId);record.installed=true;
                        record.reason="Исходный тип, code и anchor сохранены; общий контейнер с самостоятельной фиксированной таблицей Unity. "+
                            (data.hasPhysicalBody?"Переносимое физическое тело из отдельного определения. ":"Настенная аптечка: закреплена, физическое тело/масса отсутствуют. ")+
                            "Исходные замки, навыки и условный loot generator не реализованы. См. FACTORY_OFFICE_LOOT_SPEC.md.";
                    }
                    var content=office.GetComponent<RoomPhysicalContent>();content.Configure(OfficePatch);
                    content.ConfigureExpansion(FactoryOfficeDraft.PhysicalPatch,Containers.Select(c=>c.Id).ToArray());
                    string hash=new SerializedObject(report).FindProperty("sourceHash").stringValue;
                    report.Configure(report.SourceId,hash,records);EditorUtility.SetDirty(report);
                    FactoryOfficeLootChecks.CheckOffice(room);
                    if(PrefabUtility.SaveAsPrefabAsset(office,FactoryOfficeDraft.Path)==null)
                        throw new InvalidOperationException("Офисы53: Unity не сохранил четыре контейнера.");
                    AssetDatabase.SaveAssets();
                }
                finally {PrefabUtility.UnloadPrefabContents(office);}
            }
            else FactoryOfficeLootChecks.CheckOffice(officeAsset.GetComponent<RoomAuthoring>());
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=connected.GetComponent<RoomAuthoring>();var actual=RoomContentBatch.Read(room,"factory:0");
                if(actual.physical.containers.Length!=31 || retainedProps.Any(p=>actual.physical.props.All(q=>q.id!=p.id || StateJson.ToJson(q)!=p.json)) ||
                    retainedContainers.Any(p=>actual.physical.containers.All(q=>q.id!=p.id || StateJson.ToJson(q)!=p.json)) ||
                    retainedLoot.Any(p=>actual.physical.loot.All(q=>q.container!=p.container || StateJson.ToJson(q)!=p.json)) ||
                    StateJson.ToJson(room.CreateLayout())!=geometry || OtherContent(actual)!=other)
                    throw new InvalidOperationException("Офисы53: добавление контейнеров изменило прежнюю геометрию/объекты/NPC/двери/механизмы либо вложенный модуль не обновился.");
                var content=connected.GetComponent<RoomPhysicalContent>();content.Configure(ConnectedPatch);
                content.ConfigureExpansion(expansion.previousPatch,expansion.additions,expansion.earlierVersions);
                FactoryOfficeLootChecks.Check(room);ConnectedFactoryChecks.Check(room);FactoryOfficeConnectionChecks.Check(room);FactoryOfficeNpcChecks.Check(room);
                if(PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path)==null)
                    throw new InvalidOperationException("Офисы53: Unity не сохранил добавочную версию связного завода.");
                AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            FactoryOfficeLootChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Офисы53: сохранены 4 общих контейнера, всего 16 в офисе и 31 в связном заводе. Ещё 17 исходных записей офиса остаются неподключёнными; замки/навыки этих контейнеров отложены.");
        }
        internal static void ValidateSource(RoomImportReportAsset report,bool installed)
        {
            if(report.SourceId!="original:plant:53" || report.Records.Length!=46 ||
                new SerializedObject(report).FindProperty("sourceHash").stringValue!="e008b7795261a45fe627eaa921ad764ba0492365fc5e91b93b097251a57767d8")
                throw new InvalidOperationException("Офисы53: изменился исходный журнал четырёх контейнеров.");
            foreach(var entry in Containers)
            {
                var record=report.Records.SingleOrDefault(r=>r.recordId==entry.RecordId);
                if(record==null || record.code!=entry.code || record.kind!=entry.type || record.sourceX!=entry.x || record.sourceY!=entry.y || record.installed!=installed)
                    throw new InvalidOperationException("Офисы53: исходный контейнер/решение изменены: "+entry.RecordId);
                var xml=XElement.Parse(record.sourceData);
                if(xml.Name!="obj" || xml.Attributes().Count()!=4 || (string)xml.Attribute("id")!=entry.type || (string)xml.Attribute("code")!=entry.code ||
                    (int?)xml.Attribute("x")!=entry.x || (int?)xml.Attribute("y")!=entry.y)
                    throw new InvalidOperationException("Офисы53: source XML имеет другие координаты или неизвестные overrides: "+entry.RecordId);
            }
        }
        internal static ContentExpansion CreateExpansion(PhysicalLayout prior)
        {
            string prefix=prior.level+":";
            var office=prior.props.Cast<PropState>().Concat(prior.containers).Where(p=>p.id.StartsWith(prefix+SourcePrefix,StringComparison.Ordinal) &&
                !Containers.Any(c=>p.id==prefix+c.Id)).Select(p=>p.id).ToArray();
            var steps=new[]{(ContentExpansionStep)prior.expansion}.Concat(prior.expansion?.earlierVersions??Array.Empty<ContentExpansionStep>()).ToArray();
            if(prior.patch!=prefix+FactoryOfficeConnectionAuthoring.PhysicalPatch || office.Length!=15 || steps.Length!=3 ||
                steps[0]?.previousPatch!=prefix+FactoryToolStandAuthoring.ConnectedPatch || !Same(steps[0].additions,office) ||
                steps[1].previousPatch!=prefix+"base:factory-connected-46-18:physical-v2" || !Same(steps[1].additions,office.Concat(new[]{prefix+FactoryToolStandAuthoring.SourceId})) ||
                steps[2].previousPatch!=prefix+"base:factory-connected-46-18:physical-v1" || !Same(steps[2].additions,office.Concat(new[]{prefix+FactoryToolStandAuthoring.SourceId,
                    prefix+"original:plant:18:ZoIAvJZMFks7XYbh",prefix+"original:plant:18:Ainu8fDKejKYdTIz"})))
                throw new InvalidOperationException("Офисы53: прежние 15 офисных объектов/цепочки v4/v3/v2/v1 изменены; дополнение не заменяет их.");
            string Local(string id)=>id.StartsWith(prefix,StringComparison.Ordinal)?id.Substring(prefix.Length):throw new InvalidOperationException("Чужой уровень дополнения: "+id);
            var additions=Containers.Select(c=>c.Id).ToArray();
            return new ContentExpansion {previousPatch=Local(prior.patch),additions=additions,
                earlierVersions=steps.Select(s=>new ContentExpansionStep {previousPatch=Local(s.previousPatch),additions=s.additions.Select(Local).Concat(additions).ToArray()}).ToArray()};
        }
        internal static ContainerLootTable CreateLoot(string type)
        {
            ContainerLootEntry Entry(string id,int count,WeaponItemState weapon=null)=>new() {id=id,definition=id,minimum=count,maximum=count,weaponState=weapon};
            var entries=type switch {
                "explbox"=>new[]{Entry("grenade",1)},
                "weapbox"=>new[]{Entry("pistol",1,new WeaponItemState {rounds=6}),Entry("p10",10)},
                "medbox"=>new[]{Entry("heal",2)},
                "instr1"=>new[]{Entry("pin",2),Entry("rep",1),Entry("metal_comp",2)},
                _=>throw new InvalidOperationException("Нет явной таблицы офисного контейнера: "+type)};
            return new ContainerLootTable {id="base:office53-"+type,version="1",entries=entries};
        }
        private static ContainerLootAsset LootAsset(ContainerRecord entry)
        {
            var expected=CreateLoot(entry.type);var existing=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(entry.LootPath);
            if(existing!=null)
            {
                if(StateJson.ToJson(existing.Read())!=StateJson.ToJson(expected))
                    throw new InvalidOperationException("Офисы53: авторская таблица лута отличается; автоматическая замена запрещена: "+entry.LootPath);
                return existing;
            }
            if(AssetDatabase.LoadMainAssetAtPath(entry.LootPath)!=null) throw new InvalidOperationException("Путь таблицы лута занят: "+entry.LootPath);
            var asset=ScriptableObject.CreateInstance<ContainerLootAsset>();asset.Configure(expected);AssetDatabase.CreateAsset(asset,entry.LootPath);return asset;
        }
        private static string OtherContent(RoomContentBatch batch)=>StateJson.ToJson(new {batch.npcs,batch.doors,batch.mechanisms,batch.travelPoints});
        private static bool Same(System.Collections.Generic.IEnumerable<string> a,System.Collections.Generic.IEnumerable<string> b)=>a.OrderBy(id=>id).SequenceEqual(b.OrderBy(id=>id));
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Офисы53: отсутствует сохранённый ассет: "+path);
    }
}
