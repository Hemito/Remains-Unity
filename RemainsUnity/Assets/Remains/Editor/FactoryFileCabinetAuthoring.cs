using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit asset command. Placement, loot and catalog registration belong to their own authoring step.
    public static class FactoryFileCabinetAuthoring
    {
        public const string Id="filecab",Name="Картотечный шкаф";
        public const string Folder="Assets/Remains/RoomDrafts/Containers";
        public const string AssetPath=Folder+"/filecab.asset",PrefabPath=Folder+"/filecab.prefab",MaterialPath=Folder+"/filecab.mat";
        public const string Resource="Original/Room/FactoryObjects/filecab";
        public const string ImagePath="Assets/Remains/Resources/"+Resource+".png";
        public const string NativeImageHash="df2822e39e42d31359a284e3a54660abcabe8e342eb7562ef0796af13d983da4";
        public const float Width=.64f,Height=1.6f,MassKg=12,PixelsPerUnit=50;
        public const int ImageWidth=32,ImageHeight=80;
        public static PropDefinition CreateDefinition()=>new()
        {id=Id,name=Name,resource=Resource,width=Width,height=Height,mass=MassKg,movable=true,hasPhysicalBody=true};

        [MenuItem("Remains/Контент/Подготовить картотечный шкаф")]
        public static PropAsset Run()
        {
            AssetDatabase.Refresh();ValidateNativeImage();
            var paths=new[]{AssetPath,PrefabPath,MaterialPath};
            int existing=paths.Count(path=>File.Exists(Absolute(path)) || AssetDatabase.LoadMainAssetAtPath(path)!=null);
            if(existing!=0)
            {
                if(existing!=paths.Length) throw new InvalidOperationException("Неполный набор ассетов картотеки; существующие файлы не заменены: "+Folder);
                return ValidateArtifacts();
            }
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","Containers");
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath);
            var material=WorldArtworkMaterial.Create(sprite.texture);
            AssetDatabase.CreateAsset(material,MaterialPath);
            var asset=ScriptableObject.CreateInstance<PropAsset>();asset.name=Name;asset.SetDefinition(CreateDefinition());
            var root=new GameObject(Name);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                var view=root.AddComponent<PropView>();view.SetArtwork(renderer);
                asset.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath).GetComponent<PropView>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(asset,AssetPath);AssetDatabase.SaveAssets();
            var result=ValidateArtifacts();
            Debug.Log("Картотечный шкаф: исходный PNG 32×80 сохранён; тело 0,64×1,6 м, масса 12 кг — явный баланс Unity. Каталог, лут и расстановка устанавливаются отдельно.");
            return result;
        }
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
        public static void ValidateNativeImage()
        {
            string absolute=Absolute(ImagePath);
            if(!File.Exists(absolute)) throw new InvalidOperationException("Нет исходного PNG картотечного шкафа: "+ImagePath);
            using var sha=SHA256.Create();
            string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(absolute))).Replace("-","").ToLowerInvariant();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath);
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath);
            var importer=AssetImporter.GetAtPath(ImagePath) as TextureImporter;
            if(hash!=NativeImageHash || texture==null || texture.width!=ImageWidth || texture.height!=ImageHeight)
                throw new InvalidOperationException($"Изменён исходный PNG картотеки: {ImagePath}, sha256={hash}, size={texture?.width}×{texture?.height}.");
            var settings=new TextureImporterSettings();importer?.ReadTextureSettings(settings);
            if(sprite==null || sprite.pixelsPerUnit!=PixelsPerUnit || Vector2.Distance(sprite.pivot,new Vector2(16,40))>.0001f ||
                sprite.rect!=new Rect(0,0,ImageWidth,ImageHeight) || importer==null || importer.textureType!=TextureImporterType.Sprite ||
                importer.spriteImportMode!=SpriteImportMode.Single || settings.spriteMeshType!=SpriteMeshType.FullRect ||
                importer.textureCompression!=TextureImporterCompression.Uncompressed || importer.npotScale!=TextureImporterNPOTScale.None ||
                importer.mipmapEnabled || importer.alphaIsTransparency || importer.filterMode!=FilterMode.Point || importer.wrapMode!=TextureWrapMode.Clamp)
                throw new InvalidOperationException("Потерян явный импорт картотеки: native 32×80, FullRect, pivot=(16;40), 50 пикселей/м, Point/Clamp без сжатия, mipmaps и изменения RGBA. "+ImagePath);
        }
        public static PropAsset ValidateArtifacts()
        {
            ValidateNativeImage();
            var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(asset==null || prefab==null || material==null || asset.Prefab==null || asset.Prefab.gameObject!=prefab)
                throw new InvalidOperationException("Отсутствует согласованный asset/prefab/material картотеки: "+Folder);
            FactoryFileCabinetChecks.CheckDefinition(asset.CreateDefinition());
            var view=asset.Prefab;var renderer=view.Artwork;
            if(renderer==null || renderer.transform!=prefab.transform || view.transform!=prefab.transform || renderer.sprite!=AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath) || renderer.sharedMaterial!=material ||
                material.mainTexture!=asset.Texture || material.shader!=GameAssets.Load<Shader>("Presentation/WorldLit") ||
                view.VisualAnchor!=PropVisualAnchor.BodyCenter || !view.FitsStateWidth || renderer.sortingOrder!=ActorVisualLayers.RestingObjectOrder ||
                prefab.transform.localPosition!=Vector3.zero || prefab.transform.localRotation!=Quaternion.identity || prefab.transform.localScale!=Vector3.one ||
                prefab.GetComponentsInChildren<PropView>(true).Length!=1 || prefab.GetComponentsInChildren<SpriteRenderer>(true).Length!=1)
                throw new InvalidOperationException("Авторские настройки картотеки расходятся с контрактом; они не заменены: "+PrefabPath);
            return asset;
        }
    }
}
