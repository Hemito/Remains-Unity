using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    // Narrow importer for the four Trap.as symbols. It never rebuilds room tiles, the content package or creatures.
    public static class SpikeArtworkAuthoring
    {
        public const string Folder="Assets/Remains/Resources/Presentation/Spikes",CatalogPath=Folder+"/SpikeArtwork.asset",PrefabPath=Folder+"/SpikeVisual.prefab";
        public static string Package=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/spike-art-20261105"));
        private const string Marker="SpikeArtwork:";
        private sealed class Input
        {
            public string path,sha,resource;
            public int frame,width,height;
            public float x,y,zoom;
            public byte[] png;
            public int importedWidth,importedHeight;
        }
        [MenuItem("Remains/Комнаты/Подготовить два слоя шипов")]
        public static void Build()
        {
            var manifest=ReadManifest();string hash=Hash(File.ReadAllBytes(Path.Combine(Package,"spike-artwork.json")));
            var existing=AssetDatabase.LoadAssetAtPath<SpikeArtworkCatalog>(CatalogPath);
            if(existing!=null)
            {
                if(existing.ManifestSha256!=hash) throw new InvalidDataException("Графика шипов уже установлена из другого манифеста; автоматическая замена запрещена.");
                existing.Validate();SpikeArtworkChecks.CheckCatalog(existing);Debug.Log("Двухслойная графика шипов уже подготовлена; авторские ассеты сохранены.");return;
            }
            foreach(string protectedPath in new[]{CatalogPath,PrefabPath,Folder+"/Spikes.mat"})
                if(File.Exists(protectedPath)) throw new InvalidDataException("Установка шипов не перезаписывает неизвестный ассет: "+protectedPath);
            var inputs=ReadInputs(manifest);
            foreach(var item in inputs)
                if(File.Exists(item.path)) throw new InvalidDataException("Установка шипов не перезаписывает существующий рисунок без каталога: "+item.path);
            EnsureFolder(Folder);
            var frames=new Dictionary<(string,int),SpikeArtworkFrame>();
            foreach(var item in inputs)
            {
                File.WriteAllBytes(item.path,item.png);AssetDatabase.ImportAsset(item.path,ImportAssetOptions.ForceSynchronousImport);
                var importer=AssetImporter.GetAtPath(item.path) as TextureImporter??throw new InvalidDataException("Нет импортёра шипов: "+item.path);
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
                importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
                importer.spritePixelsPerUnit=50*item.zoom;importer.spritePivot=ImportedArtPadding.Pivot(item.x,item.y,item.zoom,item.importedWidth,item.importedHeight);
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
                importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {name="Standalone",overridden=true,format=TextureImporterFormat.BC7,maxTextureSize=8192,compressionQuality=100});
                importer.userData=Marker+item.sha;importer.SaveAndReimport();
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(item.path)??throw new InvalidDataException("Не импортирован кадр шипов: "+item.path);
                frames.Add((item.resource,item.frame),new SpikeArtworkFrame {resource=item.resource,frame=item.frame,sha256=item.sha,zoom=item.zoom,
                    sourceOrigin=new Vector2(item.x,item.y),sourceSize=new Vector2Int(item.width,item.height),sprite=sprite});
            }
            var material=WorldArtworkMaterial.CreateForSpriteRenderer();material.name="Шипы";AssetDatabase.CreateAsset(material,Folder+"/Spikes.mat");
            var root=new GameObject("Шипы: два исходных слоя");SpikeVisual prefab;
            try
            {
                SpriteRenderer Layer(string title)
                {
                    var node=new GameObject(title);node.transform.SetParent(root.transform,false);
                    var renderer=node.AddComponent<SpriteRenderer>();renderer.sharedMaterial=material;return renderer;
                }
                var rear=Layer("Задний слой");rear.sortingOrder=RoomTerrainView.ObjectOrder;
                var front=Layer("Передний слой");var group=front.gameObject.AddComponent<SortingGroup>();
                group.sortAtRoot=true;group.sortingOrder=WorldVisualOrderPolicy.ForegroundFixtureOrder;
                var visual=root.AddComponent<SpikeVisual>();visual.Configure(rear,front,group);
                var saved=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool success);
                if(!success || saved==null) throw new IOException("Не записан двухслойный префаб шипов.");
                prefab=saved.GetComponent<SpikeVisual>();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            var definitions=new List<SpikeArtworkDefinition>();
            foreach(var entry in (JArray)manifest["entries"])
            {
                SpikeArtworkFrame[] Layer(string key)=>frames.Where(pair=>pair.Key.Item1==(string)entry[key]).OrderBy(pair=>pair.Key.Item2).Select(pair=>pair.Value).ToArray();
                definitions.Add(new SpikeArtworkDefinition {id=(string)entry["id"],rawXml=(string)entry["rawXml"],rear=Layer("rearResource"),front=Layer("frontResource")});
            }
            var catalog=ScriptableObject.CreateInstance<SpikeArtworkCatalog>();catalog.Configure(hash,prefab,definitions.ToArray());
            AssetDatabase.CreateAsset(catalog,CatalogPath);AssetDatabase.SaveAssets();
            SpikeArtworkChecks.CheckCatalog(catalog);
            Debug.Log("Графика шипов: подготовлены 2 определения, 4 исходных символа, 10 статических кадров, два слоя и префаб; геометрия урона сохранена.");
        }
        public static void BuildBatch()
        {
            try {Build();SpikeArtworkChecks.Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        internal static JObject ReadManifest()
        {
            var manifest=JObject.Parse(File.ReadAllText(Path.Combine(Package,"spike-artwork.json")));
            if((string)manifest["format"]!="remains-spike-artwork" || (int)manifest["version"]!=1 || (double)manifest["sourcePixelsPerMetre"]!=50)
                throw new InvalidDataException("Неизвестный формат/масштаб целевой графики шипов.");
            foreach(var source in ((JObject)manifest["sources"]).Properties().Where(p=>p.Name.StartsWith("source/",StringComparison.Ordinal)))
                if(Hash(File.ReadAllBytes(SourcePath(source.Name)))!=(string)source.Value) throw new InvalidDataException("Изменён исходник шипов: "+source.Name);
            var expected=new[]{("spikes","pfeUI:3098","pfeUI:3089"),("fspikes","pfeUI:3102","pfeUI:3100")};
            if(((JArray)manifest["entries"]).Count!=2) throw new InvalidDataException("В манифесте шипов ожидается два определения.");
            foreach(var row in expected)
            {
                var entry=((JArray)manifest["entries"]).Single(e=>(string)e["id"]==row.Item1);
                if((string)entry["rearResource"]!=row.Item2 || (string)entry["frontResource"]!=row.Item3 || (int)entry["rearSourceLayer"]!=0 || (int)entry["frontSourceLayer"]!=3)
                    throw new InvalidDataException("Изменилось исходное правило слоёв шипов: "+row.Item1);
            }
            return manifest;
        }
        private static List<Input> ReadInputs(JObject manifest)
        {
            var result=new List<Input>();
            foreach(string id in new[]{"pfeUI:3089","pfeUI:3098","pfeUI:3100","pfeUI:3102"})
            {
                var row=((JArray)manifest["resources"]).Single(r=>(string)r["id"]==id);
                bool raster=id=="pfeUI:3089" || id=="pfeUI:3098";int count=raster?4:1;
                if((int)row["frameCount"]!=count || ((JArray)row["issues"]).Count!=0) throw new InvalidDataException("Неизвестные кадры шипов: "+id);
                for(int frame=1;frame<=count;frame++)
                {
                    var png=((JArray)row["files"]).Single(f=>(int?)f["frame"]==frame && ((string)f["path"]).EndsWith(".png",StringComparison.Ordinal));
                    var svg=((JArray)row["files"]).Single(f=>(int?)f["frame"]==frame && ((string)f["path"]).EndsWith(".svg",StringComparison.Ordinal));
                    var bytes=File.ReadAllBytes(SourcePath((string)png["path"]));
                    if(Hash(bytes)!=(string)png["sha256"] || Hash(File.ReadAllBytes(SourcePath((string)svg["path"])))!=(string)svg["sha256"] ||
                        (float)png["zoom"]!=(raster?1:8) || png["originX"].Type==JTokenType.Null || png["originY"].Type==JTokenType.Null ||
                        (double)png["originX"]!=(double)svg["originX"] || (double)png["originY"]!=(double)svg["originY"])
                        throw new InvalidDataException($"Изменён кадр/регистрация/масштаб шипов {id}/{frame}.");
                    var padded=ImportedArtPadding.Pad(bytes,out int width,out int height);
                    if(width!=(int)png["width"] || height!=(int)png["height"]) throw new InvalidDataException("Размер PNG не совпадает с манифестом: "+png["path"]);
                    result.Add(new Input {path=Folder+"/"+id.Replace(':','-')+"-"+frame+".png",sha=(string)png["sha256"],resource=id,frame=frame,
                        width=width,height=height,x=(float)png["originX"],y=(float)png["originY"],zoom=(float)png["zoom"],png=padded??bytes,
                        importedWidth=padded==null?width:ImportedArtPadding.Up(width),importedHeight=padded==null?height:ImportedArtPadding.Up(height)});
                }
            }
            return result;
        }
        internal static string Hash(byte[] bytes)=>ImportedArtPadding.Hash(bytes);
        internal static string SourcePath(string relative)
        {
            string path=Path.GetFullPath(Path.Combine(Package,relative));
            if(!path.StartsWith(Package+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Исходник шипов вне пакета: "+relative);
            return path;
        }
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
