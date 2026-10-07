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
    // Fixed new Unity encounter, never the source category generator.
    public static class FactoryContinuationNpcAuthoring
    {
        public const string Path=FactoryContinuationObjectsAuthoring.Path;
        public const string Patch="base:factory-continuation-27:npcs-v1";
        public const string SpawnPath=FactoryCrawlerAuthoring.SpawnPath;
        public const int DeferredRecord=64;
        public const string DeferredCode="icyxG67X0pCmenkm";
        internal sealed class Spawn
        {
            public readonly int number,x,y;public readonly string code;
            public Spawn(int number,string code,int x,int y) {this.number=number;this.code=code;this.x=x;this.y=y;}
            public string Id=>FactoryContinuationObjectsAuthoring.Prefix+code;
            public string RecordId=>FactoryContinuationObjectsAuthoring.Prefix+"record:"+number;
            public Vector2 Anchor=>FactorySourceCoordinates.Placement(x,y,1);
        }
        internal static readonly Spawn[] Records={new(25,"IZ1Jf2mzKH9o9JJS",3,15),new(55,"Rnj78F8NMUqvEzzx",28,23),
            new(56,"SjFM7jYrnw8g33Hy",22,23),new(57,"ZbXVUpeAdcLhefqW",34,23),new(58,"LKT4YfvmifjQra9Y",41,23),
            new(59,"h7lU4gtrWXuLOfHX",39,19),new(60,"eWxHy0jAj5QWsf07",43,19),new(62,"Jhels65uIWVtFV3V",36,15),
            new(63,"CX9b7iGox573yLqh",30,15),new(65,"HItr8Fi4pz7vMDdT",17,19),
            new(66,"HXRI0zCLyKOx9KQI",22,19),new(67,"adyiAKlXzA5O682i",25,19),new(68,"k8Dti2MCb0ulFmJC",9,11)};
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var saved=FactoryContinuationObjectsAuthoring.Load<GameObject>(Path).GetComponent<RoomAuthoring>();
            FactoryContinuationObjectsChecks.CheckLocal(saved);
            if(saved.GetComponent<RoomNpcContent>()!=null) {FactoryContinuationNpcChecks.Check(saved);Debug.Log("Продолжение27: 13 NPC уже сохранены; проверены без замены. Исходная запись64 остаётся pending из-за физического тела дивана33.");return;}
            Require(saved.GetComponentsInChildren<RoomActorPlacement>(true).Length==0,"Частичные NPC markers без версии; не заменены.");ValidateReport(saved,false);
            var spawn=FactoryContinuationObjectsAuthoring.Load<NpcSpawnAsset>(SpawnPath);Require(spawn.ReadInventory()!=null,"Нет начального инвентаря выбранного радтаракана.");spawn.ReadInventory().Validate();
            var preview=CreatePreview(spawn,"factory:0");
            FixedEncounterGeometryChecks.Validate(FactoryContinuationConnectionGeometry.InModule(saved),RoomPhysicalContent.Read(saved,"factory:0"),RoomDoorContent.Read(saved,"factory:0").doors,preview.actors,"продолжение27/source anchors/связный уровень");
            string before=Snapshot(saved);var root=PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();Require(Snapshot(room)==before,"Prefab изменился между preflight/load.");
                foreach(var record in Records)
                {
                    var marker=new GameObject("Радтаракан · "+record.code);marker.transform.SetParent(root.transform,false);marker.transform.localPosition=record.Anchor;
                    var placement=marker.AddComponent<RoomActorPlacement>();placement.Configure(record.Id,"radroach",-1,false,null);placement.SetSpawn(spawn);
                }
                root.AddComponent<RoomNpcContent>().Configure(Patch);FactoryContinuationNpcChecks.Check(room,false);
                Require(Snapshot(room)==before,"NPC добавление изменило terrain/physical/doors/старые системы/source.");
                var report=room.GetComponent<RoomImportAudit>().Report;var records=StateJson.Copy(report.Records);
                foreach(var record in Records)
                {var r=records.Single(r=>r.recordId==record.RecordId);r.installed=true;
                    r.reason="Самостоятельная фиксированная Unity встреча: существующий радтаракан, HP8, естественная атака и инвентарь. Исходная категория enl1 не гарантирует вид; генератор не переносится. Anchor без сдвига/нового пола. Исходный runtime не проверен.";}
                report.Configure(report.SourceId,FactoryContinuationObjectsAuthoring.SourceHash,records);FactoryContinuationNpcChecks.Check(room);
                Require(Snapshot(room)==before,"Изменены данные вне 13 source решений, включая отложенную запись64.");
                Require(PrefabUtility.SaveAsPrefabAsset(root,Path)!=null,"Не сохранены 13 NPC.");EditorUtility.SetDirty(report);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            FactoryContinuationNpcChecks.Check(FactoryContinuationObjectsAuthoring.Load<GameObject>(Path).GetComponent<RoomAuthoring>());
            Debug.Log("Продолжение27: 13 фиксированных радтараканов, source40/44; rat/term3/work и enl1:64 остаются pending. Запись64 пересекает физическое тело исходного дивана33; anchor/диван не изменены. Connected chain не изменена.");
        }
        internal static NpcLayout CreatePreview(NpcSpawnAsset spawn,string level)=>new() {level=level,patch=level+":"+Patch,
            actors=Records.Select(r=>spawn.Create(new TrainingPlacement {id=r.Id,type="radroach",x=r.Anchor.x,y=r.Anchor.y,facing=-1},level)).ToArray(),
            loot=Records.Select(r=>new NpcLootBinding {actor=level+":"+r.Id,table=spawn.ReadInventory()}).ToArray()};
        internal static void ValidateReport(RoomAuthoring room,bool installed)
        {
            var report=room.GetComponent<RoomImportAudit>()?.Report;Require(report!=null && report.SourceId=="original:plant:27" && report.Records.Length==44 &&
                new SerializedObject(report).FindProperty("sourceHash").stringValue==FactoryContinuationObjectsAuthoring.SourceHash,"Не подтверждён source journal44.");
            foreach(var entry in Records)
            {
                var r=report.Records.Single(r=>r.recordId==entry.RecordId);var xml=XElement.Parse(r.sourceData);
                Require(r.kind=="enl1" && r.code==entry.code && r.sourceX==entry.x && r.sourceY==entry.y && r.installed==installed && xml.Name=="obj" && xml.Attributes().Count()==4 &&
                    (string)xml.Attribute("id")=="enl1" && (string)xml.Attribute("code")==entry.code && (int?)xml.Attribute("x")==entry.x && (int?)xml.Attribute("y")==entry.y,
                    "Изменён enl1 source record/overrides: "+entry.RecordId);
            }
            var deferred=report.Records.Single(r=>r.recordId==FactoryContinuationObjectsAuthoring.Prefix+"record:"+DeferredRecord);
            var deferredXml=XElement.Parse(deferred.sourceData);
            Require(!deferred.installed && deferred.kind=="enl1" && deferred.code==DeferredCode && deferred.sourceX==20 && deferred.sourceY==15 &&
                deferredXml.Name=="obj" && deferredXml.Attributes().Count()==4 && (string)deferredXml.Attribute("id")=="enl1" &&
                (string)deferredXml.Attribute("code")==DeferredCode && (int?)deferredXml.Attribute("x")==20 && (int?)deferredXml.Attribute("y")==15 &&
                !string.IsNullOrWhiteSpace(deferred.reason),"Отложенная запись64 установлена или её исходные metadata изменены; конфликт с диваном33 не скрывается.");
        }
        private sealed class SnapshotDto {public RoomLayout geometry;public RoomContentBatch content;public RoomSourceRecord[] records;}
        internal static string Snapshot(RoomAuthoring room)
        {
            var content=StateJson.Copy(RoomContentBatch.Read(room,"factory:0"));content.npcs=null;
            var records=StateJson.Copy(room.GetComponent<RoomImportAudit>().Report.Records);
            foreach(var r in records.Where(r=>Records.Any(owned=>owned.RecordId==r.recordId))) {r.installed=false;r.reason="owned NPC decision normalized";}
            return StateJson.ToJson(new SnapshotDto {geometry=room.CreateLayout(),content=content,records=records});
        }
        internal static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("NPC продолжения27: "+reason);}
    }
}
