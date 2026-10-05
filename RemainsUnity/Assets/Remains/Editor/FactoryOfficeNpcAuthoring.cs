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
    public static class FactoryOfficeNpcAuthoring
    {
        public const string ConnectedPatch="base:factory-connected-46-18:npcs-v4";
        public const string OfficePatch="base:factory-office-53:npcs-v1";
        internal const string SourcePrefix="original:plant:53:";
        internal sealed class SpawnRecord
        {
            public readonly int record,x,y;
            public readonly string code,type;
            public readonly float offsetX;
            public SpawnRecord(int record,string code,int x,int y,string type,float offsetX=0)
            {this.record=record;this.code=code;this.x=x;this.y=y;this.type=type;this.offsetX=offsetX;}
            public string Id=>SourcePrefix+code;
            public string RecordId=>SourcePrefix+"record:"+record;
            public Vector2 Position=>FactorySourceCoordinates.Placement(x,y,type=="radroach"?1:2)+Vector2.right*offsetX;
            public string SpawnPath=>type=="radroach"?FactoryCrawlerAuthoring.SpawnPath:FactoryWorkshopNpcAuthoring.SpawnPath;
        }
        // Explicit fixed Unity encounter. Source categories do not promise these species/counts.
        internal static readonly SpawnRecord[] Spawns={
            new(62,"asgXcpTEfby23bgi",17,5,"radroach",.8f),
            new(63,"lAIXKaUQC2e4JtW5",45,15,"radroach"),
            new(64,"ql47iXGSZW2VGGdF",34,10,"raider"),
            new(65,"IpYV76WRjtmgXV2M",23,23,"raider"),
            new(68,"eb4N723M2J7Ve4OV",42,10,"raider")};

        // Additive authoring only: no build/import, asset replacement or runtime generation.
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var officeAsset=Require<GameObject>(FactoryOfficeDraft.Path);
            var connectedAsset=Require<GameObject>(ConnectedFactoryAuthoring.Path);
            var prior=RoomNpcContent.Read(connectedAsset.GetComponent<RoomAuthoring>(),"factory:0");
            var currentOffice=RoomNpcContent.Read(officeAsset.GetComponent<RoomAuthoring>(),"factory:0");
            if(prior?.patch=="factory:0:"+ConnectedPatch)
            {
                FactoryOfficeNpcChecks.Check(connectedAsset.GetComponent<RoomAuthoring>());
                Debug.Log("Встреча офисов53 уже сохранена; повторная команда проверила состояние без замены авторских данных.");return;
            }
            var expansion=CreateExpansion(prior);
            if(currentOffice!=null && currentOffice.patch!="factory:0:"+OfficePatch)
                throw new InvalidOperationException("Офисы53: неизвестная прежняя версия NPC, patch="+currentOffice.patch);
            foreach(var entry in Spawns)
            {
                var spawn=Require<NpcSpawnAsset>(entry.SpawnPath);
                var actor=spawn.Create(new TrainingPlacement {id=entry.Id,type=entry.type,x=entry.Position.x,y=entry.Position.y,facing=-1},"factory:0");
                var hull=Anatomy.Movement(actor);var geometry=officeAsset.GetComponent<RoomAuthoring>().CreateLayout(false);
                if(!ActorTerrainMotion.IsClear(geometry,hull,actor.Position) || !ActorTerrainMotion.Supported(geometry,hull,actor.Position))
                    throw new InvalidOperationException($"Офисы53: NPC не имеет свободного тела/опоры до сохранения, record={entry.RecordId}, position={actor.Position}.");
                spawn.ReadInventory()?.Validate();
            }
            string geometryBefore=StateJson.ToJson(connectedAsset.GetComponent<RoomAuthoring>().CreateLayout());
            var batchBefore=RoomContentBatch.Read(connectedAsset.GetComponent<RoomAuthoring>(),"factory:0");
            string unchanged=OtherContent(batchBefore);
            var retained=prior.actors.Where(a=>!a.id.StartsWith("factory:0:"+SourcePrefix,StringComparison.Ordinal)).Select(a=>(a.id,json:StateJson.ToJson(a))).ToArray();
            var retainedLoot=prior.loot.Where(b=>!b.actor.StartsWith("factory:0:"+SourcePrefix,StringComparison.Ordinal)).Select(b=>(b.actor,json:StateJson.ToJson(b))).ToArray();

            // A stopped command after the office save can resume only from this exact authored v1.
            if(currentOffice==null)
            {
                var office=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
                try
                {
                    var room=office.GetComponent<RoomAuthoring>();var report=office.GetComponent<RoomImportAudit>().Report;
                    ValidateSource(report,false);
                    if(office.GetComponent<RoomNpcContent>()!=null || office.GetComponentsInChildren<RoomActorPlacement>(true).Length!=0)
                        throw new InvalidOperationException("Офисы53: обнаружены ранее созданные NPC; авторские записи не заменены.");
                    var records=StateJson.Copy(report.Records);
                    foreach(var entry in Spawns)
                    {
                        var marker=new GameObject((entry.type=="radroach"?"Радтаракан":"Рейдер")+" · "+entry.code);
                        marker.transform.SetParent(office.transform,false);marker.transform.localPosition=entry.Position;
                        var placement=marker.AddComponent<RoomActorPlacement>();placement.Configure(entry.Id,entry.type,-1,false,null);
                        placement.SetSpawn(Require<NpcSpawnAsset>(entry.SpawnPath));
                        var record=records.Single(r=>r.recordId==entry.RecordId);record.installed=true;
                        record.reason="Явная фиксированная встреча Unity: "+(entry.type=="radroach"?"радтаракан с естественной атакой":"рейдер с пистолетом")+
                            "; исходная категория не гарантирует вид. "+(entry.offsetX==0?"Исходный anchor сохранён.":"Явная поправка Unity +0,8м X: исходный anchor над разрывом пола; sourceX/sourceY не изменены.")+" См. FACTORY_OFFICE_ENCOUNTER_SPEC.md.";
                    }
                    office.AddComponent<RoomNpcContent>().Configure(OfficePatch);
                    FactoryOfficeNpcChecks.CheckOffice(room,false);
                    var serialized=new SerializedObject(report);string hash=serialized.FindProperty("sourceHash").stringValue;
                    report.Configure(report.SourceId,hash,records);EditorUtility.SetDirty(report);
                    FactoryOfficeNpcChecks.CheckOffice(room,true);
                    if(PrefabUtility.SaveAsPrefabAsset(office,FactoryOfficeDraft.Path)==null)
                        throw new InvalidOperationException("Офисы53: Unity не сохранил добавочные NPC.");
                    AssetDatabase.SaveAssets();
                }
                finally {PrefabUtility.UnloadPrefabContents(office);}
            }
            else FactoryOfficeNpcChecks.CheckOffice(officeAsset.GetComponent<RoomAuthoring>(),true);

            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=connected.GetComponent<RoomAuthoring>();var layout=RoomNpcContent.Read(room,"factory:0");
                if(layout.patch!="factory:0:"+FactoryCrawlerAuthoring.Patch || layout.actors.Length!=13 ||
                    retained.Any(a=>layout.actors.All(b=>b.id!=a.id || StateJson.ToJson(b)!=a.json)) ||
                    retainedLoot.Any(a=>layout.loot.All(b=>b.actor!=a.actor || StateJson.ToJson(b)!=a.json)) ||
                    OtherContent(RoomContentBatch.Read(room,"factory:0"))!=unchanged || StateJson.ToJson(room.CreateLayout())!=geometryBefore)
                    throw new InvalidOperationException("Офисы53: добавление NPC изменило прежнее содержимое/геометрию либо не дошло до вложенного модуля. Прежние данные не перезаписаны.");
                var content=connected.GetComponent<RoomNpcContent>();content.Configure(ConnectedPatch);content.ConfigureExpansion(expansion);
                FactoryOfficeNpcChecks.Check(room);ConnectedFactoryChecks.Check(room);FactoryOfficeConnectionChecks.Check(room);
                if(PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path)==null)
                    throw new InvalidOperationException("Офисы53: Unity не сохранил добавочную версию связного завода.");
                AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            FactoryOfficeNpcChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Встреча офисов53: сохранены 2 радтаракана и 3 рейдера; связный завод содержит 13 NPC. Ещё 21 исходный объект офиса остаётся неподключённым.");
        }
        internal static void ValidateSource(RoomImportReportAsset report,bool installed)
        {
            if(report.SourceId!="original:plant:53" || report.Records.Length!=46) throw new InvalidOperationException("Офисы53: изменился исходный журнал NPC.");
            var npcRecords=report.Records.Where(r=>r.kind is "enl1" or "enl2").ToArray();
            if(npcRecords.Length!=5) throw new InvalidOperationException("Офисы53: требуется ровно пять исходных категорий NPC.");
            foreach(var entry in Spawns)
            {
                var record=npcRecords.SingleOrDefault(r=>r.recordId==entry.RecordId);
                if(record==null || record.code!=entry.code || record.sourceX!=entry.x || record.sourceY!=entry.y ||
                    record.kind!=(entry.type=="radroach"?"enl1":"enl2") || record.installed!=installed)
                    throw new InvalidOperationException("Офисы53: категория/координаты/решение source record изменены: "+entry.RecordId);
                var xml=XElement.Parse(record.sourceData);
                if(xml.Name!="obj" || xml.Attributes().Count()!=4 || (string)xml.Attribute("id")!=record.kind || (string)xml.Attribute("code")!=entry.code ||
                    (int?)xml.Attribute("x")!=entry.x || (int?)xml.Attribute("y")!=entry.y)
                    throw new InvalidOperationException("Офисы53: source XML содержит другие координаты или неизвестные overrides: "+entry.RecordId);
            }
        }
        internal static ContentExpansion CreateExpansion(NpcLayout prior)
        {
            const string level="factory:0:";
            if(prior?.patch!=level+FactoryCrawlerAuthoring.Patch || prior.expansion?.previousPatch!=level+FactoryWorkshopNpcAuthoring.Patch ||
                prior.expansion.earlierVersions?.Length!=1 || prior.expansion.earlierVersions[0].previousPatch!=level+FactoryWorkshopNpcAuthoring.PreviousPatch)
                throw new InvalidOperationException("Офисы53: нужны проверенные прежние версии NPC v3/v2/v1; цепочка не заменена.");
            var old=prior.actors.Where(a=>!a.id.StartsWith(level+SourcePrefix,StringComparison.Ordinal)).ToArray();
            var oldCrawler=old.Where(a=>a.actorType=="radroach").Select(a=>a.id).ToArray();
            var oldWorkshop=old.Where(a=>a.id.StartsWith(level+"original:plant:18:",StringComparison.Ordinal)).Select(a=>a.id).ToArray();
            if(old.Length!=8 || oldCrawler.Length!=2 || oldWorkshop.Length!=7 ||
                !prior.expansion.additions.OrderBy(id=>id).SequenceEqual(oldCrawler.OrderBy(id=>id)) ||
                !prior.expansion.earlierVersions[0].additions.OrderBy(id=>id).SequenceEqual(oldWorkshop.OrderBy(id=>id)))
                throw new InvalidOperationException("Офисы53: прежние 8 NPC или цепочки добавлений изменены; требуется отдельный разбор.");
            string Local(string id)=>id.StartsWith(level,StringComparison.Ordinal)?id.Substring(level.Length):
                throw new InvalidOperationException("Офисы53: ID NPC принадлежит другому уровню: "+id);
            var additions=Spawns.Select(s=>s.Id).ToArray();
            return new ContentExpansion {previousPatch=FactoryCrawlerAuthoring.Patch,additions=additions,
                earlierVersions=new[]{new ContentExpansionStep {previousPatch=FactoryWorkshopNpcAuthoring.Patch,additions=prior.expansion.additions.Select(Local).Concat(additions).ToArray()},
                    new ContentExpansionStep {previousPatch=FactoryWorkshopNpcAuthoring.PreviousPatch,additions=prior.expansion.earlierVersions[0].additions.Select(Local).Concat(additions).ToArray()}}};
        }
        private static string OtherContent(RoomContentBatch batch)=>StateJson.ToJson(new {batch.physical,batch.doors,batch.mechanisms,batch.travelPoints});
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??
            throw new InvalidOperationException("Офисы53: отсутствует согласованный сохранённый ассет: "+path);
    }
}
