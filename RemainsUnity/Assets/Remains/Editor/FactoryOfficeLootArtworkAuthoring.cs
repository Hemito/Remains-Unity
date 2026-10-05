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
    // Explicit artwork command; no placement, inventory, catalog or content-patch installation.
    public static class FactoryOfficeLootArtworkAuthoring
    {
        public const string Folder="Assets/Remains/RoomDrafts/Containers";
        public const string ResourceFolder="Original/Containers/OfficeLoot";
        public const string ExplboxId="explbox",WeapboxId="weapbox",MedboxId="medbox",Instr1Id="instr1";
        public const string ExplboxAssetPath=Folder+"/explbox.asset",WeapboxAssetPath=Folder+"/weapbox.asset",
            MedboxAssetPath=Folder+"/medbox.asset",Instr1AssetPath=Folder+"/instr1.asset";
        public const float PixelsPerUnit=50;
        private sealed class Artwork
        {
            public readonly string id,title,closedHash,openHash;
            public readonly int width,height,openHeight;
            public readonly float pivotX,pivotY,mass;
            public readonly bool body;
            public Artwork(string id,string title,int width,int height,int openHeight,float pivotX,float pivotY,float mass,bool body,string closedHash,string openHash)
            {this.id=id;this.title=title;this.width=width;this.height=height;this.openHeight=openHeight;this.pivotX=pivotX;this.pivotY=pivotY;this.mass=mass;this.body=body;this.closedHash=closedHash;this.openHash=openHash;}
            public PropDefinition Definition()=>new() {id=id,name=title,resource=Resource(id),width=width/PixelsPerUnit,
                height=height/PixelsPerUnit,mass=mass,movable=body,hasPhysicalBody=body};
        }
        private static readonly Artwork[] artwork=
        {
            new("explbox","Ящик с взрывчаткой",38,30,33,19.0f,15.0f,6.84f,true,"0280f74e4d9ce4a3c1677c046cf9ebdb9249048f51190b3d27ca18ca9a0b2520","edd4fdd772d49121fe385dfe2e84cce7f318a3d6aa287b2f2c7b4a3e1fc7f3f7"),
            new("weapbox","Оружейный шкаф",48,78,78,24.0f,39.0f,87.6096f,true,"4efead33dfc50537e58cde2ef2cc658f9ef9864152f90d114ef575e43d65ccbf","d4c3aacd458b0556f3ffea68b332315d721180743822e69e3c7ada21e522d323"),
            new("medbox","Аптечка",38,38,38,19.0f,0f,0f,false,"8cc2db8c6c59899680809333b04a1db97cba996d9f80cb0a1ca2926268bd6e5e","c7098bca81f35b73ed225abf88be0bbeb9b6fc886576ba375a3f9adafd910db5"),
            new("instr1","Шкаф для инструментов",75,51,51,37.0f,25.5f,46.818f,true,"ff3a47d3e5fbf1101840ff3a706fc63756f28c4580bc684d7162ab218ed9e3dd","c1059b50380ab5636a684af21ef3374c6587570f287251827f5d0642934e5f7a"),
        };
        private static Artwork Find(string id)=>artwork.SingleOrDefault(value=>value.id==id)??
            throw new InvalidOperationException("Нет явно подготовленной графики офисного контейнера: type="+id);
        public static string AssetPath(string id) {Find(id);return Folder+"/"+id+".asset";}
        public static string Resource(string id) {Find(id);return ResourceFolder+"/"+id+"-closed";}
        public static PropDefinition Definition(string id)=>Find(id).Definition();
        private static string PrefabPath(Artwork entry)=>Folder+"/"+entry.id+".prefab";
        private static string MaterialPath(Artwork entry)=>Folder+"/"+entry.id+".mat";
        private static string Absolute(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));

        [MenuItem("Remains/Контент/Подготовить графику офисных контейнеров")]
        public static PropAsset[] Install()
        {
            AssetDatabase.Refresh();
            foreach(var entry in artwork)
            {
                CheckImage(entry,false);CheckImage(entry,true);
                int existing=new[]{AssetPath(entry.id),PrefabPath(entry),MaterialPath(entry)}.Count(path=>
                    File.Exists(Absolute(path)) || AssetDatabase.LoadMainAssetAtPath(path)!=null);
                if(existing!=0 && existing!=3) throw new InvalidOperationException($"Неполный набор офисного контейнера {entry.id}; авторские файлы не заменены.");
                if(existing==3) CheckAsset(entry);
            }
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","Containers");
            foreach(var entry in artwork)
            {
                if(AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id))!=null) continue;
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/"+Resource(entry.id)+".png");
                var material=WorldArtworkMaterial.Create(sprite.texture);AssetDatabase.CreateAsset(material,MaterialPath(entry));
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
            Debug.Log("Графика офисных контейнеров: четыре типа, восемь родных bitmap без увеличения; настройки и существующие GUID сохранены. Лут, каталог и расстановка устанавливаются отдельно.");
            return artwork.Select(entry=>AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id))).ToArray();
        }
        public static void Check()
        {
            foreach(var entry in artwork) {CheckImage(entry,false);CheckImage(entry,true);CheckAsset(entry);}
            Debug.Log("Офисные контейнеры: PASS — исходные PNG/registration, импорт, четыре определения и prefab/material; аптечка закреплена без физической опоры.");
        }
        private static void CheckImage(Artwork entry,bool open)
        {
            string path="Assets/Remains/Resources/"+ResourceFolder+"/"+entry.id+(open?"-open":"-closed")+".png";
            string file=Absolute(path);
            if(!File.Exists(file)) throw new InvalidOperationException("Нет исходной графики офисного контейнера: "+path);
            using var sha=SHA256.Create();
            string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();
            int height=open?entry.openHeight:entry.height;
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;var settings=new TextureImporterSettings();
            importer?.ReadTextureSettings(settings);
            if(hash!=(open?entry.openHash:entry.closedHash) || texture==null || texture.width!=entry.width || texture.height!=height)
                throw new InvalidOperationException($"Изменён исходный офисный PNG: type={entry.id}, path={path}, hash={hash}, size={texture?.width}×{texture?.height}.");
            if(sprite==null || sprite.rect!=new Rect(0,0,entry.width,height) || sprite.pixelsPerUnit!=PixelsPerUnit ||
                Vector2.Distance(sprite.pivot,new Vector2(entry.pivotX,entry.pivotY))>.0001f || importer==null ||
                importer.textureType!=TextureImporterType.Sprite || importer.spriteImportMode!=SpriteImportMode.Single ||
                settings.spriteMeshType!=SpriteMeshType.FullRect || importer.textureCompression!=TextureImporterCompression.Uncompressed ||
                importer.npotScale!=TextureImporterNPOTScale.None || importer.mipmapEnabled || importer.alphaIsTransparency ||
                importer.filterMode!=FilterMode.Point || importer.wrapMode!=TextureWrapMode.Clamp)
                throw new InvalidOperationException($"Потерян явный импорт офисного bitmap: type={entry.id}, path={path}; нужны native rect, source X registration, единая привязка закрытого/открытого кадра, 50 PPU, Point/Clamp без изменения RGBA.");
        }
        private static void CheckAsset(Artwork entry)
        {
            var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(AssetPath(entry.id));
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(entry));
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(entry));
            if(asset==null || prefab==null || material==null || asset.Prefab==null || asset.Prefab.gameObject!=prefab)
                throw new InvalidOperationException("Нет согласованного набора asset/prefab/material офисного контейнера: "+entry.id);
            var actual=asset.CreateDefinition();var expected=entry.Definition();PropBodyRules.Validate(actual.movable,actual.hasPhysicalBody,actual.mass,"офисный тип="+entry.id);
            if(actual.id!=expected.id || actual.name!=expected.name || actual.resource!=expected.resource || actual.width!=expected.width ||
                actual.height!=expected.height || actual.mass!=expected.mass || actual.movable!=expected.movable || actual.hasPhysicalBody!=expected.hasPhysicalBody)
                throw new InvalidOperationException("Определение офисного контейнера изменено; оно не заменено: "+AssetPath(entry.id));
            var view=asset.Prefab;var renderer=view.Artwork;
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/"+Resource(entry.id)+".png");
            var anchor=entry.body?PropVisualAnchor.BodyCenter:PropVisualAnchor.PlacementAnchor;
            if(renderer==null || renderer.transform!=prefab.transform || view.transform!=prefab.transform || renderer.sprite!=sprite || renderer.sharedMaterial!=material ||
                material.mainTexture!=asset.Texture || material.shader!=Resources.Load<Shader>("Presentation/WorldLit") || view.VisualAnchor!=anchor ||
                view.FitsStateWidth!=entry.body || renderer.sortingOrder!=ActorVisualLayers.RestingObjectOrder ||
                prefab.transform.localPosition!=Vector3.zero || prefab.transform.localRotation!=Quaternion.identity || prefab.transform.localScale!=Vector3.one ||
                prefab.GetComponentsInChildren<PropView>(true).Length!=1 || prefab.GetComponentsInChildren<SpriteRenderer>(true).Length!=1)
                throw new InvalidOperationException("Настройки prefab офисного контейнера расходятся с контрактом; они не заменены: "+PrefabPath(entry));
            float expectedMinX=-entry.pivotX/PixelsPerUnit,expectedMinY=-entry.pivotY/PixelsPerUnit;
            if(Mathf.Abs(sprite.bounds.min.x-expectedMinX)>.00001f || Mathf.Abs(sprite.bounds.min.y-expectedMinY)>.00001f ||
                Mathf.Abs(sprite.bounds.size.x-actual.width)>.00001f || Mathf.Abs(sprite.bounds.size.y-actual.height)>.00001f)
                throw new InvalidOperationException("Фактические bounds спрайта не соответствуют source registration офисного контейнера: "+entry.id);
        }
    }
}
