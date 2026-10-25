using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Content.VectorArt;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class CatalogSpriteAuthoring
    {
        public const string Textures="Assets/Remains/ImportedContent/Art/",Lazy="Assets/Remains/Resources/Imported/Art/",
            EditorLazy=CatalogArtBuildSplit.EditorFolder;
        private static HashSet<string> covered;
        private static Dictionary<string,string> reasons;
        public static void ResetCoverage() {covered=null;reasons=null;}
        public static string Key(Sprite sprite)
        {
            string path=AssetDatabase.GetAssetPath(sprite);
            if(!IsCanonical(path))
                path=AssetDatabase.GetAssetPath(sprite.texture);
            return IsCanonical(path)
                ?"Imported/Art/"+Path.GetFileNameWithoutExtension(path):null;
        }
        public static CatalogSpriteReference Reference(Sprite sprite)
        {
            string key=Key(sprite);
            string path=AssetDatabase.GetAssetPath(sprite);
            bool reframe=key!=null && !IsCanonical(path);
            return CatalogSpriteReference.FromSprite(sprite,key,key==null?null:RasterReason(key),reframe);
        }
        private static bool IsCanonical(string path)=>path.StartsWith(Textures,StringComparison.Ordinal) ||
            path.StartsWith(Lazy,StringComparison.Ordinal) || path.StartsWith(EditorLazy,StringComparison.Ordinal);
        public static string RasterReason(string key)
        {
            if(covered==null)
            {
                var package=VectorArtPackage.Load(VectorArtPackaging.Destination);
                var catalog=ExportedContentCatalog.Load();
                using var library=new VectorArtLibrary(package,catalog,1);
                covered=new HashSet<string>(library.Keys,StringComparer.Ordinal);
                reasons=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var resource in catalog.resources)
                    foreach(var frame in resource.frames??Array.Empty<ExportedFrame>())
                    {
                        if(string.IsNullOrEmpty(frame.spriteResource) || covered.Contains(frame.spriteResource)) continue;
                        string reason=package.RasterResources.TryGetValue(resource.id,out var explicitReason)?explicitReason:
                            frame.zoom!=VectorArtLibrary.SourceZoom?"исходный растр 1×: "+resource.id:
                            "нет подтверждённого векторного кадра/регистрации: "+resource.id+", кадр "+frame.frame;
                        reasons.TryAdd(frame.spriteResource,reason);
                    }
            }
            if(covered.Contains(key)) return null;
            return reasons.TryGetValue(key,out var result)?result:throw new InvalidOperationException("Ключ отсутствует в каталоге: "+key);
        }
    }
}
