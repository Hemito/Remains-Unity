using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class TextureSizingAuthoring
    {
        public static void AnalyzeBatch()=>Execute(false,true);
        public static void ApplyBatch()=>Execute(true,true);
        [MenuItem("Remains/Импорт/Рассчитать и применить размеры текстур")]
        public static void Apply()=>Execute(true,false);
        private static void Execute(bool apply,bool exit)
        {
            try
            {
                var measurements=TextureSizingUsage.Analyze();
                var table=new TextureSizingPolicy.Table {orthographicSize=measurements.OrthographicSize};
                var reserved=Reserved();var masks=ExactTileArt(out var alphaOnly);
                foreach(string path in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Remains"})
                    .Select(AssetDatabase.GUIDToAssetPath).Where(p=>AssetImporter.GetAtPath(p) is TextureImporter).OrderBy(p=>p,StringComparer.Ordinal))
                {
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    importer.GetSourceTextureWidthAndHeight(out int width,out int height);
                    var platform=importer.GetPlatformTextureSettings("Standalone");
                    int currentMax=platform.overridden?platform.maxTextureSize:importer.maxTextureSize;
                    var entry=new TextureSizingPolicy.Entry {path=path,sourceWidth=width,sourceHeight=height,
                        maxSize=currentMax,mipmaps=importer.mipmapEnabled,decision="compressed-only"};
                    string exception=importer.isReadable?"CPU-readable: RoomLightMask/прочие CPU-потребители; точные исходные пиксели.":
                        PonyTextureImportPolicy.Applies(path)?"Базовые части пони: собственная PonyTextureImportPolicy.":
                        reserved.Contains(path)?"Действующий native/reserve контракт: точный платформенный формат и размер.":
                        masks.TryGetValue(path,out string maskReason)?maskReason:
                        importer.textureType!=TextureImporterType.Default && importer.textureType!=TextureImporterType.Sprite?
                            "Специализированный тип текстуры: "+importer.textureType:null;
                    if(exception!=null) {entry.decision=alphaOnly.Contains(path) && !importer.isReadable?"alpha-only":"exception";entry.reason=exception;}
                    else if(measurements.Textures.TryGetValue(path,out var m) && m.Complete && string.IsNullOrEmpty(m.UnmeasuredReason))
                    {
                        entry.screenPixelsPerSourcePixel=m.ScreenPixelsPerSourcePixel;
                        int needed=Mathf.CeilToInt(Mathf.Max(width,height)*m.ScreenPixelsPerSourcePixel);
                        // Unity's limit is power-of-two, but does not upscale NPOT source files.
                        // E.g. 1500 source pixels need a 2048 cap to retain all 1500 pixels.
                        entry.maxSize=Mathf.Min(16384,Mathf.Min(Mathf.NextPowerOfTwo(Mathf.Max(width,height)),Mathf.Max(32,Mathf.NextPowerOfTwo(Mathf.Max(1,needed)))));
                        entry.reason=string.Join("; ",m.Evidence);
                        if(entry.maxSize<Mathf.Max(width,height)) entry.decision="resized";
                    }
                    else
                    {
                        entry.reason=measurements.Textures.TryGetValue(path,out var unknown)?unknown.UnmeasuredReason:null;
                        if(string.IsNullOrEmpty(entry.reason)) entry.reason="Не доказан максимальный масштаб всех мировых/UI потребителей.";
                        entry.unmeasurable=true;
                        Debug.LogWarning($"Размер текстуры сохранён: path={path}, type={importer.textureType}, причина={entry.reason}");
                    }
                    table.entries.Add(entry);
                }
                string report=Path.GetFullPath("../.reference/texture-sizing");Directory.CreateDirectory(report);
                File.WriteAllText(Path.Combine(report,"decisions.json"),JsonConvert.SerializeObject(table,Formatting.Indented));
                if(apply)
                {
                    string json=JsonConvert.SerializeObject(table,Formatting.Indented);
                    if(!File.Exists(TextureSizingPolicy.TablePath) || File.ReadAllText(TextureSizingPolicy.TablePath)!=json)
                    {
                        File.WriteAllText(TextureSizingPolicy.TablePath,json);
                        AssetDatabase.ImportAsset(TextureSizingPolicy.TablePath,ImportAssetOptions.ForceUpdate);
                    }
                    foreach(var entry in table.entries.Where(e=>e.decision!="exception" && !TextureSizingPolicy.IsApprovedFormat(e.path)))
                        AssetDatabase.ImportAsset(entry.path,ImportAssetOptions.ForceUpdate);
                    AssetDatabase.SaveAssets();
                    Validate(table);
                }
                foreach(var group in table.entries.GroupBy(e=>e.decision)) Debug.Log($"Размеры текстур: {group.Key}={group.Count()}.");
                Debug.Log($"Размеры текстур: неизмеримых={table.entries.Count(e=>e.unmeasurable)}, камера={table.orthographicSize}, высота={table.referenceHeight}, применено={apply}.");
                if(exit) EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);if(exit) EditorApplication.Exit(1);else throw;}
        }
        public static void Validate(TextureSizingPolicy.Table table)
        {
            foreach(var entry in table.entries.Where(e=>e.decision!="exception"))
            {
                if(!TextureSizingPolicy.IsApprovedFormat(entry.path))
                    throw new InvalidOperationException("Не применена таблица импорта: "+entry.path);
                if(entry.decision=="alpha-only") CheckAlpha(entry);
                var importer=(TextureImporter)AssetImporter.GetAtPath(entry.path);
                if(entry.decision!="resized" || importer.spriteImportMode!=SpriteImportMode.Single || importer.textureType!=TextureImporterType.Sprite) continue;
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                if(settings.spriteMeshType!=SpriteMeshType.FullRect) continue;
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(entry.path);
                if(sprite==null) throw new InvalidOperationException("После уменьшения потерян спрайт: "+entry.path);
                var expected=new Vector2(entry.sourceWidth/importer.spritePixelsPerUnit,entry.sourceHeight/importer.spritePixelsPerUnit);
                if(Vector2.Distance(sprite.bounds.size,expected)>.005f)
                    throw new InvalidOperationException($"Уменьшение изменило мировой размер спрайта {entry.path}: {sprite.bounds.size}, ожидается {expected}.");
            }
        }
        private static void CheckAlpha(TextureSizingPolicy.Entry entry)
        {
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            Texture2D copy=null;RenderTexture target=null;var previous=RenderTexture.active;
            try
            {
                if(!source.LoadImage(File.ReadAllBytes(entry.path))) throw new InvalidDataException(entry.path);
                var imported=AssetDatabase.LoadAssetAtPath<Texture2D>(entry.path);
                if(imported.width!=source.width || imported.height!=source.height) throw new InvalidOperationException("Изменён размер alpha-only: "+entry.path);
                target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                Graphics.Blit(imported,target);RenderTexture.active=target;
                copy=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
                copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();
                var expected=source.GetPixels32();var actual=copy.GetPixels32();
                for(int i=0;i<expected.Length;i++) if(expected[i].a!=actual[i].a)
                    throw new InvalidOperationException($"Потеря alpha {entry.path}: пиксель {i}, {expected[i].a} → {actual[i].a}.");
                Debug.Log($"Маска alpha-only: точное совпадение {entry.path}, {expected.Length} пикселей.");
            }
            finally {RenderTexture.active=previous;if(target!=null) RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(source);if(copy!=null) UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static void ValidateBatch()
        {
            try
            {
                Validate(JsonConvert.DeserializeObject<TextureSizingPolicy.Table>(File.ReadAllText(TextureSizingPolicy.TablePath)));
                Debug.Log("Размеры текстур: таблица, формат и мировые размеры уменьшенных спрайтов проверены.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static HashSet<string> Reserved()
        {
            var result=new HashSet<string>(StringComparer.Ordinal);
            foreach(string relative in new[]{VectorArtworkReserveManifest.WeaponPath,VectorArtworkReserveManifest.HudPath,ItemHubVectorReserveAuthoring.ManifestPath,"Tools/Research/PonyVectorReserve63/manifest.json"})
            {
                string path=VectorArtworkReserveManifest.RepositoryPath(relative);
                if(!File.Exists(path)) throw new FileNotFoundException("Нет манифеста точного импорта",path);
                foreach(var entry in JObject.Parse(File.ReadAllText(path))["entries"]??throw new InvalidDataException(path))
                {
                    if(entry["texturePath"]!=null) result.Add((string)entry["texturePath"]);
                    foreach(var destination in entry["destinations"]??new JArray()) result.Add((string)destination["texturePath"]);
                }
            }
            return result;
        }
        private static Dictionary<string,string> ExactTileArt(out HashSet<string> alphaOnly)
        {
            alphaOnly=new HashSet<string>(StringComparer.Ordinal);
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            var manifest=TileManifest.Read(File.ReadAllText(RoomTileArtPostprocessor.ManifestPath),RoomTileArtPostprocessor.ManifestPath);
            var lighting=JObject.Parse(File.ReadAllText("Assets/Remains/Rooms/Original/room-lighting.json"));
            var lamps=(JObject)lighting["lamps"];
            var definitions=JObject.Parse(File.ReadAllText("Assets/Remains/Rooms/Original/definitions.json"));
            var emissionArt=new HashSet<string>(definitions["definitions"].Where(d=>lamps?.Property((string)d["id"])!=null)
                .Select(d=>(string)d["art"]).Where(a=>a!=null),StringComparer.Ordinal);
            foreach(var art in manifest.Art.Values)
            {
                string reason=art.Id.StartsWith("mask:",StringComparison.Ordinal) || art.Id.StartsWith("decor-erase:",StringComparison.Ordinal)?
                    "Маска terrain/erase: точность alpha, без потерь.":null;
                if(art.Id.StartsWith("decor-erase:",StringComparison.Ordinal))
                {
                    alphaOnly.Add(RoomTileArtPostprocessor.Root+art.File);
                    reason="EraseMask.shader читает только .a: Alpha8 без потерь, исходный размер и mipmaps сохранены; проверяется каждый alpha-байт.";
                }
                if(art.Id.StartsWith("decor:",StringComparison.Ordinal))
                {
                    string name=art.Id.Substring(6);int last=name.LastIndexOf(':');if(last>=0) name=name.Substring(0,last);
                    if(emissionArt.Contains(name)) reason="Эмиссия лампы: пороговый GPU-силуэт сохранён без потерь.";
                }
                if(reason!=null) result[RoomTileArtPostprocessor.Root+art.File]=reason;
            }
            return result;
        }
    }
}
