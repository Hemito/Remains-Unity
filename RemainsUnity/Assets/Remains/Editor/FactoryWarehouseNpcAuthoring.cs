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
    // Explicit Unity encounter authoring; original category generators are not implemented.
    public static class FactoryWarehouseNpcAuthoring
    {
        public const string Path="Assets/Remains/RoomDrafts/FactoryWarehouse40.prefab";
        public const string Patch="base:factory-warehouse-40:npcs-v1";
        internal const string SourcePrefix="original:plant:40:";
        internal const string SourceHash="3a33d6e9f9781454197e5cb9540f4d83c1647a83d526e19ef040af245c764268";
        internal sealed class SpawnRecord
        {
            public readonly int record,x,y;
            public readonly string code,type;
            public SpawnRecord(int record,string code,int x,int y,string type)
            {this.record=record;this.code=code;this.x=x;this.y=y;this.type=type;}
            public string Id=>SourcePrefix+code;
            public string RecordId=>SourcePrefix+"record:"+record;
            public string Kind=>type=="radroach"?"enl1":"enl2";
            public Vector2 Position=>FactorySourceCoordinates.Placement(x,y,type=="radroach"?1:2);
            public string SpawnPath=>type=="radroach"?FactoryCrawlerAuthoring.SpawnPath:FactoryWorkshopNpcAuthoring.SpawnPath;
        }
        internal static readonly SpawnRecord[] Spawns={
            new(28,"HgGFufTkTc07eLeK",22,7,"raider"),
            new(29,"Q3gwDKcdnENkVieU",16,7,"raider"),
            new(31,"pVfOTv65QLSvinrI",32,15,"radroach"),
            new(32,"bXVPdDiZ3abWLJ2F",35,15,"radroach"),
            new(44,"VESzwfuM5sPYIUFK",27,7,"raider"),
            new(76,"jnu1YAW8KNvJN69Q",43,19,"raider"),
            new(77,"XhzDlLF70QOPytxw",38,19,"raider"),
            new(78,"UyIar4dlRLpPdhTH",13,23,"raider"),
            new(79,"qHKoh2REO2sqNOEk",10,19,"raider")};

        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var saved=Require<GameObject>(Path).GetComponent<RoomAuthoring>();
            if(saved==null) throw new InvalidOperationException("Склад40: у prefab нет RoomAuthoring: "+Path);
            if(FactoryWarehouseNpcChecks.IsInstalled(saved))
            {
                FactoryWarehouseNpcChecks.Check(saved);
                Debug.Log("Склад40: девять NPC уже сохранены; повторная команда проверила расстановку без замены авторских данных.");return;
            }
            var report=saved.GetComponent<RoomImportAudit>()?.Report;
            ValidateSource(report,false);
            var geometry=saved.CreateLayout(false);var physical=RoomPhysicalContent.Read(saved,"factory:0");
            var doors=RoomDoorContent.Read(saved,"factory:0");
            var preview=new NpcLayout {level="factory:0",patch="factory:0:"+Patch,
                actors=Spawns.Select(s=>Require<NpcSpawnAsset>(s.SpawnPath).Create(new TrainingPlacement
                    {id=s.Id,type=s.type,x=s.Position.x,y=s.Position.y,facing=-1},"factory:0")).ToArray()};
            foreach(var entry in Spawns)
            {
                var inventory=Require<NpcSpawnAsset>(entry.SpawnPath).ReadInventory();
                if(inventory==null) throw new InvalidOperationException("Склад40: у выбранного NPC нет начального инвентаря: "+entry.RecordId);
                inventory.Validate();
            }
            FactoryWarehouseNpcChecks.CheckGeometry(geometry,physical,doors?.doors,preview);
            string before=Snapshot(saved);
            var prefab=PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var room=prefab.GetComponent<RoomAuthoring>();
                if(FactoryWarehouseNpcChecks.IsInstalled(room) || Snapshot(room)!=before)
                    throw new InvalidOperationException("Склад40: данные изменились между проверкой и открытием prefab; ничего не заменено.");
                foreach(var entry in Spawns)
                {
                    var marker=new GameObject((entry.type=="raider"?"Рейдер":"Радтаракан")+" · "+entry.code);
                    marker.transform.SetParent(prefab.transform,false);marker.transform.localPosition=entry.Position;
                    var placement=marker.AddComponent<RoomActorPlacement>();placement.Configure(entry.Id,entry.type,-1,false,null);
                    placement.SetSpawn(Require<NpcSpawnAsset>(entry.SpawnPath));
                }
                prefab.AddComponent<RoomNpcContent>().Configure(Patch);
                FactoryWarehouseNpcChecks.CheckInstalled(room,false);
                if(Snapshot(room)!=before) throw new InvalidOperationException("Склад40: создание NPC изменило геометрию, старое содержимое или исходные записи.");
                // Only the nine decisions change; IDs, raw XML and all other source metadata remain exact.
                var records=StateJson.Copy(report.Records);
                foreach(var entry in Spawns)
                {
                    var record=records.Single(r=>r.recordId==entry.RecordId);record.installed=true;
                    record.reason="Явная фиксированная встреча Unity: "+(entry.type=="raider"?"рейдер с пистолетом":"радтаракан с естественной атакой")+
                        "; исходная категория не гарантирует вид. Source anchor сохранён без смещения. См. FACTORY_WAREHOUSE_ENCOUNTER_SPEC.md.";
                }
                report.Configure(report.SourceId,SourceHash,records);
                FactoryWarehouseNpcChecks.Check(room);
                if(Snapshot(room)!=before) throw new InvalidOperationException("Склад40: обновление журнала затронуло данные вне девяти решений NPC.");
                if(PrefabUtility.SaveAsPrefabAsset(prefab,Path)==null)
                    throw new InvalidOperationException("Склад40: Unity не сохранил девять добавочных NPC.");
                EditorUtility.SetDirty(report);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
            FactoryWarehouseNpcChecks.Run();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Склад40: сохранены 7 рейдеров и 2 радтаракана на исходных anchors. Связный завод и его цепочки обновления этой командой не изменены.");
        }
        internal static void ValidateSource(RoomImportReportAsset report,bool installed)
        {
            if(report==null || report.SourceId!="original:plant:40" || report.Records.Length!=57 ||
                new SerializedObject(report).FindProperty("sourceHash").stringValue!=SourceHash)
                throw new InvalidOperationException("Склад40: не подтверждён исходный журнал из 57 объектов.");
            ValidateRecords(report.Records,installed);
        }
        public static void ValidateRecords(RoomSourceRecord[] records,bool installed)
        {
            var candidates=records?.Where(r=>r.kind is "enl1" or "enl2").ToArray();
            if(records?.Length!=57 || candidates?.Length!=9)
                throw new InvalidOperationException("Склад40: требуется ровно девять ground-категорий среди 57 source records.");
            foreach(var entry in Spawns)
            {
                var matches=candidates.Where(r=>r.recordId==entry.RecordId).ToArray();
                if(matches.Length!=1) throw new InvalidOperationException("Склад40: исходный NPC отсутствует/повторён: "+entry.RecordId);
                var record=matches[0];
                if(record.kind!=entry.Kind || record.code!=entry.code || record.sourceX!=entry.x || record.sourceY!=entry.y || record.installed!=installed)
                    throw new InvalidOperationException("Склад40: изменены категория/anchor/решение NPC: "+entry.RecordId);
                var xml=XElement.Parse(record.sourceData);
                if(xml.Name!="obj" || xml.Attributes().Count()!=4 || (string)xml.Attribute("id")!=entry.Kind ||
                    (string)xml.Attribute("code")!=entry.code || (int?)xml.Attribute("x")!=entry.x || (int?)xml.Attribute("y")!=entry.y)
                    throw new InvalidOperationException("Склад40: у source XML NPC изменены координаты или появились overrides: "+entry.RecordId);
            }
        }
        [Serializable] private sealed class RetainedContent
        {
            public RoomLayout geometry;
            public PhysicalLayout physical;
            public DoorLayout doors;
            public MechanismLayout mechanisms;
            public TravelPoint[] travelPoints;
            public string sourceId,sourceHash;
            public RoomSourceRecord[] otherRecords;
        }
        internal static string Snapshot(RoomAuthoring room)
        {
            var batch=RoomContentBatch.Read(room,"factory:0");var report=room.GetComponent<RoomImportAudit>()?.Report;
            if(report==null) throw new InvalidOperationException("Склад40: нет исходного журнала для сравнения сохранённых данных.");
            return Snapshot(room.CreateLayout(),batch,report.SourceId,new SerializedObject(report).FindProperty("sourceHash").stringValue,report.Records);
        }
        internal static string Snapshot(RoomLayout geometry,RoomContentBatch batch,string sourceId,string sourceHash,RoomSourceRecord[] records)
            =>StateJson.ToJson(new RetainedContent {geometry=geometry,physical=batch.physical,doors=batch.doors,mechanisms=batch.mechanisms,
                travelPoints=batch.travelPoints,sourceId=sourceId,sourceHash=sourceHash,
                otherRecords=records.Where(r=>Spawns.All(s=>s.RecordId!=r.recordId)).ToArray()});
        private static T Require<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??
            throw new InvalidOperationException("Склад40: отсутствует согласованный сохранённый ассет: "+path);
    }
}
