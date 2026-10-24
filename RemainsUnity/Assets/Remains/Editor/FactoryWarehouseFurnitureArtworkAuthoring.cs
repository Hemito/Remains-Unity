using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit resource command; catalog, placement, inventories and audio services belong to their own installers.
    public static class FactoryWarehouseFurnitureArtworkAuthoring
    {
        public const string Folder="Assets/Remains/RoomDrafts/Containers";
        public const string ResourceFolder="Original/Containers/WarehouseFurniture";
        public const string ManifestPath="Tools/Research/FactoryWarehouseFurniture/manifest.json";
        public const string CouchId="couch",SafeId="safe",LockerId="locker";
        public const float PixelsPerUnit=50,CouchMass=25.6f,SafeMass=64.8f;
        private static readonly string[] ids={CouchId,SafeId,LockerId};
        [Serializable] private sealed class State
        {public string label,resource,sha256,textureGuid;public int width,height;public float pixelsPerUnit;public float[] pivotPixels;}
        [Serializable] private sealed class Policy
        {public float width,height,mass,previewOffsetY;public bool movable,hasPhysicalBody;public string visualAnchor,artworkMode;public float[] graphicSize;}
        [Serializable] private sealed class Entry
        {public string id,nameRu;public State[] states;public Policy unity;}
        [Serializable] private sealed class Audio
        {public string id,resource,sha256;}
        [Serializable] private sealed class Manifest
        {public int schema;public Entry[] types;public Audio[] audio;}

        private static void Known(string id)
        {Require(ids.Contains(id),"Нет явно подготовленного типа складской мебели: type="+id);}
        public static string AssetPath(string id) {Known(id);return Folder+"/"+id+".asset";}
        public static string PrefabPath(string id) {Known(id);return Folder+"/"+id+".prefab";}
        public static string MaterialPath(string id) {Known(id);return Folder+"/"+id+".mat";}
        public static string ProfilePath(string id) {Known(id);return ContainerArtworkAuthoring.ProfilePath(id);}
        public static string Resource(string id) {Known(id);return ResourceFolder+"/"+id+"-closed";}
        public static string OpenResource(string id)
        {Known(id);Require(id!=CouchId,"У дивана нет исходного open кадра.");return ResourceFolder+"/"+id+"-open";}
        public static PropDefinition Definition(string id)
        {Known(id);return Definition(ReadManifest().types.Single(entry=>entry.id==id));}
        private static PropDefinition Definition(Entry entry)=>new() {id=entry.id,name=entry.nameRu,resource=Resource(entry.id),
            width=entry.unity.width,height=entry.unity.height,mass=entry.unity.mass,
            movable=entry.unity.movable,hasPhysicalBody=entry.unity.hasPhysicalBody};
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
        private static string ImagePath(State state)=>"Assets/Remains/Resources/"+state.resource+".png";
        private static T Load<T>(string path) where T:UnityEngine.Object
            =>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Складская мебель: нет импортированного "+typeof(T).Name+": "+path);
        private static string Hash(byte[] data)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}

        [MenuItem("Remains/Контент/Подготовить графику складской мебели")]
        public static PropAsset[] Install()
        {
            var manifest=ReadManifest();AssetDatabase.Refresh();
            // Check the complete set before creating anything. Never repair a partial/manual asset set silently.
            foreach(var entry in manifest.types)
            {
                foreach(var state in entry.states)
                {AssetDatabase.ImportAsset(ImagePath(state),ImportAssetOptions.ForceSynchronousImport);NativeSprite(entry,state);}
                var paths=new[]{AssetPath(entry.id),PrefabPath(entry.id),MaterialPath(entry.id),ProfilePath(entry.id)};
                int count=paths.Count(path=>File.Exists(Absolute(path)) || AssetDatabase.LoadMainAssetAtPath(path)!=null);
                Require(count==0 || count==paths.Length,$"Неполный набор SO/prefab/material/profile: type={entry.id}, found={count}/{paths.Length}; файлы/GUID не заменены.");
                if(count==0) Require(!paths.Any(path=>File.Exists(Absolute(path)+".meta")),"Найдена metadata без ассета; GUID не заменён: "+entry.id);
                else CheckAsset(entry);
            }
            CheckAudio(manifest,true);
            Require(AssetDatabase.IsValidFolder(Folder) && AssetDatabase.IsValidFolder(ContainerArtworkAuthoring.Folder),"Нет сохранённых общих папок контейнеров/профилей.");
            foreach(var entry in manifest.types)
            {
                if(AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id))!=null) continue;
                var closed=NativeSprite(entry,entry.states[0]);
                var opened=entry.states.Length==2?NativeSprite(entry,entry.states[1]):null;
                var profile=ScriptableObject.CreateInstance<ContainerArtworkProfileAsset>();profile.name=entry.id+"-artwork";
                profile.Configure(entry.id,opened!=null?ContainerArtworkMode.ClosedOpened:ContainerArtworkMode.Static,closed,opened);
                AssetDatabase.CreateAsset(profile,ProfilePath(entry.id));
                var material=WorldArtworkMaterial.Create(closed.texture);material.name=entry.id;
                AssetDatabase.CreateAsset(material,MaterialPath(entry.id));
                var asset=ScriptableObject.CreateInstance<PropAsset>();asset.name=entry.nameRu;asset.SetDefinition(Definition(entry));
                var root=new GameObject(entry.nameRu);
                try
                {
                    var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=closed;renderer.sharedMaterial=material;
                    renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                    var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.ConfigureContainerArtwork(profile);
                    if(!entry.unity.hasPhysicalBody) view.ConfigurePlacementArtwork();
                    var prefab=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(entry.id));
                    Require(prefab!=null,"Unity не сохранил prefab: "+entry.id);asset.SetPrefab(prefab.GetComponent<PropView>());
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
                AssetDatabase.CreateAsset(asset,AssetPath(entry.id));
            }
            AssetDatabase.SaveAssets();Check();
            return manifest.types.Select(entry=>Load<PropAsset>(AssetPath(entry.id))).ToArray();
        }

        public static void Check()
        {
            var manifest=ReadManifest();
            foreach(var entry in manifest.types) {foreach(var state in entry.states) NativeSprite(entry,state);CheckAsset(entry);}
            CheckAudio(manifest,false);
            Debug.Log("Складская мебель: PASS — 3 определения, 5 byte-identical native кадров/registration, 2 close/open профиля и явный static диван, 2 исходных MP3. Диван body1.6×.4/art1.6×.78, сейф1.2×1.2, шкаф NoBody1.6×2.38; каталог/расстановка/замки/мины устанавливаются отдельно.");
        }
        private static Manifest ReadManifest()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",ManifestPath));
            Require(File.Exists(path),"Отсутствует source manifest: "+path);
            var data=StateJson.FromJson<Manifest>(File.ReadAllText(path));
            Require(data?.schema==1 && data.types?.Length==3 && data.types.All(entry=>entry!=null) &&
                data.types.Select(entry=>entry.id).SequenceEqual(ids),"Изменён состав resource manifest: "+path);
            foreach(var e in data.types)
            {
                int width=e.id==SafeId?60:80,height=e.id==CouchId?39:e.id==SafeId?60:119;
                var pivot=e.id==CouchId?new Vector2(40,10):e.id==SafeId?new Vector2(30,30):new Vector2(40,0);
                float bodyWidth=e.id==SafeId?1.2f:1.6f,bodyHeight=e.id==CouchId?.4f:e.id==SafeId?1.2f:2.38f;
                float mass=e.id==CouchId?CouchMass:e.id==SafeId?SafeMass:0;bool body=e.id!=LockerId;
                // Compare serialized binary32 values to stored binary32 values. Mono may retain an
                // unassigned width/PPU division at wider intermediate precision; exact float data is still required.
                var graphic=e.id==CouchId?new Vector2(1.6f,.78f):e.id==SafeId?new Vector2(1.2f,1.2f):new Vector2(1.6f,2.38f);
                Require(e.nameRu==(e.id==CouchId?"Диван":e.id==SafeId?"Сейф":"Металлический шкаф") && e.unity!=null,
                    "Изменено имя/отсутствует Unity policy: type="+e.id+", name="+e.nameRu);
                var u=e.unity;
                Require(u.width==bodyWidth && u.height==bodyHeight && u.mass==mass && u.movable==body && u.hasPhysicalBody==body,
                    $"Изменён body contract: type={e.id}, size={u.width:R}×{u.height:R}/{bodyWidth:R}×{bodyHeight:R}, mass={u.mass:R}/{mass:R}, movable={u.movable}/{body}, body={u.hasPhysicalBody}/{body}.");
                Require(u.visualAnchor==(body?"BodyCenter":"PlacementAnchor") && u.previewOffsetY==(body?bodyHeight/2:0),
                    $"Изменена registration: type={e.id}, anchor={u.visualAnchor}, previewY={u.previewOffsetY:R}, expectedY={(body?bodyHeight/2:0):R}.");
                Require(u.graphicSize?.Length==2,"Отсутствует двумерный graphicSize: type="+e.id);
                Require(u.graphicSize[0]==graphic.x && u.graphicSize[1]==graphic.y,
                    $"Изменён native graphic contract: type={e.id}, actual={u.graphicSize[0]:R}×{u.graphicSize[1]:R}, expected={graphic.x:R}×{graphic.y:R}, native={width}×{height}/PPU{PixelsPerUnit}.");
                Require(u.artworkMode==(e.id==CouchId?"Static":"ClosedOpened"),"Изменён явный профиль состояний: type="+e.id+", mode="+u.artworkMode);
                Require(e.states?.Length==(e.id==CouchId?1:2) && e.states.All(state=>state!=null),"Неполный набор исходных состояний: "+e.id);
                for(int i=0;i<e.states.Length;i++)
                {
                    var s=e.states[i];string label=i==0?"closed":"open";
                    Require(s.label==label && s.resource==ResourceFolder+"/"+e.id+"-"+label && s.width==width && s.height==height && s.pixelsPerUnit==PixelsPerUnit &&
                        s.pivotPixels?.Length==2 && s.pivotPixels[0]==pivot.x && s.pivotPixels[1]==pivot.y && s.sha256==ExpectedHash(e.id,label) && s.textureGuid?.Length==32,
                        "Изменены native bytes/размер/pivot: type="+e.id+", state="+label);
                }
            }
            return data;
        }
        private static string ExpectedHash(string id,string state)=>(id,state) switch
        {
            (CouchId,"closed")=>"c1d0d965ff1101170d163b9daaf0162c919b632176b1b2b86095dd32396ce1d7",
            (SafeId,"closed")=>"c795f00519dc3b6dd10de025c9054307426c2f24026fb032006223119a666c5a",
            (SafeId,"open")=>"f1b471f507ce63cf973969d2dd5091a6fa9e88c8e6e15591acfdf91e5a44aaaa",
            (LockerId,"closed")=>"ac78d1f90e17b14ce2042d8ac8a4314b82b4a1b210241b2c06ff510d114e167c",
            (LockerId,"open")=>"ac7f2a3b2b700afc15195531175a4788eeef988c6fe22cb0776f322d98e851c8",
            _=>throw new InvalidOperationException("Нет исходного PNG checksum: "+id+"/"+state)
        };
        private static Sprite NativeSprite(Entry entry,State state)
        {
            string path=ImagePath(state);Require(File.Exists(Absolute(path)),"Отсутствует PNG: "+path);
            Require(Hash(File.ReadAllBytes(Absolute(path)))==state.sha256,"Изменены исходные PNG bytes: "+path);
            var sprite=Load<Sprite>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            var settings=new TextureImporterSettings();importer?.ReadTextureSettings(settings);
            Require(AssetDatabase.AssetPathToGUID(path)==state.textureGuid && sprite.texture.width==state.width && sprite.texture.height==state.height &&
                sprite.rect==new Rect(0,0,state.width,state.height) && sprite.pixelsPerUnit==PixelsPerUnit &&
                Vector2.Distance(sprite.pivot,new Vector2(state.pivotPixels[0],state.pivotPixels[1]))<.0001f && importer!=null &&
                importer.textureType==TextureImporterType.Sprite && importer.spriteImportMode==SpriteImportMode.Single && settings.spriteMeshType==SpriteMeshType.FullRect &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && importer.npotScale==TextureImporterNPOTScale.None && !importer.mipmapEnabled &&
                !importer.alphaIsTransparency && importer.maxTextureSize>=Math.Max(state.width,state.height) && importer.filterMode==FilterMode.Point &&
                importer.wrapModeU==TextureWrapMode.Clamp && importer.wrapModeV==TextureWrapMode.Clamp,"Native Sprite/GUID/PPU/pivot/import расходятся: "+path);
            // Compare 2D registration. Unity renderer culling can legitimately pad Z extents.
            var min=new Vector2(-state.pivotPixels[0]/PixelsPerUnit,-state.pivotPixels[1]/PixelsPerUnit);
            Require(Mathf.Abs(sprite.bounds.min.x-min.x)<.00001f && Mathf.Abs(sprite.bounds.min.y-min.y)<.00001f &&
                Mathf.Abs(sprite.bounds.size.x-state.width/PixelsPerUnit)<.00001f && Mathf.Abs(sprite.bounds.size.y-state.height/PixelsPerUnit)<.00001f &&
                Mathf.Abs(min.y+entry.unity.previewOffsetY)<.00001f,"Нарушена source bottom registration/body-center preview: "+path);
            return sprite;
        }
        private static void CheckAsset(Entry entry)
        {
            var asset=Load<PropAsset>(AssetPath(entry.id));var prefab=Load<GameObject>(PrefabPath(entry.id));
            var material=Load<Material>(MaterialPath(entry.id));var profile=Load<ContainerArtworkProfileAsset>(ProfilePath(entry.id));
            var actual=asset.CreateDefinition();var expected=Definition(entry);PropBodyRules.Validate(actual.movable,actual.hasPhysicalBody,actual.mass,"складская мебель="+entry.id);
            Require(StateJson.ToJson(actual)==StateJson.ToJson(expected),"Сохранённое определение изменено; замена запрещена: "+entry.id);
            profile.Validate(entry.id);var closed=NativeSprite(entry,entry.states[0]);var opened=entry.states.Length==2?NativeSprite(entry,entry.states[1]):null;
            Require(profile.Closed==closed && profile.Opened==opened && profile.Mode==(opened!=null?ContainerArtworkMode.ClosedOpened:ContainerArtworkMode.Static) &&
                profile.Select(false)==closed && profile.Select(true)==(opened??closed),"Профиль расходится с actual source frames: "+entry.id);
            var view=asset.Prefab;var renderer=view?.Artwork;
            Require(view!=null && view.gameObject==prefab && renderer!=null && renderer.transform==prefab.transform && view.ContainerArtwork==profile &&
                renderer.sprite==closed && renderer.sharedMaterial==material && material.mainTexture==closed.texture && material.shader==GameAssets.Load<Shader>("Presentation/WorldLit") &&
                view.VisualAnchor==(entry.unity.hasPhysicalBody?PropVisualAnchor.BodyCenter:PropVisualAnchor.PlacementAnchor) && view.FitsStateWidth==entry.unity.hasPhysicalBody &&
                renderer.sortingOrder==ActorVisualLayers.RestingObjectOrder && prefab.transform.localPosition==Vector3.zero && prefab.transform.localRotation==Quaternion.identity &&
                prefab.transform.localScale==Vector3.one && prefab.GetComponentsInChildren<PropView>(true).Length==1 && prefab.GetComponentsInChildren<SpriteRenderer>(true).Length==1 &&
                prefab.GetComponentsInChildren<Collider2D>(true).Length==0 && prefab.GetComponentsInChildren<Rigidbody2D>(true).Length==0,
                "Prefab/material/anchor изменены или добавлена независимая физика; файлы не заменены: "+entry.id);
        }
        private static void CheckAudio(Manifest manifest,bool import)
        {
            Require(manifest.audio?.Length==4 && manifest.audio.All(a=>a!=null) &&
                manifest.audio.Select(a=>a.id).SequenceEqual(new[]{"fall_wood_big","fall_metal_safe","safe_open","metal_open"}),"Неполный/неоднозначный audio provenance manifest.");
            foreach(var a in manifest.audio)
            {
                string resource=a.id switch {"fall_wood_big"=>"Original/Audio/crate-wood-fall","fall_metal_safe"=>"Original/Audio/crate-metal-safe-fall",
                    "safe_open"=>"Original/Audio/container-safe-open","metal_open"=>"Original/Audio/container-metal-open",_=>throw new InvalidOperationException("Неизвестный source audio ID: "+a.id)};
                string path="Assets/Remains/Resources/"+resource+(a.id.EndsWith("_open",StringComparison.Ordinal)?".mp3":".wav");
                Require(a.resource==resource && File.Exists(Absolute(path)) && Hash(File.ReadAllBytes(Absolute(path)))==a.sha256,"Source audio bytes изменены: "+a.id+", path="+path);
                if(import) AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var clip=Load<AudioClip>(path);Require(clip.length>0 && clip.channels>0 && clip.frequency>0,"Неполный native audio import: "+a.id);
            }
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Складская мебель: "+reason);}
    }
}
