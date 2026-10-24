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
            public bool mipmaps;
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
            if(importer.isReadable || PonyTextureImportPolicy.Applies(assetPath))
                throw new InvalidOperationException($"Текстура {assetPath}: новое исключение CPU/пони; пересчитайте таблицу перед импортом.");
            var platform=importer.GetPlatformTextureSettings("Standalone");
            platform.name="Standalone";platform.overridden=true;
            platform.format=TextureImporterFormat.BC7;platform.maxTextureSize=entry.maxSize;
            platform.textureCompression=TextureImporterCompression.CompressedHQ;
            platform.compressionQuality=100;platform.crunchedCompression=false;
            importer.SetPlatformTextureSettings(platform);
            // Do not introduce mipmaps for unknown consumers, masks or UI. Known world art
            // keeps its existing mip policy (including sharp base-pony policy exceptions).
            importer.mipmapEnabled=entry.mipmaps;
        }
        public static bool IsApprovedCompression(string path)
        {
            if(!Read().TryGetValue(path,out var entry) || entry.decision=="exception") return false;
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null || importer.isReadable) return false;
            var p=importer.GetPlatformTextureSettings("Standalone");
            return p.overridden && p.format==TextureImporterFormat.BC7 && p.maxTextureSize==entry.maxSize;
        }
    }
}
