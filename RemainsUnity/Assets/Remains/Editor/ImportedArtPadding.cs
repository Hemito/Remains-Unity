using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Block compression (BC7) applies only to textures whose sides are multiples of 4; Unity keeps any other texture
    // uncompressed without a warning (2252 of 2754 imported art textures, 4.95 GiB of the 2026-10-24 build). Imported art
    // is therefore installed as a copy padded with transparent pixels on the right and at the bottom: the registration
    // point (measured from the top-left) stays in place and the sprite keeps its file ID. Provenance: the importer's
    // userData keeps the source signature and adds the original size and the SHA-256 of the padded copy; the padding is
    // deterministic, so a reinstall re-derives and compares it.
    public static class ImportedArtPadding
    {
        public const string ArtRoot="Assets/Remains/ImportedContent/Art";
        public const string Marker="|padded:";
        public static bool Needs(int width,int height)=>width%4!=0 || height%4!=0;
        public static int Up(int value)=>(value+3)&~3;

        // The padded PNG of an 8-bit RGBA source, or null when it already has multiple-of-4 sides.
        public static byte[] Pad(byte[] png,out int width,out int height)
        {
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
            Texture2D target=null;
            try
            {
                if(!source.LoadImage(png,false)) throw new InvalidDataException("Не удалось прочитать PNG для дополнения до кратного 4.");
                width=source.width;height=source.height;
                if(!Needs(width,height)) return null;
                int paddedWidth=Up(width),paddedHeight=Up(height);
                target=new Texture2D(paddedWidth,paddedHeight,TextureFormat.RGBA32,false);
                target.SetPixels32(new Color32[paddedWidth*paddedHeight]);
                // Texture rows go up from the bottom: the source's top row lands on the padded top row.
                target.SetPixels32(0,paddedHeight-height,width,height,source.GetPixels32());
                target.Apply(false);
                return target.EncodeToPNG();
            }
            finally {UnityEngine.Object.DestroyImmediate(source);if(target!=null) UnityEngine.Object.DestroyImmediate(target);}
        }
        // The pivot of a registration point (pixels from the top-left of the source, at zoom) in a texture of this size.
        public static Vector2 Pivot(float originX,float originY,float zoom,int textureWidth,int textureHeight)
            =>new(originX*zoom/textureWidth,1-originY*zoom/textureHeight);
        public static string UserData(string signature,int width,int height,string paddedHash)
            =>"ExportedContent:"+signature+Marker+width+"x"+height+":"+paddedHash;
        // (signature, padded hash or null) of an imported art importer's userData.
        public static (string Signature,string PaddedHash) Parse(string userData)
        {
            if(string.IsNullOrEmpty(userData) || !userData.StartsWith("ExportedContent:",StringComparison.Ordinal)) return (null,null);
            string rest=userData.Substring("ExportedContent:".Length);
            int at=rest.IndexOf(Marker,StringComparison.Ordinal);
            if(at<0) return (rest,null);
            string tail=rest.Substring(at+Marker.Length);int colon=tail.IndexOf(':');
            return (rest.Substring(0,at),colon<0?null:tail.Substring(colon+1));
        }
        public static string Hash(byte[] bytes) {using var sha=SHA256.Create();return string.Concat(sha.ComputeHash(bytes).Select(b=>b.ToString("x2")));}

        // One-time migration of the installed art (the content package is not needed: each file is checked against the
        // SHA-256 in its signature before it is replaced). Then the sizing table gets the new sizes, the textures are
        // reimported in one pass, the lazy sprite copies are refreshed in place (same GUID) and the actual formats are
        // audited.
        public static void MigrateBatch()
        {
            try {Migrate();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Migrate()
        {
            var migrated=new List<(string Path,string Signature,int Width,int Height,string Hash)>();
            int already=0,aligned=0;
            foreach(string path in Directory.GetFiles(ArtRoot,"*.png").Select(p=>p.Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal))
            {
                var importer=AssetImporter.GetAtPath(path) as TextureImporter??throw new InvalidDataException("Нет TextureImporter: "+path);
                var (signature,paddedHash)=Parse(importer.userData);
                if(signature==null) throw new InvalidDataException($"Картинка {path} без подписи происхождения импорта.");
                byte[] bytes=File.ReadAllBytes(path);
                if(paddedHash!=null)
                {
                    if(Hash(bytes)!=paddedHash) throw new InvalidDataException($"Дополненная картинка {path} изменена после дополнения.");
                    already++;continue;
                }
                string sha=signature.Split('|')[0];
                if(Hash(bytes)!=sha) throw new InvalidDataException($"Картинка {path} не совпадает с исходным SHA-256 {sha}; дополнение отменено.");
                byte[] padded=Pad(bytes,out int width,out int height);
                if(padded==null) {aligned++;continue;}
                migrated.Add((path,signature,width,height,Hash(padded)));
                File.WriteAllBytes(path,padded);
            }
            if(migrated.Count>0)
            {
                UpdateSizingTable(migrated.ToDictionary(m=>m.Path,m=>(Up(m.Width),Up(m.Height)),StringComparer.Ordinal));
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach(var item in migrated)
                    {
                        var importer=(TextureImporter)AssetImporter.GetAtPath(item.Path);
                        var (originX,originY,zoom)=SignatureOrigin(item.Signature);
                        importer.spritePivot=Pivot(originX,originY,zoom,Up(item.Width),Up(item.Height));
                        importer.userData=UserData(item.Signature,item.Width,item.Height,item.Hash);
                        importer.SaveAndReimport();
                    }
                }
                finally {AssetDatabase.StopAssetEditing();}
                foreach(var item in migrated) RefreshLazySprite(item.Path);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"Дополнение картинок до кратного 4: дополнено {migrated.Count}, уже дополнено ранее {already}, кратные 4 без изменений {aligned}.");
            AuditFormats();
        }
        // Registration in an install signature "<sha>|originX|originY|zoom". The installer writes the origins with the Editor's
        // culture ("36,8" on a Russian system), so a decimal comma is a decimal point here (no thousands separators occur).
        public static (float X,float Y,float Zoom) SignatureOrigin(string signature)
        {
            var parts=signature.Split('|');
            if(parts.Length<4) throw new InvalidDataException("Подпись импорта без регистрации: "+signature);
            float Number(string text)=>float.Parse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture);
            return (Number(parts[1]),Number(parts[2]),Number(parts[3]));
        }

        // Repair of the 2026-10-24 migration, which read "36,8" as 368 and moved the registration of every padded sprite with a
        // fractional origin. The pivot is recomputed from the signature and must agree with the catalog's own origin.
        public static void RepairPivotsBatch()
        {
            try {RepairPivots();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static int RepairPivots()
        {
            var origins=new Dictionary<string,Vector2>(StringComparer.Ordinal);
            foreach(var entry in Remains.Content.ExportedContentCatalog.Load().resources)
                foreach(var frame in entry.frames??Array.Empty<Remains.Content.ExportedFrame>())
                    if(!string.IsNullOrEmpty(frame.spriteResource)) origins[Path.GetFileName(frame.spriteResource)]=frame.sourceOrigin;
            var repaired=new List<string>();int correct=0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(string path in Directory.GetFiles(ArtRoot,"*.png").Select(p=>p.Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal))
                {
                    var importer=AssetImporter.GetAtPath(path) as TextureImporter??throw new InvalidDataException("Нет TextureImporter: "+path);
                    var (signature,paddedHash)=Parse(importer.userData);
                    if(signature==null || paddedHash==null || signature.Split('|').Length<4) continue;
                    var (originX,originY,zoom)=SignatureOrigin(signature);
                    string name=Path.GetFileNameWithoutExtension(path);
                    if(origins.TryGetValue(name,out var catalog) && (Mathf.Abs(catalog.x-originX)>1e-3f || Mathf.Abs(catalog.y-originY)>1e-3f))
                        throw new InvalidDataException($"Регистрация {path}: подпись {originX},{originY}, каталог {catalog}.");
                    importer.GetSourceTextureWidthAndHeight(out int width,out int height);
                    var pivot=Pivot(originX,originY,zoom,width,height);
                    if((importer.spritePivot-pivot).sqrMagnitude<1e-10f) {correct++;continue;}
                    importer.spritePivot=pivot;
                    importer.SaveAndReimport();
                    repaired.Add(path);
                }
            }
            finally {AssetDatabase.StopAssetEditing();}
            foreach(string path in repaired) RefreshLazySprite(path);
            AssetDatabase.SaveAssets();
            Debug.Log($"Регистрация дополненных картинок: исправлено {repaired.Count}, уже верных {correct}.");
            return repaired.Count;
        }
        // The installed lazy sprite (Resources/Imported/Art/<name>.asset) takes the reimported sprite's geometry in place.
        public static void RefreshLazySprite(string texturePath)
        {
            string name=Path.GetFileNameWithoutExtension(texturePath);
            string lazyPath="Assets/Remains/Resources/Imported/Art/"+name+".asset";
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(texturePath)??throw new InvalidDataException("Спрайт не импортирован: "+texturePath);
            var lazy=AssetDatabase.LoadAssetAtPath<Sprite>(lazyPath);
            if(lazy==null) return;   // not every frame has a lazy copy
            EditorUtility.CopySerialized(sprite,lazy);
            lazy.name=name;EditorUtility.SetDirty(lazy);
        }
        private static void UpdateSizingTable(Dictionary<string,(int Width,int Height)> sizes)
        {
            string path=TextureSizingPolicy.TablePath;
            if(!File.Exists(path)) return;
            var table=JObject.Parse(File.ReadAllText(path));
            int updated=0;
            foreach(var entry in table["entries"] as JArray??throw new InvalidDataException("Таблица размеров текстур без entries."))
                if(sizes.TryGetValue((string)entry["path"],out var size)) {entry["sourceWidth"]=size.Width;entry["sourceHeight"]=size.Height;updated++;}
            File.WriteAllText(path,table.ToString(Newtonsoft.Json.Formatting.Indented));
            Debug.Log($"Таблица размеров текстур: обновлены размеры {updated} дополненных картинок.");
        }
        // The policy chooses formats; this reports textures whose imported format is not the chosen one (Unity's silent
        // uncompressed fallback for sides that are not multiples of 4, including after a maximum-size downscale).
        public static void AuditFormatsBatch()
        {
            try {AuditFormats();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static int AuditFormats()
        {
            int checkedCount=0;var fallback=new List<string>();
            foreach(var entry in TextureSizingPolicy.Entries())
            {
                if(entry.decision=="exception") continue;
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(entry.path);
                if(texture==null) continue;
                checkedCount++;
                var expected=entry.decision=="alpha-only"?TextureFormat.Alpha8:TextureFormat.BC7;
                if(texture.format!=expected) fallback.Add($"{entry.path}: {texture.width}×{texture.height}, формат {texture.format} вместо {expected}");
                Resources.UnloadAsset(texture);
            }
            foreach(string line in fallback.Take(50)) Debug.LogWarning("Текстура без выбранного сжатия: "+line);
            // The whole list for review (the log keeps the first 50).
            string report=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/texture-sizing/format-audit.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllLines(report,fallback);
            Debug.Log($"Проверка форматов текстур: проверено {checkedCount}, без выбранного сжатия {fallback.Count}"+(fallback.Count>50?" (первые 50 выше)":"")+".");
            return fallback.Count;
        }
    }
}
