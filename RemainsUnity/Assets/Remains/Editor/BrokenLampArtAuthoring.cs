using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class BrokenLampArtAuthoring
    {
        public const string Root="Assets/Remains/Resources/Presentation/Lamps/Broken/";
        public const string AssetPath=Root+"LampArt.asset";
        private const string Archive="Q:/Загрузка/Remains_broken_lamps.zip";
        private static readonly string[] Types={"light1","light2","light3","light4","light5","light6","light7","stlight1","stlight3","stlight4"};

        public static void GenerateAndCheck()
        {
            try {Generate();LampPresentationChecks.RunBatch();}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        [MenuItem("Remains/Свет/Импортировать разбитые лампы")]
        public static void Generate()
        {
            var original=AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(RoomTileArtAuthoring.AssetPath)
                ??throw new InvalidOperationException("Нет исходного набора графики комнат.");
            using var zip=ZipFile.OpenRead(Archive);
            var named=zip.Entries.ToDictionary(entry=>entry.FullName,StringComparer.Ordinal);
            if(!named.TryGetValue("manifest_broken.json",out var manifestEntry)) throw new InvalidOperationException("Нет манифеста разбитых ламп.");
            JsonValue manifest;
            using(var reader=new StreamReader(manifestEntry.Open())) manifest=JsonText.Parse(reader.ReadToEnd(),Archive);
            if(manifest.Get("format").AsString(Archive)!="remains-lamp-broken-art" || manifest.Get("version").AsLong(Archive)!=1 || manifest.Get("types").AsLong(Archive)!=Types.Length)
                throw new InvalidOperationException("Неверный формат/версия/число видов разбитых ламп.");
            var rows=manifest.Get("art").Items;
            if(rows.Count!=Types.Length || !new HashSet<string>(rows.Select(row=>Text(row,"type")),StringComparer.Ordinal).SetEquals(Types))
                throw new InvalidOperationException("Манифест разбитых ламп содержит неожиданные или повторные виды.");
            var omitted=manifest.Get("notIncluded").Items.Select(item=>item.AsString(Archive)).ToArray();
            if(omitted.Length!=2 || !new HashSet<string>(omitted).SetEquals(new[]{"bar","exit_light"}))
                throw new InvalidOperationException("Манифест разбитых ламп содержит неожиданные исключения.");
            // Validate all archive inputs before writing any project assets. Only explicit approved paths are read.
            var validated=new List<(string id,string type,TileArt reference,byte[] bytes)>();
            foreach(var row in rows)
            {
                string type=Text(row,"type"),id=Text(row,"definitionId"),path=Text(row,"path");
                if(id!="remains:decor:"+type || path!=type+"/broken.png" || !named.TryGetValue(path,out var entry))
                    throw new InvalidOperationException($"Разбитая лампа {type}: неверный ID или путь {path}.");
                string frame=Text(row,"referenceOffFrame");
                if(frame.Length<2 || frame[0]!='f' || !int.TryParse(frame.Substring(1),out int frameNumber))
                    throw new InvalidOperationException($"Разбитая лампа {type}: неверный исходный кадр {frame}.");
                int expectedFrame=type=="light2" || type.StartsWith("stlight",StringComparison.Ordinal)?3:2;
                if(frameNumber!=expectedFrame) throw new InvalidOperationException($"Разбитая лампа {type}: ожидается исходный выключенный кадр f{expectedFrame}, получен {frame}.");
                var reference=original.Manifest.Art[TileManifest.DecorFrameId(type,frameNumber)];
                if(Number(row,"width")!=reference.Width || Number(row,"height")!=reference.Height || Number(row,"scale")!=reference.Zoom || Number(row,"originX")!=reference.OriginX || Number(row,"originY")!=reference.OriginY)
                    throw new InvalidOperationException($"Разбитая лампа {type}: размеры, масштаб или привязка не совпадают с {reference.Id}.");
                byte[] bytes;
                using(var stream=entry.Open()) using(var memory=new MemoryStream()) {stream.CopyTo(memory);bytes=memory.ToArray();}
                using(var sha=SHA256.Create())
                    if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=Text(row,"sha256"))
                        throw new InvalidOperationException($"Разбитая лампа {type}: SHA256 изображения не совпадает с манифестом.");
                var probe=new Texture2D(2,2);
                try {if(!ImageConversion.LoadImage(probe,bytes) || probe.width!=reference.Width || probe.height!=reference.Height) throw new InvalidOperationException($"Разбитая лампа {type}: неверное PNG изображение или размер.");}
                finally {UnityEngine.Object.DestroyImmediate(probe);}
                validated.Add((id,type,reference,bytes));
            }
            Directory.CreateDirectory(Root);AssetDatabase.Refresh();
            var asset=AssetDatabase.LoadAssetAtPath<BrokenLampArt>(AssetPath);
            if(asset==null) {asset=ScriptableObject.CreateInstance<BrokenLampArt>();AssetDatabase.CreateAsset(asset,AssetPath);}
            var oldSprites=AssetDatabase.LoadAllAssetsAtPath(AssetPath).OfType<Sprite>().ToDictionary(sprite=>sprite.name,StringComparer.Ordinal);
            var results=new List<BrokenLampArt.Entry>();
            foreach(var item in validated)
            {
                string path=Root+item.type+".png";
                File.WriteAllBytes(path,item.bytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=(float)(RoomTileArtPostprocessor.SourcePixelsPerMeter*item.reference.Zoom);
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=true;
                importer.mipmapEnabled=true;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
                var pivot=new Vector2((float)(item.reference.OriginX*item.reference.Zoom/item.reference.Width),(float)(1-item.reference.OriginY*item.reference.Zoom/item.reference.Height));
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spriteMeshType=SpriteMeshType.FullRect;
                settings.spriteExtrude=0;settings.spritePivot=pivot;importer.SetTextureSettings(settings);importer.SaveAndReimport();
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(texture.width!=item.reference.Width || texture.height!=item.reference.Height) throw new InvalidOperationException($"Разбитая лампа {item.type}: импорт изменил разрешение.");
                var desired=pivot*new Vector2(texture.width,texture.height);
                if((sprite.pivot-desired).sqrMagnitude>.0001f)
                {
                    // Some Unity importer versions clamp registration outside the canvas. A serialized full-rect Sprite preserves it.
                    Debug.LogWarning($"Разбитая лампа {item.type}, {path}: импортёр изменил pivot с {desired} на {sprite.pivot}; создаётся сериализованный спрайт с исходной привязкой.");
                    if(oldSprites.TryGetValue(item.type,out var stale)) {UnityEngine.Object.DestroyImmediate(stale,true);oldSprites.Remove(item.type);}
                    sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),pivot,importer.spritePixelsPerUnit,0,SpriteMeshType.FullRect);
                    sprite.name=item.type;AssetDatabase.AddObjectToAsset(sprite,asset);
                }
                var originalSprite=original.Tile(item.reference.Id).sprite;
                if((sprite.bounds.center-originalSprite.bounds.center).sqrMagnitude>1e-8f || (sprite.bounds.size-originalSprite.bounds.size).sqrMagnitude>1e-8f)
                    throw new InvalidOperationException($"Разбитая лампа {item.type}: границы {sprite.bounds} отличаются от исходных {originalSprite.bounds}.");
                results.Add(new BrokenLampArt.Entry{definitionId=item.id,sprite=sprite});
                Debug.Log($"Разбитая лампа {item.type}: {texture.width}×{texture.height}, PPU {sprite.pixelsPerUnit}, pivot {sprite.pivot}, bounds {sprite.bounds} совпадают с {item.reference.Id}.");
            }
            foreach(var stale in oldSprites.Values) UnityEngine.Object.DestroyImmediate(stale,true);
            asset.Configure(results.ToArray(),omitted.Select(type=>"remains:decor:"+type).ToArray());
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            Debug.Log("BrokenLampArtAuthoring: PASS — 10 PNG, исходные SHA/размеры/привязки и границы спрайтов проверены.");
        }
        private static string Text(JsonValue row,string key)=>row.Get(key).AsString(Archive+":"+key);
        private static double Number(JsonValue row,string key)=>row.Get(key).AsDouble(Archive+":"+key);
    }
}
