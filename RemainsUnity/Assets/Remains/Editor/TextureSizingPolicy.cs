using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Editor-only policy. Source PNGs, sprite registration and runtime paths stay unchanged.
    public sealed class TextureSizingPolicy : AssetPostprocessor
    {
        public const string TablePath="Assets/Remains/Editor/TextureSizingTable.json";
        [Serializable] public sealed class Entry
        {
            public string path,decision,reason;
            public int sourceWidth,sourceHeight,maxSize;
            public bool mipmaps,unmeasurable;
            public float screenPixelsPerSourcePixel;
        }
        [Serializable] public sealed class Table
        {
            public int version=1,referenceHeight=2160;
            public float orthographicSize;
            public List<Entry> entries=new();
        }
        private static Dictionary<string,Entry> cached;
        private static DateTime stamp;
        private static bool rebuildQueued;
        public override int GetPostprocessOrder()=>10000;
        private static Dictionary<string,Entry> Read()
        {
            if(!File.Exists(TablePath)) return new Dictionary<string,Entry>();
            var modified=File.GetLastWriteTimeUtc(TablePath);
            if(cached==null || modified!=stamp)
            {
                var table=JsonConvert.DeserializeObject<Table>(File.ReadAllText(TablePath))??throw new InvalidDataException("Пустая таблица размеров текстур.");
                if(table.version!=1) throw new InvalidDataException("Неизвестная версия таблицы размеров текстур.");
                cached=table.entries.ToDictionary(e=>e.path,StringComparer.Ordinal);stamp=modified;
            }
            return cached;
        }
        private void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/Remains/",StringComparison.Ordinal)) return;
            if(!File.Exists(TablePath)) return; // Bootstrap: no policy has been authored yet.
            context.DependsOnSourceAsset(TablePath);
            if(!Read().TryGetValue(assetPath,out var entry))
            {
                Debug.LogWarning($"Текстура {assetPath}: нет измерения в {TablePath}; настройки сохранены. Выполните TextureSizingAuthoring.AnalyzeBatch.");return;
            }
            if(entry.decision=="exception") return;
            var importer=(TextureImporter)assetImporter;
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(width!=entry.sourceWidth || height!=entry.sourceHeight)
                throw new InvalidOperationException($"Текстура {assetPath}: размер исходника изменился {entry.sourceWidth}×{entry.sourceHeight} → {width}×{height}; пересчитайте таблицу импорта.");
            if(importer.isReadable || PonyTextureImportPolicy.Applies(assetPath))
                throw new InvalidOperationException($"Текстура {assetPath}: новое исключение CPU/пони; пересчитайте таблицу перед импортом.");
            var platform=importer.GetPlatformTextureSettings("Standalone");
            platform.name="Standalone";platform.overridden=true;
            platform.format=entry.decision=="alpha-only"?TextureImporterFormat.Alpha8:TextureImporterFormat.BC7;platform.maxTextureSize=entry.maxSize;
            platform.textureCompression=entry.decision=="alpha-only"?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ;
            platform.compressionQuality=100;platform.crunchedCompression=false;
            importer.SetPlatformTextureSettings(platform);
            // Do not introduce mipmaps for unknown consumers, masks or UI. Known world art
            // keeps its existing mip policy (including sharp base-pony policy exceptions).
            importer.mipmapEnabled=entry.mipmaps;
        }
        // New art gets a table row after the import transaction, never while loading
        // other assets from inside OnPreprocessTexture. Unknown usage retains size.
        private static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] movedFrom)
        {
            if(rebuildQueued || !File.Exists(TablePath)) return;
            bool Texture(string p)=>p.StartsWith("Assets/Remains/",StringComparison.Ordinal) &&
                (p.EndsWith(".png",StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase));
            bool Usage(string p)=>p=="Assets/Remains/Scenes/Remains.unity" ||
                p.EndsWith(".room.json",StringComparison.Ordinal) || p==RoomTileArtPostprocessor.ManifestPath ||
                p=="Assets/Remains/Rooms/Original/definitions.json" || p=="Assets/Remains/Rooms/Original/room-lighting.json" ||
                p=="Assets/Remains/Resources/Content/ExportedContent.asset" ||
                (p.StartsWith("Assets/Remains/Resources/Presentation/",StringComparison.Ordinal) && p.EndsWith(".prefab",StringComparison.Ordinal));
            if(!imported.Concat(moved).Any(p=>Usage(p) || Texture(p) && !Read().ContainsKey(p)) && !deleted.Any(p=>Texture(p)||Usage(p))) return;
            rebuildQueued=true;
            EditorApplication.delayCall+=()=>{rebuildQueued=false;TextureSizingAuthoring.Apply();};
        }
        public static bool IsApprovedCompression(string path)
            =>Read().TryGetValue(path,out var entry) && entry.decision!="alpha-only" && IsApprovedFormat(path);
        public static bool IsApprovedFormat(string path)
        {
            if(!Read().TryGetValue(path,out var entry) || entry.decision=="exception") return false;
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null || importer.isReadable) return false;
            var p=importer.GetPlatformTextureSettings("Standalone");
            return p.overridden && p.format==(entry.decision=="alpha-only"?TextureImporterFormat.Alpha8:TextureImporterFormat.BC7) && p.maxTextureSize==entry.maxSize &&
                p.textureCompression==(entry.decision=="alpha-only"?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ) && !p.crunchedCompression && importer.mipmapEnabled==entry.mipmaps;
        }
    }
}
