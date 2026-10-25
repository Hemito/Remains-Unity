using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content.VectorArt;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Writes the shipped vector art package (Assets/StreamingAssets/VectorArt, read by VectorArtLibrary):
    //  - the converter's RVG documents (gzip) and their bitmaps, unchanged;
    //  - the game render profile per archive, from the catalog the references were rendered with;
    //  - the resources kept as rasters: every resource with a frame over the target in the game rasterizer's comparison.
    // -executeMethod Remains.Editor.VectorArtPackaging.PackBatch [-rvgDir .reference/vector-probe/rvg]
    //   [-catalogDir .reference/content-catalog-v9] [-rvgReport .reference/vector-probe/game-rasterizer/report.json]
    public static class VectorArtPackaging
    {
        public static string Destination=>Path.Combine(Application.streamingAssetsPath,VectorArtPackage.Folder);
        public const double TargetMae=.01;

        public static void PackBatch()
        {
            try
            {
                string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
                var manifest=Pack(Argument("-rvgDir")??Path.Combine(root,".reference/vector-probe/rvg"),
                    Argument("-catalogDir")??Path.Combine(root,".reference/content-catalog-v9"),
                    Argument("-rvgReport")??Path.Combine(root,".reference/vector-probe/game-rasterizer/report.json"),Destination);
                Debug.Log($"Пакет векторной графики: документов {manifest.documents.Count}, файлов {manifest.sha256.Count}, растровых исключений {manifest.raster.Count} → {Destination}.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static VectorArtPackage.Manifest Pack(string rvgDir,string catalogDir,string reportPath,string destination)
        {
            rvgDir=Path.GetFullPath(rvgDir);
            var manifest=new VectorArtPackage.Manifest {version=VectorArtPackage.Version};
            var report=JsonUtility.FromJson<RvgReferenceComparison.Report>(File.ReadAllText(reportPath,Encoding.UTF8));
            if(report?.results==null || report.results.Count==0) throw new InvalidDataException("Пустой отчёт сравнения: "+reportPath);
            if(!report.profile) throw new InvalidDataException("Отчёт сравнения сделан без профиля рендера; исключения по нему не годятся: "+reportPath);
            if(Directory.Exists(destination)) Directory.Delete(destination,true);
            Directory.CreateDirectory(destination);
            void Copy(string relative)
            {
                string target=Path.Combine(destination,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(rvgDir,relative),target);
                manifest.sha256.Add(relative.Replace('\\','/'),VectorArtPackage.Sha256(File.ReadAllBytes(target)));
            }
            foreach(string gzip in Directory.GetFiles(rvgDir,"*.rvg.json.gz").OrderBy(p=>p,StringComparer.Ordinal))
            {
                string name=Path.GetFileName(gzip);
                // The plain JSON is the converter's checked output; the gzip must be the same document.
                string plain=Path.Combine(rvgDir,name.Substring(0,name.Length-3));
                using(var stream=new System.IO.Compression.GZipStream(File.OpenRead(gzip),System.IO.Compression.CompressionMode.Decompress))
                using(var reader=new StreamReader(stream,Encoding.UTF8))
                    if(File.Exists(plain) && reader.ReadToEnd()!=File.ReadAllText(plain,Encoding.UTF8)) throw new InvalidDataException("gzip RVG не совпадает с JSON: "+name);
                var document=RvgDocument.Parse(File.ReadAllText(plain,Encoding.UTF8),name);
                Copy(name);manifest.documents.Add(name);
                foreach(var bitmap in document.bitmaps) Copy(bitmap.file);
                var rules=CatalogProfile(catalogDir,document.source.archive);
                manifest.profiles.Add(new VectorArtPackage.Profile {archive=document.source.archive,hairlineTwips=rules.HairlineTwips,
                    glowFreeSprites=rules.GlowFreeSprites.OrderBy(i=>i).ToList(),parts=rules.Parts.Select(p=>new VectorArtPackage.Part {sprite=p.Sprite,depth=p.Depth}).ToList()});
            }
            if(manifest.documents.Count==0) throw new InvalidDataException("Нет *.rvg.json.gz в "+rvgDir);
            foreach(var group in report.results.Where(r=>string.IsNullOrEmpty(r.problem) && r.mae>TargetMae).GroupBy(r=>r.resource).OrderBy(g=>g.Key,StringComparer.Ordinal))
                manifest.raster.Add(new VectorArtPackage.Raster {resource=group.Key,reason=string.Format(CultureInfo.InvariantCulture,
                    "кадров с MAE > {0} против эталона: {1}, худший {2:F4} (сравнение игрового растеризатора)",TargetMae,group.Count(),group.Max(r=>r.mae))});
            File.WriteAllText(VectorArtPackage.ManifestPath(destination),JsonConvert.SerializeObject(manifest,Formatting.Indented),new UTF8Encoding(false));
            AssetDatabase.Refresh();
            return manifest;
        }

        // The profile the catalog references were rendered with: catalog.json `renders` for the archive, parts from the
        // part:* bindings (Tools/ContentExport/RenderSource.cs).
        public static RvgRenderProfile CatalogProfile(string catalogDir,string archive)
        {
            var catalog=JObject.Parse(File.ReadAllText(Path.Combine(catalogDir,"catalog.json"),Encoding.UTF8));
            var render=catalog["renders"]?.FirstOrDefault(r=>(string)r["archive"]==archive)
                ??throw new InvalidDataException($"В каталоге нет профиля рендера для {archive}.");
            var parts=catalog["definitions"].SelectMany(d=>d["graphics"]??new JArray())
                .Where(g=>((string)g["role"])?.StartsWith("part:",StringComparison.Ordinal)==true && ((string)g["resource"])?.StartsWith(archive+":",StringComparison.Ordinal)==true)
                .Select(g=>System.Xml.Linq.XElement.Parse((string)g["metadataXml"]))
                .Select(m=>new RvgRenderProfile.Part((int)m.Attribute("osn"),(int)m.Attribute("depth")));
            return new RvgRenderProfile((int)render["hairlineTwips"],render["glowFreeSprites"].Select(t=>(int)t),parts);
        }
        private static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);
            return at>=0 && at+1<args.Length?args[at+1]:null;
        }
    }
}
