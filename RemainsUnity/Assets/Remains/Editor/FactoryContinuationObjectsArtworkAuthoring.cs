using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Resource-only command. It never installs room placements, loot, catalogs or gameplay rules.
    public static class FactoryContinuationObjectsArtworkAuthoring
    {
        public const string Folder="Assets/Remains/RoomDrafts/Continuation27";
        public const string ManifestPath="Tools/Research/FactoryContinuationObjects/manifest.json";
        public const string ResourceFolder="Original/Room/ContinuationObjects";
        public const string PlatformId="continuation-platform1";
        public const float CupMass=60.16f;
        [Serializable] internal sealed class State
        {public string label,resource,stagedPng,sha256,textureGuid,spriteId;public int width,height;public float pixelsPerUnit;public float[] pivotPixels;}
        [Serializable] internal sealed class Policy
        {public float width,height,mass,previewOffsetY,maxHealth;public bool movable,hasPhysicalBody,interactive;public string visualAnchor,definitionId,surfaceTexture;}
        [Serializable] internal sealed class Entry {public string id,nameRu,sourceKind;public int spriteId;public State[] states;public Policy unity;}
        [Serializable] internal sealed class Source {public string textureXmlSha256,allDataSha256,roomXmlSha256,sourceId;}
        [Serializable] internal sealed class Audio {public string id,resource,sourcePath,stagedPath,sha256,extension;}
        [Serializable] internal sealed class Manifest {public int schema;public Source source;public Entry[] types;public Audio[] audio;}
        public static string AssetPath(string id) {Known(id);return Folder+"/"+(id=="platform1"?PlatformId:id)+".asset";}
        public static string PrefabPath(string id) {Known(id);return id=="platform1"?"Assets/Remains/Resources/Presentation/Doors/"+PlatformId+".prefab":Folder+"/"+id+".prefab";}
        public static string MaterialPath(string id) {Known(id);return Folder+"/"+id+".mat";}
        public static string ProfilePath(string id) {Require(id is "cup" or "wallcab","Нет контейнерного профиля: "+id);return ContainerArtworkAuthoring.ProfilePath(id);}
        public static string Resource(string id,string state="closed")
        {Known(id);Require(state is "closed" or "open" || id=="platform1" && state=="die","Нет состояния: "+id+"/"+state);return ResourceFolder+"/"+id+"-"+state;}
        private static void Known(string id)=>Require(id is "cup" or "wallcab" or "platform1","Нет подготовленного source типа: "+id);
        internal static string Repo(string relative)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..",relative));
        internal static string Project(string relative)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",relative));
        internal static string Hash(byte[] bytes) {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static PropDefinition Definition(string id)
        {
            Require(id is "cup" or "wallcab","Platform1 имеет DoorAsset, а не PropAsset: "+id);
            var e=ReadManifest().types.Single(t=>t.id==id);
            return new PropDefinition {id=id,name=e.nameRu,resource=Resource(id),width=e.unity.width,height=e.unity.height,
                mass=e.unity.mass,movable=e.unity.movable,hasPhysicalBody=e.unity.hasPhysicalBody};
        }
        internal static Manifest ReadManifest()
        {
            var data=StateJson.FromJson<Manifest>(File.ReadAllText(Repo(ManifestPath)));
            Require(data?.schema==1 && data.source?.sourceId=="original:plant:27" && data.types?.Length==3 &&
                data.types.Select(t=>t?.id).SequenceEqual(new[]{"cup","wallcab","platform1"}),"Изменён состав/source manifest.");
            foreach(var e in data.types)
            {
                int w=e.id=="cup"?47:e.id=="wallcab"?32:120,h=e.id=="cup"?80:40,sprite=e.id=="cup"?227:e.id=="wallcab"?147:90;
                var pivot=e.id=="cup"?new Vector2(24,40):e.id=="wallcab"?new Vector2(16,2):new Vector2(60,0);
                Require(e.spriteId==sprite && e.sourceKind=="native-bitmap; no8x interpolation" && e.states?.Length==(e.id=="platform1"?3:2) && e.unity!=null,"Изменён native state набор: "+e.id);
                for(int i=0;i<e.states.Length;i++)
                {
                    var s=e.states[i];string label=i==0?"closed":i==1?"open":"die";
                    Require(s!=null && s.label==label && s.resource==Resource(e.id,label) && s.stagedPng=="Tools/Research/FactoryContinuationObjects/resources/"+e.id+"-"+label+".png" &&
                        s.width==w && s.height==h && s.pixelsPerUnit==50 && s.pivotPixels?.Length==2 && s.pivotPixels[0]==pivot.x && s.pivotPixels[1]==pivot.y &&
                        s.sha256?.Length==64 && s.textureGuid?.Length==32 && s.spriteId?.Length==32,"Неполная/изменённая native registration: "+e.id+"/"+label);
                }
                var p=e.unity;
                Require(p.width==(e.id=="cup"?.94f:e.id=="wallcab"?.64f:2.4f) && p.height==(e.id=="cup"?1.6f:.8f) &&
                    p.mass==(e.id=="cup"?CupMass:0) && p.movable==(e.id=="cup") && p.hasPhysicalBody==(e.id=="cup") &&
                    p.visualAnchor==(e.id=="cup"?"BodyCenter":"PlacementAnchor") && p.previewOffsetY==(e.id=="cup"?.8f:0),"Изменён явный body/registration контракт: "+e.id);
                if(e.id=="platform1") Require(p.definitionId==PlatformId && !p.interactive && p.maxHealth==5000 && p.surfaceTexture=="door:metal","Изменена stationary platform policy.");
            }
            foreach(var pair in new[]{(".reference/original/texture1.xml",data.source.textureXmlSha256),
                (".reference/original/dlc-ui/scripts/fe/AllData.as",data.source.allDataSha256),("Tools/Research/FactoryContinuation27/source-room.xml",data.source.roomXmlSha256)})
                Require(Hash(File.ReadAllBytes(Repo(pair.Item1)))==pair.Item2,"Изменён подтверждённый источник: "+pair.Item1);
            Require(data.audio?.Length==3 && data.audio.Select(a=>a.id).SequenceEqual(new[]{"wood_open","fall_wood_big","break_metal"}),"Неполный исходный audio manifest.");
            return data;
        }
        [MenuItem("Remains/Контент/Подготовить шкафы и платформы продолжения27")]
        public static PropAsset[] Install()
        {
            var data=ReadManifest();StageResources(data);AssetDatabase.Refresh();
            Directory.CreateDirectory(Project(Folder));AssetDatabase.Refresh();
            foreach(var e in data.types)
            {
                foreach(var state in e.states) {AssetDatabase.ImportAsset(ImagePath(state),ImportAssetOptions.ForceSynchronousImport);NativeSprite(state);}
                string[] paths=e.id=="platform1"?new[]{AssetPath(e.id),PrefabPath(e.id),MaterialPath(e.id)}:
                    new[]{AssetPath(e.id),PrefabPath(e.id),MaterialPath(e.id),ProfilePath(e.id)};
                int present=paths.Count(p=>File.Exists(Project(p)) || AssetDatabase.LoadMainAssetAtPath(p)!=null);
                Require(present==0 || present==paths.Length,"Частичный SO/prefab/material/profile набор; исправление/GUID замена запрещены: "+e.id);
                if(present==0) Require(paths.All(p=>!File.Exists(Project(p)+".meta")),"Meta без ассета; GUID не заменён: "+e.id);
                else CheckAsset(e);
            }
            foreach(var a in data.audio) {AssetDatabase.ImportAsset(AudioPath(a),ImportAssetOptions.ForceSynchronousImport);AudioClip(a);}
            foreach(var e in data.types)
            {
                if(AssetDatabase.LoadMainAssetAtPath(AssetPath(e.id))!=null) continue;
                var closed=NativeSprite(e.states[0]);var opened=NativeSprite(e.states[1]);var material=WorldArtworkMaterial.Create(closed.texture);
                AssetDatabase.CreateAsset(material,MaterialPath(e.id));
                var root=new GameObject(e.nameRu);
                try
                {
                    var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=closed;renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                    if(e.id=="platform1")
                    {
                        var asset=ScriptableObject.CreateInstance<DoorAsset>();asset.name=e.nameRu;
                        asset.ConfigureBarrier(PlatformId,new Vector2(2.4f,.8f),closed,opened,NativeSprite(e.states[2]),null,null,
                            AudioClip(data.audio.Single(a=>a.id=="break_metal")),false,5000);
                        AssetDatabase.CreateAsset(asset,AssetPath(e.id));root.AddComponent<DoorView>().Configure(asset,renderer);
                    }
                    else
                    {
                        var profile=ScriptableObject.CreateInstance<ContainerArtworkProfileAsset>();profile.Configure(e.id,ContainerArtworkMode.ClosedOpened,closed,opened);
                        AssetDatabase.CreateAsset(profile,ProfilePath(e.id));var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.ConfigureContainerArtwork(profile);
                        if(!e.unity.hasPhysicalBody) view.ConfigurePlacementArtwork();
                        var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(e.id));Require(prefab!=null,"Не сохранён prefab: "+e.id);
                        var asset=ScriptableObject.CreateInstance<PropAsset>();asset.name=e.nameRu;asset.SetDefinition(Definition(e.id));asset.SetPrefab(prefab.GetComponent<PropView>());
                        AssetDatabase.CreateAsset(asset,AssetPath(e.id));continue;
                    }
                    Require(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(e.id))!=null,"Не сохранён prefab платформы.");
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            AssetDatabase.SaveAssets();Check();return new[]{Load<PropAsset>(AssetPath("cup")),Load<PropAsset>(AssetPath("wallcab"))};
        }
        // Full payload preflight precedes any write. Existing files and GUIDs are never replaced.
        private static void StageResources(Manifest data)
        {
            foreach(var s in data.types.SelectMany(e=>e.states))
            {
                Require(Hash(File.ReadAllBytes(Repo(s.stagedPng)))==s.sha256,"Изменён staged PNG: "+s.resource);
                string target=Project(ImagePath(s));if(File.Exists(target)) Require(Hash(File.ReadAllBytes(target))==s.sha256 && File.Exists(target+".meta") && File.ReadAllText(target+".meta").Contains("guid: "+s.textureGuid),"Существующий PNG/GUID отличается: "+s.resource);
                else Require(!File.Exists(target+".meta"),"Одинокая PNG meta: "+s.resource);
            }
            foreach(var a in data.audio)
            {string source=a.stagedPath==null?a.sourcePath:a.stagedPath;Require(Hash(File.ReadAllBytes(Repo(source)))==a.sha256,"Изменён audio payload: "+a.id);
                string target=Project(AudioPath(a));if(File.Exists(target)) Require(Hash(File.ReadAllBytes(target))==a.sha256,"Изменён установленный audio: "+a.id);else Require(a.stagedPath!=null && !File.Exists(target+".meta"),"Нет исходного audio target: "+a.id);}
            foreach(var s in data.types.SelectMany(e=>e.states))
            {string target=Project(ImagePath(s));if(File.Exists(target)) continue;Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Repo(s.stagedPng),target);File.Copy(Repo(s.stagedPng)+".meta",target+".meta");}
            foreach(var a in data.audio.Where(a=>a.stagedPath!=null))
            {string target=Project(AudioPath(a));if(File.Exists(target)) continue;Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Repo(a.stagedPath),target);File.Copy(Repo(a.stagedPath)+".meta",target+".meta");}
        }
        private static string ImagePath(State state)=>"Assets/Remains/Resources/"+state.resource+".png";
        private static string AudioPath(Audio audio)=>"Assets/Remains/Resources/"+audio.resource+audio.extension;
        private static AudioClip AudioClip(Audio audio)
        {var clip=Load<AudioClip>(AudioPath(audio));Require(clip.length>0 && clip.frequency>0 && clip.channels>0,"Неполный audio import: "+audio.id);return clip;}
        private static T Load<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Продолжение27: нет "+typeof(T).Name+": "+path);
        private static Sprite NativeSprite(State state)
        {
            string path=ImagePath(state);Require(Hash(File.ReadAllBytes(Project(path)))==state.sha256,"Изменён native PNG: "+path);
            var sprite=Load<Sprite>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;var settings=new TextureImporterSettings();importer?.ReadTextureSettings(settings);
            Require(importer!=null && AssetDatabase.AssetPathToGUID(path)==state.textureGuid && sprite.rect==new Rect(0,0,state.width,state.height) &&
                sprite.texture.width==state.width && sprite.texture.height==state.height && sprite.pixelsPerUnit==50 &&
                Vector2.Distance(sprite.pivot,new Vector2(state.pivotPixels[0],state.pivotPixels[1]))<.0001f &&
                importer.textureType==TextureImporterType.Sprite && importer.spriteImportMode==SpriteImportMode.Single && settings.spriteMeshType==SpriteMeshType.FullRect &&
                importer.npotScale==TextureImporterNPOTScale.None && !importer.mipmapEnabled && !importer.alphaIsTransparency && importer.textureCompression==TextureImporterCompression.Uncompressed &&
                importer.filterMode==FilterMode.Point && importer.wrapModeU==TextureWrapMode.Clamp && importer.wrapModeV==TextureWrapMode.Clamp &&
                importer.maxTextureSize>=Math.Max(state.width,state.height),"Нарушен native sampler/registration/GUID: "+path);
            Require(File.ReadAllText(Project(path)+".meta").Contains("spriteID: "+state.spriteId),"SpriteID изменён: "+path);return sprite;
        }
        private static void CheckAsset(Entry entry)
        {
            string id=entry.id;var prefab=Load<GameObject>(PrefabPath(id));var material=Load<Material>(MaterialPath(id));var renderer=prefab.GetComponent<SpriteRenderer>();
            Require(renderer!=null && renderer.sprite==NativeSprite(entry.states[0]) && renderer.sharedMaterial==material &&
                material.mainTexture==renderer.sprite.texture && material.shader==GameAssets.Load<Shader>("Presentation/WorldLit") &&
                prefab.transform.localPosition==Vector3.zero && prefab.transform.localRotation==Quaternion.identity && prefab.transform.localScale==Vector3.one &&
                prefab.GetComponentsInChildren<SpriteRenderer>(true).Length==1 && prefab.GetComponentsInChildren<Collider2D>(true).Length==0 && prefab.GetComponentsInChildren<Rigidbody2D>(true).Length==0,
                "Изменён prefab/material либо добавлена независимая физика: "+id);
            if(id=="platform1")
            {
                var asset=Load<DoorAsset>(AssetPath(id));asset.Validate();
                Require(prefab.GetComponent<DoorView>()?.Definition==asset && asset.Id==PlatformId && !asset.Interactive && asset.MaxHealth==5000 && asset.Size==new Vector2(2.4f,.8f) &&
                    asset.InteractionSize==asset.Size && asset.Closed==renderer.sprite && asset.Opened==NativeSprite(entry.states[1]) && asset.Destroyed==NativeSprite(entry.states[2]) &&
                    asset.OpenSound==null && asset.CloseSound==null && asset.DestroySound==AudioClip(ReadManifest().audio.Single(a=>a.id=="break_metal")),"Изменён stationary platform DoorAsset.");
            }
            else
            {
                var asset=Load<PropAsset>(AssetPath(id));var view=prefab.GetComponent<PropView>();var profile=Load<ContainerArtworkProfileAsset>(ProfilePath(id));profile.Validate(id);
                Require(StateJson.ToJson(asset.CreateDefinition())==StateJson.ToJson(Definition(id)) && asset.Prefab==view && view!=null && view.Artwork==renderer &&
                    view.ContainerArtwork==profile && view.VisualAnchor==(id=="cup"?PropVisualAnchor.BodyCenter:PropVisualAnchor.PlacementAnchor) && view.FitsStateWidth==(id=="cup") &&
                    profile.Mode==ContainerArtworkMode.ClosedOpened && profile.Closed==renderer.sprite && profile.Opened==NativeSprite(entry.states[1]),"Изменён PropAsset/профиль/anchor: "+id);
            }
        }
        public static DoorAsset PlatformAsset=>Load<DoorAsset>(AssetPath("platform1"));
        public static void Check()
        {var data=ReadManifest();foreach(var e in data.types) CheckAsset(e);foreach(var a in data.audio) AudioClip(a);
            Debug.Log("Продолжение27: 2 шкафа/SO +stationary platform/SO, 7 native bitmap состояний/registration. Source locks/thre800 и original runtime остаются неподтверждёнными.");}
        internal static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Графика продолжения27: "+reason);}
    }
}
