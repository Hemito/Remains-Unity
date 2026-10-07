using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Remains.Editor
{
    [Serializable] public sealed class ReserveManifest
    {
        public int schema;
        public ReserveEntry[] entries;
    }
    [Serializable] public sealed class ReserveEntry
    {
        public string id,texturePath,productionBeforeSha256,textureGuid,sourceKind,rawPng,png,pngSha256;
        public int pixelWidth,pixelHeight,requiredMaxTextureSize;
        public float exportZoom;
        public float[] sourceBoundsPixelsYDown,sourceOriginPixelsYDown;
        public ReserveSprite[] sprites;
    }
    [Serializable] public sealed class ReserveSprite
    {
        public string assetPath,guid;
        public long localId;
        public float priorPixelsPerUnit,pixelsPerUnit;
        public float[] priorRect,priorPivotPixelsBottomLeft,priorBorder,worldBounds,rect,pivotPixelsBottomLeft,border;
    }
    // One declared canvas/registration contract is shared by authoring and native checks.
    // Neither gameplay nor per-frame presentation reads this authoring-only manifest.
    public static class VectorArtworkReserveManifest
    {
        public const string WeaponPath="Tools/Research/VectorArtworkReserve/weapon-manifest.json";
        public const string HudPath="Tools/Research/VectorArtworkReserve/hud-manifest.json";
        public static string RepositoryPath(string relative)
        {
            Require(!string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative),"Требуется относительный путь ресурса: "+relative);
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string path=Path.GetFullPath(Path.Combine(root,relative));
            Require(path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Ресурс вышел за проект: "+relative);
            return path;
        }
        public static ReserveManifest Load(string relative)
        {
            string path=RepositoryPath(relative);
            Require(File.Exists(path),"Нет манифеста экспорта: "+relative);
            var manifest=JsonUtility.FromJson<ReserveManifest>(File.ReadAllText(path));
            Require(manifest?.schema==1 && manifest.entries?.Length>0,"Повреждён манифест экспорта: "+relative);
            Require(manifest.entries.All(e=>e!=null && !string.IsNullOrWhiteSpace(e.id)) &&
                manifest.entries.Select(e=>e.id).Distinct(StringComparer.Ordinal).Count()==manifest.entries.Length,"Повтор/пустой ID экспорта: "+relative);
            foreach(var entry in manifest.entries) Validate(entry);
            return manifest;
        }
        public static ReserveEntry[] Entries()=>Load(HudPath).entries.Concat(Load(WeaponPath).entries).ToArray();
        public static ReserveEntry Find(string id)
        {
            var selected=Entries().Where(e=>e.id==id).ToArray();
            Require(selected.Length==1,"Нет единственной декларации экспорта: id="+id);
            return selected[0];
        }
        public static void Validate(ReserveEntry entry)
        {
            string context="id="+entry.id+", texture="+entry.texturePath;
            Require(entry.sourceKind=="true-vector" && entry.exportZoom==8,"Экспорт не является векторным8×: "+context);
            Require(entry.texturePath?.StartsWith("Assets/Remains/Resources/Original/",StringComparison.Ordinal)==true,
                "Неизвестный путь текстуры: "+context);
            Require(entry.pixelWidth>0 && entry.pixelHeight>0 && entry.requiredMaxTextureSize>=Math.Max(entry.pixelWidth,entry.pixelHeight),
                "Экспорт не помещается в явно заданный импорт: "+context);
            Require(entry.requiredMaxTextureSize>=32 && entry.requiredMaxTextureSize<=16384 &&
                (entry.requiredMaxTextureSize&(entry.requiredMaxTextureSize-1))==0,"Неверный importer cap: "+context);
            Require(IsHash(entry.productionBeforeSha256) && IsHash(entry.pngSha256) && IsGuid(entry.textureGuid),"Отсутствует отпечаток/GUID: "+context);
            Array(entry.sourceBoundsPixelsYDown,4,context+" source bounds");Array(entry.sourceOriginPixelsYDown,2,context+" source origin");
            Require(entry.sourceBoundsPixelsYDown[2]>entry.sourceBoundsPixelsYDown[0] && entry.sourceBoundsPixelsYDown[3]>entry.sourceBoundsPixelsYDown[1],
                "Пустые/перевёрнутые границы источника: "+context);
            Require(entry.sprites?.Length>0,"Нет явной регистрации Sprite: "+context);
            foreach(var sprite in entry.sprites)
            {
                Require(sprite!=null && sprite.assetPath?.StartsWith("Assets/Remains/",StringComparison.Ordinal)==true && IsGuid(sprite.guid) && sprite.localId!=0,
                    "Нет сохранённой Sprite-ссылки: "+context);
                Array(sprite.priorRect,4,context+" prior rect");Array(sprite.rect,4,context+" rect");
                Array(sprite.priorPivotPixelsBottomLeft,2,context+" prior pivot");Array(sprite.pivotPixelsBottomLeft,2,context+" pivot");
                Array(sprite.priorBorder,4,context+" prior border");Array(sprite.border,4,context+" border");Array(sprite.worldBounds,4,context+" world bounds");
                Require(float.IsFinite(sprite.priorPixelsPerUnit) && sprite.priorPixelsPerUnit>0 && float.IsFinite(sprite.pixelsPerUnit) && sprite.pixelsPerUnit>0 &&
                    sprite.rect[2]>0 && sprite.rect[3]>0 && sprite.priorRect[2]>0 && sprite.priorRect[3]>0,"Неверная плотность/область Sprite: "+context);
                Require(sprite.rect[0]>=0 && sprite.rect[1]>=0 && sprite.rect[0]+sprite.rect[2]<=entry.pixelWidth &&
                    sprite.rect[1]+sprite.rect[3]<=entry.pixelHeight,"Sprite выходит за экспортный canvas: "+context);
                for(int i=0;i<4;i++) Require(sprite.border[i]>=0 && sprite.priorBorder[i]>=0,"Отрицательная граница Sprite: "+context);
                float[] next={-sprite.pivotPixelsBottomLeft[0]/sprite.pixelsPerUnit,-sprite.pivotPixelsBottomLeft[1]/sprite.pixelsPerUnit,
                    (sprite.rect[2]-sprite.pivotPixelsBottomLeft[0])/sprite.pixelsPerUnit,(sprite.rect[3]-sprite.pivotPixelsBottomLeft[1])/sprite.pixelsPerUnit};
                float[] prior={-sprite.priorPivotPixelsBottomLeft[0]/sprite.priorPixelsPerUnit,-sprite.priorPivotPixelsBottomLeft[1]/sprite.priorPixelsPerUnit,
                    (sprite.priorRect[2]-sprite.priorPivotPixelsBottomLeft[0])/sprite.priorPixelsPerUnit,(sprite.priorRect[3]-sprite.priorPivotPixelsBottomLeft[1])/sprite.priorPixelsPerUnit};
                for(int i=0;i<4;i++) Require(Mathf.Abs(next[i]-sprite.worldBounds[i])<=.000001f && Mathf.Abs(prior[i]-sprite.worldBounds[i])<=.000001f,
                    "Экспорт меняет мировой размер/привязку: "+context+", edge="+i);
            }
        }
        public static void VerifyStaged(ReserveEntry entry)
        {
            Require(Hash(RepositoryPath(entry.png))==entry.pngSha256,"Изменён подготовленный PNG: id="+entry.id+", path="+entry.png);
        }
        public static string Hash(string path)
        {
            Require(File.Exists(path),"Нет файла ресурса: "+path);
            using var algorithm=SHA256.Create();using var stream=File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        private static bool IsHash(string value)=>value?.Length==64 && value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        private static bool IsGuid(string value)=>value?.Length==32 && value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        private static void Array(float[] values,int count,string context)
        {Require(values?.Length==count && values.All(float.IsFinite),"Нет конечной геометрии: "+context);}
        public static void Require(bool condition,string context)
        {if(!condition) throw new InvalidOperationException("Качество векторных ресурсов: "+context);}
    }
}
