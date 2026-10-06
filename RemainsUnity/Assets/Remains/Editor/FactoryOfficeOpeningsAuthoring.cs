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
        [Serializable] internal sealed class ResourcesManifest {public int schema;public GraphicType[] types;public SoundEntry[] sounds;}
        [Serializable] internal sealed class SoundEntry {public string id,sourceFile,resource,sha256,rawAssetPath;public long bytes;public UnitySoundPayload unityPayload;}
        [Serializable] internal class SoundPayload {public string path,sha256;public long bytes;}
        [Serializable] internal sealed class UnitySoundPayload:SoundPayload {public int version;public string conversionManifest;}
        [Serializable] internal sealed class PcmPayload:SoundPayload {public int channels,sampleRate,sampleBytes;public long frames;}
        [Serializable] internal sealed class SoundConversion {public int schema;public SoundPayload source;public PcmPayload output;}
        private const string ResearchPrefix="Tools/Research/OfficeOpeningsResources/";
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
            var manifest=ReadResources();ValidateSoundFiles(manifest,Path.GetDirectoryName(Application.dataPath));
            // Explicit authoring command only; complete native import before requiring its AudioClip.
            foreach(var entry in manifest.sounds) AssetDatabase.ImportAsset(SoundAssetPath(manifest,entry.id),ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            foreach(var definition in data.definitions)
            {
                var states=manifest.types.Single(t=>t.id==definition.type).states;
                var closed=Sprite(states.Single(s=>s.label=="close"));var opened=Sprite(states.Single(s=>s.label=="open"));var destroyed=Sprite(states.Single(s=>s.label=="die"));
                AudioClip Sound(string id)=>SoundAsset(manifest,id);
                var asset=AssetDatabase.LoadAssetAtPath<DoorAsset>(AssetPath(definition.id));
                if(asset==null)
                {
                    Require(AssetDatabase.LoadMainAssetAtPath(AssetPath(definition.id))==null,"Путь SO занят: "+definition.id);
                    asset=ScriptableObject.CreateInstance<DoorAsset>();asset.name=definition.name;
                    asset.ConfigureBarrier(definition.id,definition.Size,closed,opened,destroyed,Sound(definition.opening),Sound(definition.closing),Sound(definition.destruction),definition.interactive,definition.maxHealth,definition.InteractionSize);
                    AssetDatabase.CreateAsset(asset,AssetPath(definition.id));
                }
                CheckAsset(asset,definition,states,manifest);
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
            var data=ReadData();var manifest=ReadResources();ValidateSoundFiles(manifest,Path.GetDirectoryName(Application.dataPath));
            foreach(var definition in data.definitions) {CheckAsset(Asset<DoorAsset>(AssetPath(definition.id)),definition,manifest.types.Single(t=>t.id==definition.type).states,manifest);CheckPrefab(definition);}
        }
        internal static ResourcesManifest ReadResources(string researchPath=null)=>StateJson.FromJson<ResourcesManifest>(File.ReadAllText(researchPath==null?Research("manifest.json"):Path.Combine(researchPath,"manifest.json")));
        internal static string SoundAssetPath(ResourcesManifest manifest,string id)
        {
            if(id==null) return null; // Explicit noninteractive definition has no open/close sound.
            Require(manifest?.sounds!=null && manifest.sounds.Count(s=>s?.id==id)==1,"Нет единственного manifest audio entry: "+id);
            var entry=manifest.sounds.Single(s=>s.id==id);string extension=Path.GetExtension(entry.sourceFile);
            Require(entry.resource=="Original/Room/OfficeOpenings/Audio/"+id && (extension==".mp3" || extension==".wav") &&
                Path.GetFileName(entry.sourceFile).EndsWith("_"+id+extension,StringComparison.Ordinal) && ValidHash(entry.sha256) && entry.bytes>0,
                "Некорректный explicit audio path/hash: id="+id+", source="+entry.sourceFile+", resource="+entry.resource);
            Require((entry.rawAssetPath!=null)==(entry.unityPayload!=null),"Неполная регистрация audio conversion: "+id);
            if(entry.unityPayload==null) return "Assets/Remains/Resources/"+entry.resource+extension;
            var converted=entry.unityPayload;
            Require(converted.version==1 && extension==".mp3" && entry.rawAssetPath==ResearchPrefix+"SourceAudio/"+id+extension &&
                converted.path=="RemainsUnity/Assets/Remains/Resources/"+entry.resource+".wav" && ValidHash(converted.sha256) && converted.bytes>0 &&
                converted.conversionManifest!=null && converted.conversionManifest.StartsWith(ResearchPrefix,StringComparison.Ordinal) &&
                converted.conversionManifest.EndsWith(".json",StringComparison.Ordinal) && SafeRelative(converted.conversionManifest),
                "Некорректные версия/пути/hash explicit PCM conversion: id="+id+", path="+converted.path+", manifest="+converted.conversionManifest);
            return converted.path.Substring("RemainsUnity/".Length);
        }
        private static bool ValidHash(string value)=>value?.Length==64 && value.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));
        private static bool SafeRelative(string value)=>!string.IsNullOrWhiteSpace(value) && !value.Contains("\\") && !value.Contains(":") &&
            value.Split('/').All(part=>part.Length>0 && part!="." && part!="..");
        internal static void ValidateSoundFiles(ResourcesManifest manifest,string projectPath)
        {
            Require(manifest?.schema==1 && manifest.sounds?.Length==7 &&
                manifest.sounds.Select(s=>s?.id).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(new[]{"break_glass","break_metal","break_wood","metal_door_close","metal_door_open","wood_door_close","wood_door_open"}),
                "Потерян exact семизвуковой source manifest/version.");
            string repoPath=Path.GetFullPath(Path.Combine(projectPath,".."));
            foreach(var entry in manifest.sounds)
            {
                string assetPath=SoundAssetPath(manifest,entry.id);
                if(entry.unityPayload==null)
                {
                    ReadSoundPayload(Path.Combine(projectPath,assetPath),entry.sha256,entry.bytes,entry.id);continue;
                }
                ReadSoundPayload(Path.Combine(repoPath,entry.rawAssetPath),entry.sha256,entry.bytes,entry.id+" raw");
                string conversionPath=Path.Combine(repoPath,entry.unityPayload.conversionManifest);
                Require(File.Exists(conversionPath),"Нет зарегистрированного conversion manifest: "+entry.id+", path="+conversionPath);
                var conversion=StateJson.FromJson<SoundConversion>(File.ReadAllText(conversionPath));ValidateConversion(entry,conversion);
                byte[] pcm=ReadSoundPayload(Path.Combine(projectPath,assetPath),entry.unityPayload.sha256,entry.unityPayload.bytes,entry.id+" PCM");
                ValidatePcmPayload(pcm,conversion.output,entry.id);
                string stale=Path.Combine(projectPath,"Assets/Remains/Resources/"+entry.resource+Path.GetExtension(entry.sourceFile));
                Require(!File.Exists(stale) && !File.Exists(stale+".meta"),"Исходный неимпортируемый MP3 остался в Unity Assets: id="+entry.id+", path="+stale);
            }
        }
        internal static void ValidateConversion(SoundEntry entry,SoundConversion conversion)
        {
            var target=entry.unityPayload;
            Require(target!=null && conversion?.schema==1 && conversion.source!=null && conversion.output!=null &&
                conversion.source.path==entry.rawAssetPath && conversion.source.sha256==entry.sha256 && conversion.source.bytes==entry.bytes &&
                conversion.output.path==target.path && conversion.output.sha256==target.sha256 && conversion.output.bytes==target.bytes &&
                conversion.output.channels>0 && conversion.output.sampleRate>0 && conversion.output.sampleBytes==2 && conversion.output.frames>0,
                "Conversion manifest не соответствует исходному/Unity payload или PCM16: id="+entry.id);
        }
        private static byte[] ReadSoundPayload(string file,string expectedHash,long expectedBytes,string id)
        {
            Require(File.Exists(file),"Нет manifest audio payload: id="+id+", path="+file+", sha256="+expectedHash);
            byte[] payload=File.ReadAllBytes(file);using var hash=System.Security.Cryptography.SHA256.Create();
            string digest=BitConverter.ToString(hash.ComputeHash(payload)).Replace("-","").ToLowerInvariant();
            Require(payload.LongLength==expectedBytes && digest==expectedHash,"Audio payload отличается: id="+id+", path="+file+", actual="+digest+", expected="+expectedHash);return payload;
        }
        internal static void ValidatePcmPayload(byte[] payload,PcmPayload expected,string id)
        {
            using var stream=new MemoryStream(payload,false);using var reader=new BinaryReader(stream);
            string Tag()=>System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
            Require(payload.Length>=44 && Tag()=="RIFF" && reader.ReadUInt32()==payload.LongLength-8 && Tag()=="WAVE","Некорректный PCM RIFF header: "+id);
            bool format=false,data=false;long dataBytes=0;
            while(stream.Position<stream.Length)
            {
                Require(stream.Length-stream.Position>=8,"Неполный PCM chunk header: "+id);
                string tag=Tag();uint length=reader.ReadUInt32();long end=stream.Position+length;
                Require(end<=stream.Length,"PCM chunk выходит за payload: "+id+", chunk="+tag);
                if(tag=="fmt ")
                {
                    Require(!format && length>=16,"Повторный/неполный PCM fmt chunk: "+id);
                    ushort encoding=reader.ReadUInt16(),channels=reader.ReadUInt16();uint rate=reader.ReadUInt32(),byteRate=reader.ReadUInt32();
                    ushort alignment=reader.ReadUInt16(),bits=reader.ReadUInt16();
                    Require(encoding==1 && channels==expected.channels && rate==expected.sampleRate && bits==16 &&
                        alignment==(long)expected.channels*expected.sampleBytes && byteRate==(long)expected.sampleRate*alignment,
                        "PCM формат отличается от conversion manifest: "+id);format=true;
                }
                if(tag=="data") {Require(!data,"Повторный PCM data chunk: "+id);data=true;dataBytes=length;}
                stream.Position=end+(length&1);
                Require(stream.Position<=stream.Length,"Отсутствует padding PCM chunk: "+id);
            }
            Require(format && data && dataBytes%((long)expected.channels*expected.sampleBytes)==0 && dataBytes/((long)expected.channels*expected.sampleBytes)==expected.frames,"PCM frame count отличается от conversion manifest: "+id);
        }
        private static AudioClip SoundAsset(ResourcesManifest manifest,string id)
        {
            string path=SoundAssetPath(manifest,id);if(path==null) return null;
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var entry=manifest.sounds.Single(s=>s.id==id);
            Require(clip!=null,"Manifest audio payload существует/проверен, но AudioClip не импортирован: id="+id+", path="+path+
                ", guid="+AssetDatabase.AssetPathToGUID(path)+", importer="+(AssetImporter.GetAtPath(path)?.GetType().Name??"<нет>")+
                ", assetCount="+AssetDatabase.LoadAllAssetsAtPath(path).Length+", verifiedSha256="+(entry.unityPayload?.sha256??entry.sha256)+", bytes="+(entry.unityPayload?.bytes??entry.bytes)+". Проверьте native import; другой файл не подставлен.");return clip;
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
        private static void CheckAsset(DoorAsset asset,Definition expected,GraphicState[] states,ResourcesManifest manifest)
        {
            asset.Validate();
            Require(asset.Id==expected.id && asset.Size==expected.Size && asset.InteractionSize==expected.InteractionSize && asset.Interactive==expected.interactive &&
                asset.MaxHealth==expected.maxHealth && asset.Closed==Sprite(states.Single(s=>s.label=="close")) && asset.Opened==Sprite(states.Single(s=>s.label=="open")) &&
                asset.Destroyed==Sprite(states.Single(s=>s.label=="die")),"Авторский SO отличается, замена запрещена: "+expected.id);
            string SoundPath(AudioClip clip)=>clip==null?null:AssetDatabase.GetAssetPath(clip);
            string Expected(string id)=>SoundAssetPath(manifest,id);
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
