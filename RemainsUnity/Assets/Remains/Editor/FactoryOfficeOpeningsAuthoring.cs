using System;
using System.IO;
using System.Globalization;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit source records and independent Unity policy. No source generator/terrain algorithm.
    public static class FactoryOfficeOpeningsAuthoring
    {
        public const string OfficePatch="base:factory-office-53:doors-v2";
        public const string ConnectedPatch="base:factory-connected-46-18:doors-v3";
        internal const string Folder="Assets/Remains/Resources/Presentation/Doors";
        internal const string Prefix="original:plant:53:";
        [Serializable] internal sealed class Definition
        {
            public string type,id,name,surfaceTexture,opening,closing,destruction;
            public float[] size,interactionSize;
            public bool interactive;
            public float maxHealth;
            public Vector2 Size=>new(size[0],size[1]);
            public Vector2 InteractionSize=>new(interactionSize[0],interactionSize[1]);
        }
        [Serializable] internal sealed class Placement
        {
            public int record,x,y;
            public string type,code;
            public float[] anchor;
            public string Id=>Prefix+code;
            public string RecordId=>Prefix+"record:"+record;
            public Vector2 Position=>new(anchor[0],anchor[1]);
        }
        [Serializable] internal sealed class Reference {public int record;public string code,targetCode,disposition;}
        [Serializable] internal sealed class Data
        {
            public int schema;
            public string sourceId,sourceHash,officePrevious,officePatch,connectedPrevious,connectedPatch,terrainDisposition;
            public Definition[] definitions;
            public Placement[] placements;
            public Reference reference;
        }
        [Serializable] internal sealed class ResourcesManifest {public GraphicType[] types;}
        [Serializable] internal sealed class GraphicType {public string id;public GraphicState[] states;}
        [Serializable] internal sealed class GraphicState {public string label,resource,sha256;public int[] pixels;public float pixelsPerUnit;public float[] pngPivotPixels;}
        internal static string Research(string file)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Research/OfficeOpeningsResources",file));
        internal static Data ReadData(string researchPath=null)
        {
            var data=StateJson.FromJson<Data>(File.ReadAllText(researchPath==null?Research("unity-authoring.json"):Path.Combine(researchPath,"unity-authoring.json")));
            Require(data?.schema==1 && data.sourceId=="original:plant:53" && data.sourceHash=="e008b7795261a45fe627eaa921ad764ba0492365fc5e91b93b097251a57767d8" &&
                data.officePatch==OfficePatch && data.connectedPatch==ConnectedPatch && data.officePrevious==FactoryOfficeDraft.DoorPatch &&
                data.connectedPrevious==FactoryOfficeConnectionAuthoring.DoorPatch,"Изменён источник или версии explicit authoring данных.");
            Require(data.definitions?.Length==6 && data.placements?.Length==6 &&
                data.definitions.Select(d=>d.type).OrderBy(id=>id).SequenceEqual(new[]{"door1","door1a","door1b","hatch1","septum","window1"}) &&
                data.placements.Select(p=>p.record).OrderBy(n=>n).SequenceEqual(new[]{26,29,30,31,44,84}),"Нужны ровно шесть типов и шесть unique source placements.");
            Require(data.placements.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()==6 &&
                data.reference?.record==28 && data.reference.code=="zlnR8eeCKCDkaJ8P" && data.reference.targetCode=="NMeCLKXlWsv6CYYL" &&
                data.reference.disposition=="coincident-provenance-reference-no-entity","Изменён coincident reference28 или повтор source IDs.");
            foreach(var definition in data.definitions)
                Require(definition.id=="office-"+definition.type && !string.IsNullOrWhiteSpace(definition.name) &&
                    definition.size?.Length==2 && definition.interactionSize?.Length==2 && definition.maxHealth>0 && float.IsFinite(definition.maxHealth) &&
                    definition.size.Concat(definition.interactionSize).All(v=>float.IsFinite(v)&&v>0) &&
                    !string.IsNullOrWhiteSpace(definition.surfaceTexture) && !string.IsNullOrWhiteSpace(definition.destruction) &&
                    (definition.interactive? !string.IsNullOrWhiteSpace(definition.opening)&&!string.IsNullOrWhiteSpace(definition.closing):definition.opening==null&&definition.closing==null),
                    "Неполное самостоятельное определение: "+definition.id);
            foreach(var placement in data.placements)
                Require(placement.anchor?.Length==2 && data.definitions.Count(d=>d.type==placement.type)==1 &&
                    Vector2.Distance(placement.Position,FactorySourceCoordinates.Placement(placement.x,placement.y,placement.type=="hatch1"?2:1))<.0001f,
                    "Anchor не совпадает с source cell registration: "+placement.RecordId);
            var resource=JObject.Parse(File.ReadAllText(researchPath==null?Research("manifest.json"):Path.Combine(researchPath,"manifest.json")));
            foreach(var definition in data.definitions)
            {
                var original=resource["types"].Single(t=>(string)t["id"]==definition.type);var attributes=original["definition"];
                Require(definition.maxHealth==float.Parse((string)attributes["hp"],CultureInfo.InvariantCulture) && definition.interactive==((string)attributes["inter"]=="1") &&
                    Vector2.Distance(definition.Size,new Vector2((float)original["unityBlockingSize"][0],(float)original["unityBlockingSize"][1]))<.0001f &&
                    definition.opening==(string)attributes["open"] && definition.closing==(string)attributes["close"] && definition.destruction==(string)attributes["die"],
                    "HP/size/interaction/audio не совпадают с явным исходным content contract: "+definition.id);
            }
            foreach(var placement in data.placements)
            {
                var record=resource["records"].Single(r=>(string)r["recordId"]==placement.RecordId);var attributes=record["source"];
                Require((string)attributes["id"]==placement.type && (string)attributes["code"]==placement.code &&
                    (int)attributes["x"]==placement.x && (int)attributes["y"]==placement.y &&
                    Vector2.Distance(placement.Position,new Vector2((float)record["localUnityAnchor"][0],(float)record["localUnityAnchor"][1]))<.0001f,
                    "Placement не совпадает с исходным resource manifest: "+placement.RecordId);
            }
            return data;
        }
        public static string AssetPath(string id)=>Folder+"/"+id+".asset";
        public static string PrefabPath(string id)=>Folder+"/"+id+".prefab";

        [MenuItem("Remains/Контент/Установить проёмы офисов53")]
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();PropCatalog.Load();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            var data=ReadData();var officeAsset=Asset<GameObject>(FactoryOfficeDraft.Path);var connectedAsset=Asset<GameObject>(ConnectedFactoryAuthoring.Path);
            var prior=RoomContentBatch.Read(connectedAsset.GetComponent<RoomAuthoring>(),"factory:0");
            var local=RoomDoorContent.Read(officeAsset.GetComponent<RoomAuthoring>(),"factory:0");
            bool resumed=local.patch=="factory:0:"+OfficePatch;
            if(prior.doors.patch=="factory:0:"+ConnectedPatch) {IdempotentVerify();return;}
            Require(prior.doors.patch=="factory:0:"+data.connectedPrevious && (resumed || local.patch=="factory:0:"+data.officePrevious),
                "Неизвестная предыдущая версия; авторские данные не заменены.");
            ValidateSource(officeAsset.GetComponent<RoomImportAudit>().Report,resumed);
            if(resumed) FactoryOfficeOpeningsChecks.CheckOffice(officeAsset.GetComponent<RoomAuthoring>());
            var predecessor=resumed?FactoryOfficeOpeningsChecks.ConnectedPredecessorForResume(prior.doors,data):prior.doors;
            var expansion=CreateExpansion(predecessor,true);
            string geometry=StateJson.ToJson(connectedAsset.GetComponent<RoomAuthoring>().CreateLayout());
            string other=OtherContent(prior);
            var retained=predecessor.doors.Select(d=>(d.id,json:StateJson.ToJson(d))).ToArray();
            string bindings=Bindings(connectedAsset.GetComponent<RoomAuthoring>(),data);
            InstallAssets(data);
            if(!resumed)
            {
                var office=PrefabUtility.LoadPrefabContents(FactoryOfficeDraft.Path);
                try
                {
                    var room=office.GetComponent<RoomAuthoring>();var report=office.GetComponent<RoomImportAudit>().Report;
                    ValidateSource(report,false);var records=StateJson.Copy(report.Records);
                    var before=RoomContentBatch.Read(room,"factory:0");string originalGeometry=StateJson.ToJson(room.CreateLayout());
                    string originalOther=OtherContent(before),originalBindings=Bindings(room,data);
                    var officeExpansion=CreateExpansion(before.doors,false);
                    foreach(var entry in data.placements)
                    {
                        Require(!room.GetComponentsInChildren<RoomDoorAuthoring>(true).Any(p=>p.Read(room,"factory:0").id==entry.Id),"Частичная расстановка не заменена: "+entry.Id);
                        var definition=data.definitions.Single(d=>d.type==entry.type);
                        var marker=(GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(PrefabPath(definition.id)),room.transform);
                        marker.name=definition.name+" · "+entry.code;marker.transform.localPosition=entry.Position;
                        marker.AddComponent<RoomDoorAuthoring>().Configure(entry.Id,false,definition.surfaceTexture);
                        var record=records.Single(r=>r.recordId==entry.RecordId);record.installed=true;
                        record.reason="Исходные type/code/cell/графическая регистрация сохранены. Закреплённая "+
                            (definition.interactive?"дверь/люк с E":"разрушаемая преграда без E")+"; явные SO HP/проём/E rect. Начально закрыта и явно без замка: locks/context distribution/skills не реализованы, source equivalence не заявлена. См. FACTORY_OFFICE_OPENINGS_SPEC.md.";
                    }
                    var reference=records.Single(r=>r.code==data.reference.code);reference.installed=true;
                    reference.reason="Подключено только provenance reference к record27/NMeCLKXlWsv6CYYL: source совпадает в(28;5). Собственный collider/view/interactive/state/ID мира не создан; прежняя дверь27 сохранена.";
                    var content=office.GetComponent<RoomDoorContent>();content.Configure(OfficePatch);content.ConfigureExpansion(officeExpansion);
                    report.Configure(data.sourceId,data.sourceHash,records);EditorUtility.SetDirty(report);
                    var current=RoomContentBatch.Read(room,"factory:0");
                    Require(StateJson.ToJson(room.CreateLayout())==originalGeometry && OtherContent(current)==originalOther && Bindings(room,data)==originalBindings,
                        "Установка изменила прежние geometry/artwork/bindings/physical/NPC/mechanisms/travel.");
                    FactoryOfficeOpeningsChecks.CheckOffice(room);
                    Require(PrefabUtility.SaveAsPrefabAsset(office,FactoryOfficeDraft.Path)!=null,"Unity не сохранил офисный модуль.");AssetDatabase.SaveAssets();
                }
                finally {PrefabUtility.UnloadPrefabContents(office);}
            }
            var connected=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                var room=connected.GetComponent<RoomAuthoring>();var actual=RoomContentBatch.Read(room,"factory:0");
                Require(actual.doors.doors.Length==15 && retained.All(p=>actual.doors.doors.Any(d=>d.id==p.id && StateJson.ToJson(d)==p.json)) &&
                    StateJson.ToJson(room.CreateLayout())==geometry && OtherContent(actual)==other && Bindings(room,data)==bindings,
                    "Связный завод потерял прежнюю дверь/привязку/geometry/artwork/physical/NPC/mechanisms либо nested prefab не обновился.");
                var content=connected.GetComponent<RoomDoorContent>();content.Configure(ConnectedPatch);content.ConfigureExpansion(expansion);
                FactoryOfficeOpeningsChecks.Check(room);
                Require(PrefabUtility.SaveAsPrefabAsset(connected,ConnectedFactoryAuthoring.Path)!=null,"Unity не сохранил связный завод.");AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(connected);}
            IdempotentVerify();RoomAuthoringScenes.ValidateSavedOverrides();
            Debug.Log("Проёмы офисов53: сохранены6 unique entities и provenance reference28; офис9/связный завод15 дверей,44/46 записей. Две lov pending. Исходные locks/context/skills и original runtime не подтверждены.");
        }
        public static void IdempotentVerify()
        {
            CheckAssets();FactoryOfficeOpeningsChecks.Run();
            Debug.Log("Проёмы офисов53: повторная проверка сохранённых SO/prefab/ID/цепочек без перезаписи авторских данных — PASS.");
        }
        internal static void ValidateSource(RoomImportReportAsset report,bool installed)
        {
            var data=ReadData();Require(report.SourceId==data.sourceId && report.Records.Length==46 &&
                new SerializedObject(report).FindProperty("sourceHash").stringValue==data.sourceHash,"Изменился исходный журнал.");
            foreach(var entry in data.placements)
            {
                var r=report.Records.SingleOrDefault(r=>r.recordId==entry.RecordId);
                Require(r!=null && r.code==entry.code && r.kind==entry.type && r.sourceX==entry.x && r.sourceY==entry.y && r.installed==installed,
                    "Изменены source record/решение: "+entry.RecordId);
                var xml=XElement.Parse(r.sourceData);Require(xml.Name=="obj" && xml.Attributes().Count()==4 && (string)xml.Attribute("id")==entry.type &&
                    (string)xml.Attribute("code")==entry.code && (int?)xml.Attribute("x")==entry.x && (int?)xml.Attribute("y")==entry.y,
                    "Изменён source XML/overrides: "+entry.RecordId);
            }
            var reference=report.Records.Single(r=>r.code==data.reference.code);var target=report.Records.Single(r=>r.code==data.reference.targetCode);
            Require(reference.recordId==Prefix+"record:28" && reference.kind=="door1" && reference.sourceX==28 && reference.sourceY==5 &&
                reference.installed==installed && target.installed && target.kind==reference.kind && target.sourceX==reference.sourceX && target.sourceY==reference.sourceY,
                "Reference28 потерян, скрыт или превратился в другую исходную дверь.");
        }
        internal static ContentExpansion CreateExpansion(DoorLayout prior,bool connected,Data supplied=null)
        {
            var data=supplied??ReadData();string prefix=prior.level+":";
            Require(prior.patch==prefix+(connected?data.connectedPrevious:data.officePrevious) && prior.doors.Length==(connected?9:3),
                "Неизвестный или неполный predecessor дверей: "+prior.patch);
            var additions=data.placements.Select(p=>p.Id).ToArray();
            string Local(string id)=>id.StartsWith(prefix,StringComparison.Ordinal)?id.Substring(prefix.Length):throw new InvalidOperationException("Проёмы: чужой уровень: "+id);
            if(!connected) Require(prior.expansion==null,"У прежнего офиса неожиданная дверная цепочка.");
            else Require(prior.expansion?.previousPatch==prefix+"base:factory-connected-46-18:doors-v1" && prior.expansion.additions.Length==3 &&
                prior.expansion.earlierVersions.Length==0 && prior.expansion.additions.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(
                    new[]{"NMeCLKXlWsv6CYYL","foMRNLiLOHCLfItd","ffoNKTl2qsZVBCG3"}.Select(code=>prefix+Prefix+code).OrderBy(id=>id,StringComparer.Ordinal)),
                "Потеряна точная прежняя дверная цепочка root v1→v2.");
            var earlier=prior.expansion==null?Array.Empty<ContentExpansionStep>():new[]{(ContentExpansionStep)prior.expansion}.Concat(prior.expansion.earlierVersions)
                .Select(s=>new ContentExpansionStep {previousPatch=Local(s.previousPatch),additions=s.additions.Select(Local).Concat(additions).ToArray()}).ToArray();
            return new ContentExpansion {previousPatch=Local(prior.patch),additions=additions,earlierVersions=earlier};
        }
        internal static void InstallAssets(Data data)
        {
            foreach(var definition in data.definitions)
            {
                var states=StateJson.FromJson<ResourcesManifest>(File.ReadAllText(Research("manifest.json"))).types.Single(t=>t.id==definition.type).states;
                var closed=Sprite(states.Single(s=>s.label=="close"));var opened=Sprite(states.Single(s=>s.label=="open"));var destroyed=Sprite(states.Single(s=>s.label=="die"));
                AudioClip Sound(string id)=>id==null?null:Asset<AudioClip>("Assets/Remains/Resources/Original/Room/OfficeOpenings/Audio/"+id+(id.StartsWith("break_",StringComparison.Ordinal)?".wav":".mp3"));
                var asset=AssetDatabase.LoadAssetAtPath<DoorAsset>(AssetPath(definition.id));
                if(asset==null)
                {
                    Require(AssetDatabase.LoadMainAssetAtPath(AssetPath(definition.id))==null,"Путь SO занят: "+definition.id);
                    asset=ScriptableObject.CreateInstance<DoorAsset>();asset.name=definition.name;
                    asset.ConfigureBarrier(definition.id,definition.Size,closed,opened,destroyed,Sound(definition.opening),Sound(definition.closing),Sound(definition.destruction),definition.interactive,definition.maxHealth,definition.InteractionSize);
                    AssetDatabase.CreateAsset(asset,AssetPath(definition.id));
                }
                CheckAsset(asset,definition,states);
                string materialPath=Folder+"/"+definition.id+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null)
                {
                    Require(AssetDatabase.LoadMainAssetAtPath(materialPath)==null,"Путь материала занят: "+materialPath);
                    material=WorldArtworkMaterial.Create(closed.texture);AssetDatabase.CreateAsset(material,materialPath);
                }
                Require(material.mainTexture==closed.texture && material.shader==Resources.Load<Shader>("Presentation/WorldLit"),"Авторский материал отличается, замена запрещена: "+materialPath);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(definition.id));
                if(prefab==null)
                {
                    Require(AssetDatabase.LoadMainAssetAtPath(PrefabPath(definition.id))==null,"Путь prefab занят: "+definition.id);
                    var root=new GameObject(definition.name);
                    try
                    {
                        var renderer=root.AddComponent<SpriteRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                        root.AddComponent<DoorView>().Configure(asset,renderer);
                        Require(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(definition.id))!=null,"Не сохранён prefab: "+definition.id);
                    }
                    finally {UnityEngine.Object.DestroyImmediate(root);}
                }
                CheckPrefab(definition);
            }
            AssetDatabase.SaveAssets();
        }
        public static void CheckAssets()
        {
            var data=ReadData();var manifest=StateJson.FromJson<ResourcesManifest>(File.ReadAllText(Research("manifest.json")));
            foreach(var definition in data.definitions) {CheckAsset(Asset<DoorAsset>(AssetPath(definition.id)),definition,manifest.types.Single(t=>t.id==definition.type).states);CheckPrefab(definition);}
        }
        private static Sprite Sprite(GraphicState state)
        {
            string path="Assets/Remains/Resources/"+state.resource+".png";var sprite=Asset<Sprite>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            using var hash=System.Security.Cryptography.SHA256.Create();string digest=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
            Require(digest==state.sha256 && sprite.texture.width==state.pixels[0] && sprite.texture.height==state.pixels[1] &&
                sprite.rect==new Rect(0,0,state.pixels[0],state.pixels[1]) && sprite.pixelsPerUnit==state.pixelsPerUnit &&
                Vector2.Distance(sprite.pivot,new Vector2(state.pngPivotPixels[0],state.pngPivotPixels[1]))<.001f && importer!=null &&
                importer.spriteImportMode==SpriteImportMode.Single && importer.textureType==TextureImporterType.Sprite && !importer.mipmapEnabled &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && importer.wrapModeU==TextureWrapMode.Clamp && importer.wrapModeV==TextureWrapMode.Clamp &&
                importer.filterMode==(state.pixelsPerUnit==200?FilterMode.Bilinear:FilterMode.Point),"Исходный Sprite/hash/import/pivot отличается: "+path);
            return sprite;
        }
        private static void CheckAsset(DoorAsset asset,Definition expected,GraphicState[] states)
        {
            asset.Validate();
            Require(asset.Id==expected.id && asset.Size==expected.Size && asset.InteractionSize==expected.InteractionSize && asset.Interactive==expected.interactive &&
                asset.MaxHealth==expected.maxHealth && asset.Closed==Sprite(states.Single(s=>s.label=="close")) && asset.Opened==Sprite(states.Single(s=>s.label=="open")) &&
                asset.Destroyed==Sprite(states.Single(s=>s.label=="die")),"Авторский SO отличается, замена запрещена: "+expected.id);
            string SoundPath(AudioClip clip)=>clip==null?null:AssetDatabase.GetAssetPath(clip);
            string Expected(string id)=>id==null?null:"Assets/Remains/Resources/Original/Room/OfficeOpenings/Audio/"+id+(id.StartsWith("break_",StringComparison.Ordinal)?".wav":".mp3");
            Require(SoundPath(asset.OpenSound)==Expected(expected.opening) && SoundPath(asset.CloseSound)==Expected(expected.closing) && SoundPath(asset.DestroySound)==Expected(expected.destruction),
                "Авторские звуки отличаются, замена запрещена: "+expected.id);
        }
        private static void CheckPrefab(Definition definition)
        {
            var prefab=Asset<GameObject>(PrefabPath(definition.id));var view=prefab.GetComponent<DoorView>();var material=Asset<Material>(Folder+"/"+definition.id+".mat");
            Require(view!=null && view.Definition==Asset<DoorAsset>(AssetPath(definition.id)) && view.Artwork!=null && view.Artwork.transform==prefab.transform &&
                view.Artwork.sprite==view.Definition.Closed && view.Artwork.sharedMaterial==material && view.Artwork.sortingOrder==ActorVisualLayers.RestingObjectOrder &&
                prefab.transform.localScale==Vector3.one && prefab.transform.localRotation==Quaternion.identity && prefab.GetComponentsInChildren<Collider2D>(true).Length==0 &&
                prefab.GetComponentsInChildren<Rigidbody2D>(true).Length==0 && prefab.GetComponentsInChildren<RoomPhysicalPlacement>(true).Length==0,
                "Prefab отличается от закреплённого DoorView или создаёт вторую физику/TK: "+definition.id);
        }
        internal static string Bindings(RoomAuthoring room,Data data)
        {
            var ids=data.placements.Select(p=>p.Id).ToArray();
            return StateJson.ToJson(room.GetComponentsInChildren<RoomDoorAuthoring>(true).Where(room.Included).Where(p=>!ids.Contains(p.Read(room,"factory:0").id))
                .Select(p=>new {state=p.Read(room,"factory:0"),prefab=AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(p.GetComponent<DoorView>().gameObject)),
                    definition=AssetDatabase.GetAssetPath(p.GetComponent<DoorView>().Definition),sprite=AssetDatabase.GetAssetPath(p.GetComponent<DoorView>().Artwork.sprite),
                    material=AssetDatabase.GetAssetPath(p.GetComponent<DoorView>().Artwork.sharedMaterial),rotation=p.transform.localRotation,scale=p.transform.localScale}).OrderBy(p=>p.state.id));
        }
        private static string OtherContent(RoomContentBatch batch)=>StateJson.ToJson(new {batch.physical,batch.npcs,batch.mechanisms,batch.travelPoints});
        internal static T Asset<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Проёмы офисов53: отсутствует сохранённый ассет: "+path);
        internal static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Проёмы офисов53: "+reason);}
    }
}
