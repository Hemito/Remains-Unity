using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Remains.Content.VectorArt
{
    // The shipped vector art (StreamingAssets/VectorArt, written by Editor VectorArtPackaging): faithful RVG documents and
    // their bitmaps, the game render profile per archive, and the resources kept as rasters with the reason. Loading is
    // plain C# (no Unity API), so it runs off the main thread.
    public sealed class VectorArtPackage
    {
        public const string Folder="VectorArt",ManifestFile="package.json";
        public const int Version=1;

        [Serializable] public sealed class Manifest
        {
            public int version;
            public List<string> documents=new();
            public List<Profile> profiles=new();
            public List<Raster> raster=new();
            public Dictionary<string,string> sha256=new();
        }
        [Serializable] public sealed class Profile
        {
            public string archive;
            public int hairlineTwips;
            public List<int> glowFreeSprites=new();
            public List<Part> parts=new();
        }
        [Serializable] public sealed class Part {public int sprite,depth;}
        [Serializable] public sealed class Raster {public string resource,reason;}

        public readonly string Root;
        public readonly Manifest Contents;
        public readonly IReadOnlyList<RvgDocument> Documents;
        public readonly IReadOnlyDictionary<string,string> RasterResources;
        public readonly IReadOnlyList<string> ProfileReport;

        private VectorArtPackage(string root,Manifest manifest,List<RvgDocument> documents,List<string> profileReport)
        {
            Root=root;Contents=manifest;Documents=documents;ProfileReport=profileReport;
            RasterResources=manifest.raster.ToDictionary(r=>r.resource,r=>r.reason,StringComparer.Ordinal);
        }

        public static string ManifestPath(string root)=>Path.Combine(root,ManifestFile);
        public static Manifest ReadManifest(string root)
        {
            var manifest=JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(ManifestPath(root),Encoding.UTF8),
                new JsonSerializerSettings {MissingMemberHandling=MissingMemberHandling.Error})
                ??throw new InvalidDataException("Пустой манифест векторной графики: "+ManifestPath(root));
            if(manifest.version!=Version) throw new InvalidDataException($"Манифест векторной графики версии {manifest.version}, ожидается {Version}.");
            if(manifest.documents.Count==0) throw new InvalidDataException("Манифест векторной графики без документов.");
            foreach(var raster in manifest.raster)
                if(string.IsNullOrWhiteSpace(raster.resource) || string.IsNullOrWhiteSpace(raster.reason)) throw new InvalidDataException("Растровое исключение без ресурса или причины.");
            return manifest;
        }

        // Every file is checked against the manifest before use; documents are parsed and the profile applied.
        public static VectorArtPackage Load(string root)
        {
            var manifest=ReadManifest(root);
            foreach(var pair in manifest.sha256)
            {
                string path=Path.Combine(root,pair.Key);
                if(!File.Exists(path)) throw new FileNotFoundException("Нет файла векторной графики: "+pair.Key,path);
                if(Sha256(File.ReadAllBytes(path))!=pair.Value) throw new InvalidDataException("Изменён файл векторной графики: "+pair.Key);
            }
            var documents=new List<RvgDocument>();var report=new List<string>();
            foreach(string name in manifest.documents)
            {
                if(!manifest.sha256.ContainsKey(name)) throw new InvalidDataException("Документ без подписи в манифесте: "+name);
                using var gzip=new GZipStream(File.OpenRead(Path.Combine(root,name)),CompressionMode.Decompress);
                using var reader=new StreamReader(gzip,Encoding.UTF8);
                var document=RvgDocument.Parse(reader.ReadToEnd(),name);
                var profile=manifest.profiles.SingleOrDefault(p=>p.archive==document.source.archive)
                    ??throw new InvalidDataException("Нет профиля рендера для архива "+document.source.archive+".");
                var applied=new RvgRenderProfile(profile.hairlineTwips,profile.glowFreeSprites,profile.parts.Select(p=>new RvgRenderProfile.Part(p.sprite,p.depth))).Apply(document);
                report.Add($"{document.source.archive}: линий расширено {applied.WidenedStrokes}, свечений снято {applied.GlowFiltersRemoved}, частей убрано {applied.PartPlacementsRemoved}");
                documents.Add(document);
            }
            return new VectorArtPackage(root,manifest,documents,report);
        }
        public static string Sha256(byte[] data)
        {
            using var sha=SHA256.Create();
            return string.Concat(sha.ComputeHash(data).Select(b=>b.ToString("x2")));
        }
    }
}
