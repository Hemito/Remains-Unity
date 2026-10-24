using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit asset command. Placement, inventories, catalog and patch chains are installed separately.
    public static class FactoryOfficeFurnitureArtworkAuthoring
    {
        public const string Folder="Assets/Remains/RoomDrafts/Containers";
        public const string ResourceFolder="Original/Containers/OfficeFurniture";
        public const string TableId="table",Table1Id="table1",TrashId="trash",BookcaseId="bookcase";
        public const string TableAssetPath=Folder+"/table.asset",Table1AssetPath=Folder+"/table1.asset",
            TrashAssetPath=Folder+"/trash.asset",BookcaseAssetPath=Folder+"/bookcase.asset";
        public const float PixelsPerUnit=50,TableMass=9.92f,Table1Mass=10.72f,TrashMass=4,BookcaseMass=0;

        private sealed class Artwork
        {
            public readonly string id,title,closedHash,openHash;
            public readonly int width,height,openHeight;
            public readonly float pivotX,pivotY,mass;
            public readonly bool body;
            public Artwork(string id,string title,int width,int height,float pivotX,float pivotY,float mass,bool body,string closedHash,
                int openHeight=0,string openHash=null)
            {
                this.id=id;this.title=title;this.width=width;this.height=height;this.pivotX=pivotX;this.pivotY=pivotY;
                this.mass=mass;this.body=body;this.closedHash=closedHash;this.openHeight=openHeight;this.openHash=openHash;
            }
            public bool HasOpen=>openHash!=null;
            public PropDefinition Definition()=>new() {id=id,name=title,resource=Resource(id),width=width/PixelsPerUnit,
                height=height/PixelsPerUnit,mass=mass,movable=body,hasPhysicalBody=body};
        }
        // Fixed independent Unity content values, informed by resource/code-derived measurements.
        // No original sizing, mass, physics, loot or interaction algorithms are implemented here.
        private static readonly Artwork[] artwork=
        {
            new(TableId,"Письменный стол",62,40,31,20,TableMass,true,"14d16dfa108ecbb90bb8f14d77ec59b964bcb22e54639296e1816c8b1678cf69"),
            new(Table1Id,"Деревянный стол",67,40,34,20,Table1Mass,true,"13ab91a550353f29aeb2f29fc4e19403599a93d73711ec14b0b2f221af4bf1ba"),
            new(TrashId,"Мусорка",25,40,12,20,TrashMass,true,"147d46b0cf4657799f0fc6f26cea3fd860c3c2725bb8ffb3e75bc2b279e342e2",
                44,"43bb27da28ebda4fdec145d3868f1ad136743c605ca933ba5be6588e8fd4f30e"),
            new(BookcaseId,"Книжный шкаф",80,120,40,0,BookcaseMass,false,"c7646253a402d2100352498ba0046c458c5d47be7dc0b0d321a0977c1b77909f"),
        };
        private static Artwork Find(string id)=>artwork.SingleOrDefault(value=>value.id==id)??
            throw new InvalidOperationException("Нет явно подготовленного типа офисной мебели: type="+id);
        public static string AssetPath(string id) {Find(id);return Folder+"/"+id+".asset";}
        public static string Resource(string id) {Find(id);return ResourceFolder+"/"+id+"-closed";}
        public static PropDefinition Definition(string id)=>Find(id).Definition();
        private static string PrefabPath(Artwork entry)=>Folder+"/"+entry.id+".prefab";
        private static string MaterialPath(Artwork entry)=>Folder+"/"+entry.id+".mat";
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
        private static void CheckImages(Artwork entry) {CheckImage(entry,false);if(entry.HasOpen) CheckImage(entry,true);}

        [MenuItem("Remains/Контент/Подготовить графику офисной мебели")]
        public static PropAsset[] Install()
        {
            AssetDatabase.Refresh();
            // Validate every existing asset before creating anything. Never replace a different authored set.
            foreach(var entry in artwork)
            {
                CheckImages(entry);
                string[] paths={AssetPath(entry.id),PrefabPath(entry),MaterialPath(entry)};
                int existing=paths.Count(path=>File.Exists(Absolute(path)) || AssetDatabase.LoadMainAssetAtPath(path)!=null);
                if(existing!=0 && existing!=3)
                    throw new InvalidOperationException($"Неполный набор офисной мебели: type={entry.id}; файлы не заменены.");
                if(existing==0 && paths.Any(path=>File.Exists(Absolute(path)+".meta")))
                    throw new InvalidOperationException($"Офисная мебель: найдена отдельная metadata без ассета, type={entry.id}; GUID не заменён.");
                if(existing==3) CheckAsset(entry);
            }
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","Containers");
            foreach(var entry in artwork)
            {
                if(AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id))!=null) continue;
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/"+Resource(entry.id)+".png");
                var material=WorldArtworkMaterial.Create(sprite.texture);
                AssetDatabase.CreateAsset(material,MaterialPath(entry));
                var asset=ScriptableObject.CreateInstance<PropAsset>();asset.name=entry.title;asset.SetDefinition(entry.Definition());
                var root=new GameObject(entry.title);
                try
                {
                    var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                    renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                    var view=root.AddComponent<PropView>();view.SetArtwork(renderer);
                    if(!entry.body) view.ConfigurePlacementArtwork();
                    asset.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(entry)).GetComponent<PropView>());
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
                AssetDatabase.CreateAsset(asset,AssetPath(entry.id));
            }
            AssetDatabase.SaveAssets();Check();
            Debug.Log("Офисная мебель: четыре определения с исходной родной графикой; прежние ассеты/GUID сохранены. Расстановка, взаимодействие, звук и лут устанавливаются отдельно.");
            return artwork.Select(entry=>AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id))).ToArray();
        }
        public static void Check()
        {
            foreach(var entry in artwork) {CheckImages(entry);CheckAsset(entry);}
            Debug.Log("Офисная мебель: PASS — пять native PNG, исходные registration/clip, явный импорт, определения/prefab/material; книжный шкаф закреплён без физической опоры.");
        }
        private static void CheckImage(Artwork entry,bool open)
        {
            string path="Assets/Remains/Resources/"+ResourceFolder+"/"+entry.id+(open?"-open":"-closed")+".png";
            string file=Absolute(path);
            if(!File.Exists(file)) throw new InvalidOperationException("Нет исходной графики офисной мебели: "+path);
            using var sha=SHA256.Create();
            string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();
            int height=open?entry.openHeight:entry.height;
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;var settings=new TextureImporterSettings();
            importer?.ReadTextureSettings(settings);
            if(hash!=(open?entry.openHash:entry.closedHash) || texture==null || texture.width!=entry.width || texture.height!=height)
                throw new InvalidOperationException($"Изменён исходный PNG офисной мебели: type={entry.id}, path={path}, hash={hash}, size={texture?.width}×{texture?.height}.");
            if(sprite==null || sprite.rect!=new Rect(0,0,entry.width,height) || sprite.pixelsPerUnit!=PixelsPerUnit ||
                Vector2.Distance(sprite.pivot,new Vector2(entry.pivotX,entry.pivotY))>.0001f || importer==null ||
                importer.textureType!=TextureImporterType.Sprite || importer.spriteImportMode!=SpriteImportMode.Single ||
                settings.spriteMeshType!=SpriteMeshType.FullRect || importer.textureCompression!=TextureImporterCompression.Uncompressed ||
                importer.maxTextureSize<Math.Max(entry.width,height) || importer.npotScale!=TextureImporterNPOTScale.None ||
                importer.mipmapEnabled || importer.alphaIsTransparency || importer.filterMode!=FilterMode.Point || importer.wrapMode!=TextureWrapMode.Clamp)
                throw new InvalidOperationException($"Потерян явный импорт офисной мебели: type={entry.id}, path={path}; нужны native clip/FullRect, source X registration, 50 PPU, Point/Clamp без изменения RGBA.");
        }
        private static void CheckAsset(Artwork entry)
        {
            var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id));
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(entry));
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(entry));
            if(asset==null || prefab==null || material==null || asset.Prefab==null || asset.Prefab.gameObject!=prefab)
                throw new InvalidOperationException("Нет согласованного набора asset/prefab/material офисной мебели: "+entry.id);
            var actual=asset.CreateDefinition();var expected=entry.Definition();
            PropBodyRules.Validate(actual.movable,actual.hasPhysicalBody,actual.mass,"офисная мебель="+entry.id);
            if(actual.id!=expected.id || actual.name!=expected.name || actual.resource!=expected.resource || actual.width!=expected.width ||
                actual.height!=expected.height || actual.mass!=expected.mass || actual.movable!=expected.movable || actual.hasPhysicalBody!=expected.hasPhysicalBody)
                throw new InvalidOperationException("Определение офисной мебели изменено; оно не заменено: "+AssetPath(entry.id));
            var view=asset.Prefab;var renderer=view.Artwork;
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/"+Resource(entry.id)+".png");
            var anchor=entry.body?PropVisualAnchor.BodyCenter:PropVisualAnchor.PlacementAnchor;
            if(renderer==null || renderer.transform!=prefab.transform || view.transform!=prefab.transform || renderer.sprite!=sprite || renderer.sharedMaterial!=material ||
                material.mainTexture!=asset.Texture || material.shader!=GameAssets.Load<Shader>("Presentation/WorldLit") || view.VisualAnchor!=anchor ||
                view.FitsStateWidth!=entry.body || renderer.sortingOrder!=ActorVisualLayers.RestingObjectOrder ||
                prefab.transform.localPosition!=Vector3.zero || prefab.transform.localRotation!=Quaternion.identity || prefab.transform.localScale!=Vector3.one ||
                prefab.GetComponentsInChildren<PropView>(true).Length!=1 || prefab.GetComponentsInChildren<SpriteRenderer>(true).Length!=1)
                throw new InvalidOperationException("Настройки prefab офисной мебели расходятся с контрактом; они не заменены: "+PrefabPath(entry));
            float minX=-entry.pivotX/PixelsPerUnit,minY=-entry.pivotY/PixelsPerUnit;
            if(Mathf.Abs(sprite.bounds.min.x-minX)>.00001f || Mathf.Abs(sprite.bounds.min.y-minY)>.00001f ||
                Mathf.Abs(sprite.bounds.size.x-actual.width)>.00001f || Mathf.Abs(sprite.bounds.size.y-actual.height)>.00001f)
                throw new InvalidOperationException("Фактические bounds спрайта не соответствуют исходной registration офисной мебели: "+entry.id);
        }
    }
}
