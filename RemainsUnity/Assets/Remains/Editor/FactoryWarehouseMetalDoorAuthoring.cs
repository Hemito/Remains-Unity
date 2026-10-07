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
    // Only the new native profile. Source placements and additive content versions are installed separately.
    public static class FactoryWarehouseMetalDoorAuthoring
    {
        public const string DefinitionId="warehouse-metal-door",SurfaceTexture="door:metal";
        public const string Folder="Assets/Remains/Resources/Presentation/Doors";
        public const string AssetPath=Folder+"/"+DefinitionId+".asset";
        public const string PrefabPath=Folder+"/"+DefinitionId+".prefab";
        public const string MaterialPath=Folder+"/"+DefinitionId+".mat";
        public const string ManifestPath="Tools/Research/FactoryMetalDoors/manifest.json";
        private const string Name="Металлическая дверь склада";
        [Serializable] internal sealed class State
        {public string label,resource,sha256;public int width,height;public float pixelsPerUnit;public float[] pivotPixels;}
        [Serializable] internal sealed class Policy
        {public float[] size,interactionSize;public float maxHealth;public bool interactive;}
        [Serializable] internal sealed class Sounds {public string open,close,die,manifest;}
        [Serializable] internal sealed class Manifest
        {public int schema;public string definitionId;public State[] states;public Policy unity;public Sounds sounds;}

        [MenuItem("Remains/Контент/Подготовить металлическую дверь склада")]
        public static DoorAsset Prepare()
        {
            var manifest=ReadManifest();var sounds=ReadSounds(manifest);
            FactoryOfficeOpeningsAuthoring.ValidateSoundFiles(sounds,Path.GetDirectoryName(Application.dataPath));
            foreach(var state in manifest.states) AssetDatabase.ImportAsset(ImagePath(state),ImportAssetOptions.ForceSynchronousImport);
            foreach(string id in SoundIds(manifest)) AssetDatabase.ImportAsset(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,id),ImportAssetOptions.ForceSynchronousImport);
            var closed=NativeSprite(manifest,"close");var opened=NativeSprite(manifest,"open");var destroyed=NativeSprite(manifest,"die");
            var audio=SoundIds(manifest).Select(id=>Load<AudioClip>(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,id))).ToArray();
            var paths=new[]{AssetPath,PrefabPath,MaterialPath};
            int existing=paths.Count(p=>File.Exists(Absolute(p)) || AssetDatabase.LoadMainAssetAtPath(p)!=null);
            if(existing!=0)
            {
                Require(existing==paths.Length,"Неполный набор SO/material/prefab; авторские файлы не заменены: "+DefinitionId);
                return Verify();
            }
            Require(AssetDatabase.IsValidFolder(Folder),"Нет общей папки дверей: "+Folder);
            var asset=ScriptableObject.CreateInstance<DoorAsset>();asset.name=Name;
            asset.ConfigureBarrier(DefinitionId,new Vector2(manifest.unity.size[0],manifest.unity.size[1]),closed,opened,destroyed,
                audio[0],audio[1],audio[2],manifest.unity.interactive,manifest.unity.maxHealth,
                new Vector2(manifest.unity.interactionSize[0],manifest.unity.interactionSize[1]));
            var material=WorldArtworkMaterial.Create(closed.texture);material.name=DefinitionId;
            AssetDatabase.CreateAsset(asset,AssetPath);AssetDatabase.CreateAsset(material,MaterialPath);
            var root=new GameObject(Name);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                root.AddComponent<DoorView>().Configure(asset,renderer);
                Require(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)!=null,"Unity не сохранил новый prefab: "+PrefabPath);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.SaveAssetIfDirty(asset);AssetDatabase.SaveAssetIfDirty(material);
            var result=Verify();
            Debug.Log("Металлическая дверь склада: отдельный профиль warehouse-metal-door, 3 исходных кадра40×80/50PPU, HP1000 и звуки. Legacy door2 не заменена; source threshold100 и замки не реализованы. Размещения64/71 устанавливаются отдельно.");
            return result;
        }

        public static DoorAsset Verify()
        {
            var manifest=ReadManifest();var sounds=ReadSounds(manifest);
            FactoryOfficeOpeningsAuthoring.ValidateSoundFiles(sounds,Path.GetDirectoryName(Application.dataPath));
            var asset=Load<DoorAsset>(AssetPath);asset.Validate();
            Require(asset.Id==DefinitionId && asset.CapabilityVersion==1 && asset.Interactive && asset.MaxHealth==1000 &&
                asset.Size==new Vector2(.8f,1.6f) && asset.InteractionSize==asset.Size &&
                asset.Closed==NativeSprite(manifest,"close") && asset.Opened==NativeSprite(manifest,"open") && asset.Destroyed==NativeSprite(manifest,"die"),
                "Сохранённый SO не совпадает с manifest/native кадрами; замена запрещена: "+AssetPath);
            var clips=new[]{asset.OpenSound,asset.CloseSound,asset.DestroySound};var ids=SoundIds(manifest);
            for(int i=0;i<ids.Length;i++)
                Require(clips[i]==Load<AudioClip>(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,ids[i])),"Изменён audio binding: "+ids[i]);
            var material=Load<Material>(MaterialPath);var prefab=Load<GameObject>(PrefabPath);var view=prefab.GetComponent<DoorView>();
            Require(material.mainTexture==asset.Closed.texture && material.shader==Resources.Load<Shader>("Presentation/WorldLit"),"Изменён общий lit material: "+MaterialPath);
            Require(view!=null && view.Definition==asset && view.Artwork!=null && view.Artwork.transform==prefab.transform &&
                view.Artwork.sprite==asset.Closed && view.Artwork.sharedMaterial==material && view.Artwork.sortingOrder==ActorVisualLayers.RestingObjectOrder &&
                prefab.transform.localPosition==Vector3.zero && prefab.transform.localRotation==Quaternion.identity && prefab.transform.localScale==Vector3.one &&
                prefab.GetComponentsInChildren<DoorView>(true).Length==1 && prefab.GetComponentsInChildren<SpriteRenderer>(true).Length==1 &&
                prefab.GetComponentsInChildren<Collider2D>(true).Length==0 && prefab.GetComponentsInChildren<Rigidbody2D>(true).Length==0 &&
                prefab.GetComponentsInChildren<RoomPhysicalPlacement>(true).Length==0 && prefab.GetComponentsInChildren<RoomDoorAuthoring>(true).Length==0,
                "Изменена регистрация prefab либо добавлена независимая физика/TK/placement: "+PrefabPath);
            var legacy=Load<DoorAsset>(Folder+"/door2.asset");legacy.Validate();
            Require(legacy.Id=="door2" && legacy.CapabilityVersion==0 && legacy.MaxHealth==0 && legacy.Destroyed==null,
                "Прежняя door2 получила новую прочность/разрушение вместо отдельного профиля.");
            return asset;
        }
        internal static Manifest ReadManifest()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",ManifestPath));
            Require(File.Exists(path),"Нет explicit resource manifest: "+path);
            var data=StateJson.FromJson<Manifest>(File.ReadAllText(path));
            Require(data?.schema==1 && data.definitionId==DefinitionId && data.states?.Length==3 && data.states.All(s=>s!=null) &&
                data.states.Select(s=>s.label).OrderBy(s=>s,StringComparer.Ordinal).SequenceEqual(new[]{"close","die","open"}) &&
                data.unity!=null && data.unity.size?.Length==2 && data.unity.interactionSize?.Length==2 && data.unity.interactive && data.unity.maxHealth==1000 &&
                data.unity.size.SequenceEqual(new[]{.8f,1.6f}) && data.unity.interactionSize.SequenceEqual(data.unity.size) &&
                data.sounds!=null && data.sounds.open=="metal_door_open" && data.sounds.close=="metal_door_close" && data.sounds.die=="break_metal" &&
                data.sounds.manifest=="Tools/Research/OfficeOpeningsResources/manifest.json","Неполный/изменённый native profile contract: "+path);
            foreach(var s in data.states)
                Require(s.resource=="Original/Doors/WarehouseMetal/door2-"+s.label && s.width==40 && s.height==80 && s.pixelsPerUnit==50 &&
                    s.pivotPixels?.Length==2 && s.pivotPixels[0]==20 && s.pivotPixels[1]==0 &&
                    s.sha256?.Length==64 && s.sha256.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"Изменена native регистрация кадра: "+s.label);
            return data;
        }
        internal static Sprite NativeSprite(Manifest manifest,string label)
        {
            var state=manifest.states.Single(s=>s.label==label);string path=ImagePath(state);byte[] bytes=File.ReadAllBytes(Absolute(path));
            using var sha=SHA256.Create();string actual=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            Require(actual==state.sha256,"Исходный PNG изменён: "+path+", sha256="+actual+", expected="+state.sha256);
            var sprite=Load<Sprite>(path);var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            var settings=new TextureImporterSettings();importer?.ReadTextureSettings(settings);
            Require(sprite.texture.width==state.width && sprite.texture.height==state.height && sprite.rect==new Rect(0,0,state.width,state.height) &&
                sprite.pixelsPerUnit==state.pixelsPerUnit && sprite.pivot==new Vector2(state.pivotPixels[0],state.pivotPixels[1]) && importer!=null &&
                importer.textureType==TextureImporterType.Sprite && importer.spriteImportMode==SpriteImportMode.Single && settings.spriteMeshType==SpriteMeshType.FullRect &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && importer.npotScale==TextureImporterNPOTScale.None && !importer.mipmapEnabled &&
                !importer.alphaIsTransparency && importer.maxTextureSize>=80 && importer.filterMode==FilterMode.Point &&
                importer.wrapModeU==TextureWrapMode.Clamp && importer.wrapModeV==TextureWrapMode.Clamp,"Native Sprite/PPU/pivot/import отличаются: "+path);
            return sprite;
        }
        private static FactoryOfficeOpeningsAuthoring.ResourcesManifest ReadSounds(Manifest manifest)
            =>FactoryOfficeOpeningsAuthoring.ReadResources(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Application.dataPath,"../..",manifest.sounds.manifest))));
        private static string[] SoundIds(Manifest manifest)=>new[]{manifest.sounds.open,manifest.sounds.close,manifest.sounds.die};
        private static string ImagePath(State state)=>"Assets/Remains/Resources/"+state.resource+".png";
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
        private static T Load<T>(string path) where T:UnityEngine.Object
            =>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Металлическая дверь склада: нет импортированного "+typeof(T).Name+": "+path);
        internal static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Металлическая дверь склада: "+reason);}
    }
}
