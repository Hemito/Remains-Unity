using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The radioactive barrel from the original data: AllData obj radbarrel (tip box, ico phis) gives mass and rad; the body is
    // its picture (loc/Box.as scX/scY = vis bounds), the room-tile export of texture1:40, at 50 px per metre. Placed on the
    // generated factory as a later physical type (physical-v3); radiation itself is TD-37.
    //   batch -executeMethod Remains.Editor.FactoryRadBarrelAuthoring.RunBatch
    public static class FactoryRadBarrelAuthoring
    {
        public const string Id="radbarrel",Folder="Assets/Remains/RoomDrafts/FactoryProps",AssetPath=Folder+"/radbarrel.asset";
        private const string Resource="Original/Props/radbarrel",Tile="Assets/Remains/Rooms/Original/Tiles/objects/radbarrel-f1.png";
        private const float PixelsPerMetre=50;

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            if(AssetDatabase.LoadMainAssetAtPath(AssetPath)!=null) throw new InvalidOperationException("Радиоактивная бочка уже сохранена; авторские правки не заменены.");
            var row=((JArray)JObject.Parse(File.ReadAllText(ContentPackage.Catalog))["definitions"]).OfType<JObject>()
                .Single(r=>(string)r["category"]=="obj" && (string)r["id"]==Id);
            var obj=XElement.Parse((string)row["rawXml"]);
            if((string)obj.Attribute("tip")!="box" || (string)obj.Attribute("ico")!="phis" || obj.Attribute("cont")!=null)
                throw new InvalidDataException("radbarrel в выгрузке оригинала больше не простой физический ящик: "+obj);
            float mass=Num(obj,"massa"),radioactivity=Num(obj,"rad");

            string texturePath="Assets/Remains/Resources/"+Resource+".png";
            if(File.Exists(texturePath)) throw new InvalidOperationException("Рисунок бочки уже существует: "+texturePath);
            AssetDatabase.CopyAsset(Tile,texturePath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType=TextureImporterType.Default;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
            importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture==null || texture.width!=40 || texture.height!=57) throw new InvalidOperationException("Изменён исходный рисунок бочки (ожидается 40×57).");

            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),PixelsPerMetre,0,SpriteMeshType.FullRect);
            AssetDatabase.CreateAsset(sprite,Folder+"/radbarrel-sprite.asset");
            var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,Folder+"/radbarrel.mat");
            // Every catalog type carries an explicit container artwork profile; the barrel has one picture (Static).
            var profile=ScriptableObject.CreateInstance<ContainerArtworkProfileAsset>();profile.name=Id+"-artwork";
            profile.Configure(Id,ContainerArtworkMode.Static,sprite,null);AssetDatabase.CreateAsset(profile,ContainerArtworkAuthoring.ProfilePath(Id));
            var definition=ScriptableObject.CreateInstance<PropAsset>();
            definition.SetDefinition(new PropDefinition {id=Id,name="Радиоактивная бочка",mass=mass,width=texture.width/PixelsPerMetre,
                height=texture.height/PixelsPerMetre,resource=Resource,radioactivity=radioactivity});
            var root=new GameObject("Радиоактивная бочка");
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
                var view=root.AddComponent<PropView>();view.SetArtwork(renderer);view.ConfigureContainerArtwork(profile);
                definition.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,Folder+"/radbarrel.prefab").GetComponent<PropView>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            AssetDatabase.CreateAsset(definition,AssetPath);
            var catalog=EntityAssetCatalog.Load();catalog.AddProp(definition);EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            FactoryLevelAuthoring.ConfigureProps();
            AssetDatabase.SaveAssets();
            Debug.Log($"Радиоактивная бочка: {mass} кг, {texture.width/PixelsPerMetre}×{texture.height/PixelsPerMetre} м, rad={radioactivity} (радиация — TD-37); добавлена в каталог и корень завода.");
        }
        private static float Num(XElement obj,string name)=>float.Parse((string)obj.Attribute(name)
            ??throw new InvalidDataException($"radbarrel: нет атрибута {name}."),CultureInfo.InvariantCulture);
    }
}
