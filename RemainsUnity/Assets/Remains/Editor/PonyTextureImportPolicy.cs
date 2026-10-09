using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Import policy of the base pony part textures (Resources/Original/Pony/*.png, not subfolders):
    // vector parts exported with a reserve (4×, six shapes 8×) are shown smaller than their pixels, so
    // they need mipmaps (sharp Kaiser filter keeps small details such as the PipBuck readable) and edge
    // colour dilation (alphaIsTransparency) — without it filtering pulls the transparent black into
    // every part's edge and the joints show seams. Compressed (BC7 on desktop). One rule, checked by
    // PonyVectorReserveAuthoring too. Wearables and head styles keep their own policies.
    public sealed class PonyTextureImportPolicy : AssetPostprocessor
    {
        public const string Root="Assets/Remains/Resources/Original/Pony/";

        public static bool Applies(string assetPath)=>assetPath.StartsWith(Root,StringComparison.Ordinal) &&
            assetPath.EndsWith(".png",StringComparison.OrdinalIgnoreCase) && assetPath.IndexOf('/',Root.Length)<0;

        private void OnPreprocessTexture()
        {
            if(!Applies(assetPath)) return;
            var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Default;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=true;
            importer.mipmapFilter=TextureImporterMipFilter.KaiserFilter;
            importer.filterMode=FilterMode.Bilinear;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.maxTextureSize=2048;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;
        }

        public static bool Complies(TextureImporter importer,int requiredSize)=>importer!=null &&
            importer.textureType==TextureImporterType.Default && importer.npotScale==TextureImporterNPOTScale.None &&
            importer.alphaIsTransparency && importer.mipmapEnabled && importer.mipmapFilter==TextureImporterMipFilter.KaiserFilter &&
            importer.textureCompression==TextureImporterCompression.CompressedHQ && importer.maxTextureSize>=requiredSize;

        // Explicit authoring step: reimport every base pony texture so the policy reaches files whose
        // pixels did not change.
        //   -executeMethod Remains.Editor.PonyTextureImportPolicy.ReimportAll
        [MenuItem("Remains/Пони/Применить правило импорта текстур")]
        public static void ReimportAll()
        {
            int count=0;
            foreach(string file in Directory.GetFiles(Root,"*.png",SearchOption.TopDirectoryOnly))
            {
                string path=file.Replace('\\','/');
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);count++;
            }
            Debug.Log($"Текстуры пони переимпортированы по правилу: {count}.");
        }
    }
}
