using System;
using System.IO;
using System.Text;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Import settings for exported terrain art come from tiles-manifest.json, never from
    // guesses: vector masks are 8x sprites (PPU = 50 px/m × zoom) pivoted at their SWF
    // registration; bitmaps stay at native size (no NPOT rescale, no size cap below source).
    public sealed class RoomTileArtPostprocessor : AssetPostprocessor
    {
        public const string Root="Assets/Remains/Rooms/Original/Tiles/";
        public const string ManifestPath=Root+"tiles-manifest.json";
        public const float SourcePixelsPerMeter=50;

        private void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith(Root,StringComparison.Ordinal)) return;
            context.DependsOnSourceAsset(ManifestPath);
            var manifest=LoadManifest();
            string relative=assetPath.Substring(Root.Length);
            TileArt art=null;
            foreach(var entry in manifest.Art.Values) if(entry.File==relative) {art=entry;break;}
            if(art==null) {context.LogImportError($"Графика клеток {assetPath}: нет записи в {ManifestPath}.");return;}

            var importer=(TextureImporter)assetImporter;
            importer.mipmapEnabled=true;
            importer.alphaIsTransparency=true;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=Mathf.Max(2048,Mathf.NextPowerOfTwo(Mathf.Max(art.Width,art.Height)));
            importer.filterMode=FilterMode.Bilinear;
            if(art.Id.StartsWith("texture:",StringComparison.Ordinal))
            {
                importer.textureType=TextureImporterType.Default;
                importer.wrapMode=TextureWrapMode.Repeat;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;
                return;
            }
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit=(float)(SourcePixelsPerMeter*art.Zoom);
            var settings=new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Custom;
            settings.spriteMeshType=SpriteMeshType.FullRect;
            settings.spriteExtrude=0;
            // Registration point: source pixels from the canvas top-left -> normalised pivot from bottom-left.
            settings.spritePivot=new Vector2((float)(art.OriginX*art.Zoom/art.Width),(float)(1-art.OriginY*art.Zoom/art.Height));
            importer.SetTextureSettings(settings);
        }

        private static TileManifest LoadManifest()
        {
            string full=Path.Combine(Path.GetDirectoryName(Application.dataPath)??"",ManifestPath);
            return TileManifest.Read(File.ReadAllText(full,Encoding.UTF8),ManifestPath);
        }
    }
}
