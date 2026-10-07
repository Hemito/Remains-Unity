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
    public static class FactoryContinuationObjectsAuthoring
    {
        public const string Path="Assets/Remains/RoomDrafts/FactoryContinuation27.prefab";
        public const string PhysicalPrevious="base:factory-continuation-27:physical-v1",PhysicalPatch="base:factory-continuation-27:physical-v2";
        public const string DoorPrevious="base:factory-continuation-27:doors-v1",DoorPatch="base:factory-continuation-27:doors-v2";
        public const string Prefix="original:plant:27:";
        public const string SourceHash="2300f72c02bf58273641760f36a8847ecf5e071257a47af631d2df8f468d04ec";
        internal sealed class Placement
        {
            public readonly int record,x,y,size;public readonly string kind,code;
            public Placement(int record,string kind,string code,int x,int y,int size) {this.record=record;this.kind=kind;this.code=code;this.x=x;this.y=y;this.size=size;}
            public string Id=>Prefix+code;public string RecordId=>Prefix+"record:"+record;
            public Vector2 Anchor=>FactorySourceCoordinates.Placement(x,y,size);
            public bool Barrier=>kind=="platform1";
        }
        internal static readonly Placement[] Records={new(28,"cup","BNhmeNonzjYCRmpA",17,15,2),new(29,"cup","qgesiLTW2tpBNq9n",35,19,2),
            new(44,"wallcab","Ru7orXdjq82U1G4B",45,18,1),new(45,"wallcab","z170CoQxi9l2EbNC",2,14,1),
            new(50,"platform1","yXugjFWQjw6IYnFl",18,8,3),new(54,"platform1","acyM5Ph0UBQIcnZX",27,8,3)};
        public static string LootPath(string type) {Require(type is "cup" or "wallcab","Нет таблицы лута: "+type);return FactoryContinuationObjectsArtworkAuthoring.Folder+"/"+type+"-loot.asset";}
        public static ContainerLootTable CreateLoot(string type)
        {
            Require(type is "cup" or "wallcab","Неизвестный initial loot тип: "+type);
            ContainerLootEntry Item(string id,int count)=>new() {id=id,definition=id,minimum=count,maximum=count};
            return new ContainerLootTable {id="base:continuation27-"+type,version="1",entries=type=="cup"?
                new[]{Item("caps",4),Item("pin",1),Item("kombu_comp",1)}:new[]{Item("caps",3),Item("metal_comp",1)}};
        }
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();WeaponCatalog.Load();
            var saved=Load<GameObject>(Path).GetComponent<RoomAuthoring>();Require(saved!=null,"Нет RoomAuthoring продолжения27.");
            var beforePhysical=RoomPhysicalContent.Read(saved,"factory:0");var beforeDoors=RoomDoorContent.Read(saved,"factory:0");
            if(beforePhysical?.patch=="factory:0:"+PhysicalPatch || beforeDoors?.patch=="factory:0:"+DoorPatch)
            {FactoryContinuationObjectsChecks.CheckLocal(saved);Debug.Log("Продолжение27: шесть записей уже установлены; проверено без перезаписи.");return;}
            Require(beforePhysical?.patch=="factory:0:"+PhysicalPrevious && beforeDoors?.patch=="factory:0:"+DoorPrevious &&
                beforePhysical.props.Length==6 && beforePhysical.containers.Length==11 && beforePhysical.loot.Length==11 && beforeDoors.doors.Length==3,
                "Неполная/неизвестная baseline стадия17 physical/3 barriers.");
            BeforeObjects(beforePhysical);BeforeObjects(beforeDoors);ValidateReport(saved,false);
            var icons=new Remains.Presentation.InventoryArtwork();var texts=ItemTextCatalog.Load();
            foreach(var type in new[]{"cup","wallcab"}) foreach(var item in CreateLoot(type).entries)
            {Require(texts.Contains(item.definition),"Нет явных метаданных предмета начального лута: container="+type+", definition="+item.definition+", catalog="+ItemTextCatalog.Resource);icons.Require(item.definition);}
            string old=Snapshot(saved);var assets=FactoryContinuationObjectsArtworkAuthoring.Install();
            var catalog=EntityAssetCatalog.Load();string actors=catalog.ActorsJson();var oldProps=catalog.CreateProps();
            foreach(var asset in assets) catalog.AddProp(asset);
            Require(catalog.ActorsJson()==actors && oldProps.All(p=>catalog.CreateProps().Any(c=>c.id==p.id && StateJson.ToJson(c)==StateJson.ToJson(p))),"Регистрация изменила прежний каталог.");
            foreach(string type in new[]{"cup","wallcab"}) EnsureLoot(type);
            var prefab=PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var room=prefab.GetComponent<RoomAuthoring>();Require(Snapshot(room)==old,"Prefab изменился после preflight.");
                foreach(var record in Records)
                {
                    if(record.Barrier)
                    {
                        var view=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(FactoryContinuationObjectsArtworkAuthoring.PrefabPath(record.kind)),prefab.transform);
                        view.name="Твёрдая платформа · "+record.code;view.transform.localPosition=record.Anchor;
                        view.AddComponent<RoomDoorAuthoring>().Configure(record.Id,false,"door:metal");
                    }
                    else
                    {
                        var marker=new GameObject((record.kind=="cup"?"Шкаф":"Шкафчик")+" · "+record.code);marker.transform.SetParent(prefab.transform,false);marker.transform.localPosition=record.Anchor;
                        var asset=Load<PropAsset>(FactoryContinuationObjectsArtworkAuthoring.AssetPath(record.kind));
                        var placement=marker.AddComponent<RoomContainerPlacement>();placement.Configure(record.Id,asset);placement.SetLoot(Load<ContainerLootAsset>(LootPath(record.kind)));
                        var preview=(GameObject)PrefabUtility.InstantiatePrefab(asset.Prefab.gameObject,marker.transform);
                        preview.transform.localPosition=asset.Prefab.VisualAnchor==Remains.Presentation.PropVisualAnchor.BodyCenter?Vector3.up*asset.CreateDefinition().height*.5f:Vector3.zero;
                    }
                }
                room.GetComponent<RoomPhysicalContent>().Configure(PhysicalPatch);
                room.GetComponent<RoomPhysicalContent>().ConfigureExpansion(PhysicalPrevious,Records.Where(r=>!r.Barrier).Select(r=>r.Id).ToArray());
                room.GetComponent<RoomDoorContent>().Configure(DoorPatch);
                room.GetComponent<RoomDoorContent>().ConfigureExpansion(new ContentExpansion {previousPatch=DoorPrevious,additions=Records.Where(r=>r.Barrier).Select(r=>r.Id).ToArray()});
                FactoryContinuationObjectsChecks.CheckLocal(room,false);
                Require(Snapshot(room)==old,"Добавление изменило geometry/старые physical/doors/другие systems или source data.");
                var report=room.GetComponent<RoomImportAudit>().Report;var entries=StateJson.Copy(report.Records);
                foreach(var record in Records)
                {var entry=entries.Single(r=>r.recordId==record.RecordId);entry.installed=true;
                    entry.reason=record.Barrier?"Неподвижная сплошная platform1: неинтерактивный DoorState, нативное close/пустые open/die, HP5000. Исходный thre800 пока pending; без движения/ремонта. Исходный runtime не проверен.":
                        "Общий шкаф/SO/нативные close/open + самостоятельный фиксированный начальный лут Unity один раз. "+(record.kind=="cup"?"Физическое тело .94×1.6/mass60.16, подвижное.":"Исходный wall2: NoBody/noTK/noSupport, registration16,2px.")+" Исходные замки/навыки/разрушение предметов pending; исходный runtime не проверен.";}
                report.Configure(report.SourceId,SourceHash,entries);FactoryContinuationObjectsChecks.CheckLocal(room);
                Require(Snapshot(room)==old,"Изменены исходные данные вне шести решений.");
                Require(PrefabUtility.SaveAsPrefabAsset(prefab,Path)!=null,"Не сохранён локальный модуль27.");EditorUtility.SetDirty(report);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(prefab);}
            FactoryContinuationObjectsChecks.CheckLocal(Load<GameObject>(Path).GetComponent<RoomAuthoring>());
            Debug.Log("Продолжение27: 4 общих шкафа и 2 неподвижные опоры. Physical6/15/15, doors5; connected factory этой командой не изменён.");
        }
        private static void EnsureLoot(string type)
        {
            var expected=CreateLoot(type);expected.Validate();string path=LootPath(type);var saved=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(path);
            if(saved!=null) {Require(StateJson.ToJson(saved.Read())==StateJson.ToJson(expected),"Авторская таблица отличается, замена запрещена: "+type);return;}
            Require(AssetDatabase.LoadMainAssetAtPath(path)==null && !System.IO.File.Exists(FactoryContinuationObjectsArtworkAuthoring.Project(path)+".meta"),"Путь таблицы занят/имеет orphan meta: "+type);
            var asset=ScriptableObject.CreateInstance<ContainerLootAsset>();asset.Configure(expected);AssetDatabase.CreateAsset(asset,path);
        }
        internal static void ValidateReport(RoomAuthoring room,bool installed)
        {
            var report=room.GetComponent<RoomImportAudit>()?.Report;Require(report!=null && report.SourceId=="original:plant:27" && report.Records.Length==44 &&
                new SerializedObject(report).FindProperty("sourceHash").stringValue==SourceHash,"Нет полного исходного журнала44.");
            foreach(var record in Records)
            {
                var entry=report.Records.Single(r=>r.recordId==record.RecordId);var xml=XElement.Parse(entry.sourceData);
                Require(entry.code==record.code && entry.kind==record.kind && entry.sourceX==record.x && entry.sourceY==record.y && entry.installed==installed &&
                    xml.Name=="obj" && xml.Attributes().Count()==4 && (string)xml.Attribute("id")==record.kind && (string)xml.Attribute("code")==record.code &&
                    (int?)xml.Attribute("x")==record.x && (int?)xml.Attribute("y")==record.y,"Изменён source record/override/decision: "+record.RecordId);
            }
        }
        public static PhysicalLayout BeforeObjects(PhysicalLayout source)
        {
            if(source==null)return null;Require(source.props!=null && source.containers!=null && source.loot!=null && !string.IsNullOrWhiteSpace(source.level),"Неполная physical projection.");
            var ids=Records.Where(r=>!r.Barrier).Select(r=>source.level+":"+r.Id).ToArray();var copy=StateJson.Copy(source);
            if(source.patch!=source.level+":"+PhysicalPatch)
            {Require(!source.props.Cast<PropState>().Concat(source.containers).Any(p=>ids.Contains(p?.id)) && !source.loot.Any(b=>ids.Contains(b?.container)),"Новые IDs скрыты под старой physical версией.");return copy;}
            FactoryContinuationObjectsChecks.CheckNewPhysical(source);
            Require(source.expansion?.previousPatch==source.level+":"+PhysicalPrevious && Same(source.expansion.additions,ids) && source.expansion.earlierVersions?.Length==0,"Неверная local physical expansion.");
            copy.containers=copy.containers.Where(c=>!ids.Contains(c.id)).ToArray();copy.loot=copy.loot.Where(b=>!ids.Contains(b.container)).ToArray();copy.patch=source.expansion.previousPatch;copy.expansion=null;return copy;
        }
        public static DoorLayout BeforeObjects(DoorLayout source)
        {
            if(source==null)return null;Require(source.doors!=null && !string.IsNullOrWhiteSpace(source.level),"Неполная door projection.");
            var ids=Records.Where(r=>r.Barrier).Select(r=>source.level+":"+r.Id).ToArray();var copy=StateJson.Copy(source);
            if(source.patch!=source.level+":"+DoorPatch) {Require(!source.doors.Any(d=>ids.Contains(d?.id)),"Новые platform IDs скрыты под старой door версией.");return copy;}
            FactoryContinuationObjectsChecks.CheckNewDoors(source);
            Require(source.expansion?.previousPatch==source.level+":"+DoorPrevious && Same(source.expansion.additions,ids) && source.expansion.earlierVersions?.Length==0,"Неверная local doors expansion.");
            copy.doors=copy.doors.Where(d=>!ids.Contains(d.id)).ToArray();copy.patch=source.expansion.previousPatch;copy.expansion=null;return copy;
        }
        private sealed class SnapshotDto {public RoomLayout geometry;public RoomContentBatch content;public RoomSourceRecord[] records;}
        internal static string Snapshot(RoomAuthoring room)
        {
            var batch=StateJson.Copy(RoomContentBatch.Read(room,"factory:0"));batch.physical=BeforeObjects(batch.physical);batch.doors=BeforeObjects(batch.doors);
            var records=StateJson.Copy(room.GetComponent<RoomImportAudit>().Report.Records);
            foreach(var r in records.Where(r=>Records.Any(owned=>owned.RecordId==r.recordId))) {r.installed=false;r.reason="owned decision normalized";}
            return StateJson.ToJson(new SnapshotDto {geometry=room.CreateLayout(),content=batch,records=records});
        }
        internal static bool Same(System.Collections.Generic.IEnumerable<string> a,System.Collections.Generic.IEnumerable<string> b)=>a!=null && b!=null && a.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(b.OrderBy(x=>x,StringComparer.Ordinal));
        internal static T Load<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Продолжение27: нет "+typeof(T).Name+": "+path);
        internal static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("Объекты продолжения27: "+reason);}
    }
}
