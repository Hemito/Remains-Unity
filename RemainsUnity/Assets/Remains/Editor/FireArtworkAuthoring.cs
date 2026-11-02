using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The flames of fire from the export: part fire's sheet sprFire (sprite1:32, 17 frames of 50×67 px) copied into the
    // project and cut into sprites standing on their base (50 px per metre), with a world material, into FireArtwork.
    //   batch -executeMethod Remains.Editor.FireArtworkAuthoring.RunBatch
    public static class FireArtworkAuthoring
    {
        private const string Folder="Assets/Remains/Resources/Original/Effects",Texture=Folder+"/fire.png",MaterialPath=Folder+"/fire.mat";
        private const string Asset="Assets/Remains/Resources/"+FireArtwork.Resource+".asset";
        private const int Frames=17,Width=50,Height=67;private const float PixelsPerMetre=50,FramesPerSecond=15;   // part fire anim 2: every second frame

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            string source=Path.Combine(ContentPackage.Directory,"images","sprite1","32","32_sprFire.png");
            if(!File.Exists(source)) throw new FileNotFoundException("Нет листа пламени выгрузки sprFire: "+source);
            Directory.CreateDirectory(Folder);File.Copy(source,Texture,true);
            AssetDatabase.ImportAsset(Texture,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Texture);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit=PixelsPerMetre;importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
#pragma warning disable 618
            importer.spritesheet=Enumerable.Range(0,Frames).Select(i=>new SpriteMetaData {name="fire_"+i.ToString("00"),rect=new Rect(i*Width,0,Width,Height),
                alignment=(int)SpriteAlignment.BottomCenter,pivot=new Vector2(.5f,0)}).ToArray();
#pragma warning restore 618
            importer.SaveAndReimport();
            var sprites=AssetDatabase.LoadAllAssetsAtPath(Texture).OfType<Sprite>().OrderBy(s=>s.name,StringComparer.Ordinal).ToArray();
            if(sprites.Length!=Frames) throw new InvalidDataException($"Лист пламени: {sprites.Length} кадров вместо {Frames}.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null) {material=WorldArtworkMaterial.CreateForSpriteRenderer();AssetDatabase.CreateAsset(material,MaterialPath);}
            var artwork=AssetDatabase.LoadAssetAtPath<FireArtwork>(Asset);bool fresh=artwork==null;
            if(fresh) artwork=ScriptableObject.CreateInstance<FireArtwork>();
            artwork.Configure(sprites,material,FramesPerSecond);
            if(fresh) AssetDatabase.CreateAsset(artwork,Asset);else EditorUtility.SetDirty(artwork);
            AssetDatabase.SaveAssets();
            Debug.Log($"Рисунок огня: {sprites.Length} кадров {Width}×{Height} px из sprFire.");
        }
    }
}
