using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit editor-only bindings; RoomArtworkVisibilityBake is the only compositor.
    public static class FactoryContinuationArtwork
    {
        public const string MaskFolder="Assets/Remains/RoomDrafts/FactoryContinuation27WindowMasks";
        public const string CoveragePath="Assets/Remains/Resources/Original/Room/Factory27R60/window-mask-bwindow-1.png";
        public static RoomArtworkAuthoring Find(RoomAuthoring room,RoomArtwork expected)
        {
            var found=room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(a=>Same(a.Read(room),expected)).ToArray();
            FactoryContinuationSource.Require(found.Length==1,"Нужен единственный authored artwork, texture="+expected.texture+", x="+expected.x+", y="+expected.y+", count="+found.Length);
            return found[0];
        }
        public static bool Same(RoomArtwork a,RoomArtwork b)=>a.texture==b.texture && a.order==b.order && a.repeat==b.repeat && a.stretchY==b.stretchY &&
            a.backgroundLighting==b.backgroundLighting && a.selfIlluminated==b.selfIlluminated && a.slopeDirection==b.slopeDirection &&
            Mathf.Abs(a.x-b.x)<.00001f && Mathf.Abs(a.y-b.y)<.00001f && Mathf.Abs(a.width-b.width)<.00001f && Mathf.Abs(a.height-b.height)<.00001f && Mathf.Abs(a.shade-b.shade)<.00001f;
        public static void ApplyAlpha(RoomAuthoring room,FactoryContinuationSource.Data data)
        {
            foreach(var placement in data.manifest.decorPlacements)
            {
                var renderer=Find(room,data.decor.artwork[placement.artworkIndex]).GetComponent<MeshRenderer>();var material=renderer?.sharedMaterial;
                FactoryContinuationSource.Require(material!=null && material.HasProperty("_Color"),"Нет prepared diffuse material, record="+placement.recordId);
                var color=material.GetColor("_Color");color.a=placement.sourceAlpha;material.SetColor("_Color",color);
            }
        }
        public static void InstallWindows(GameObject root,FactoryContinuationSource.Data data)
        {
            var room=root.GetComponent<RoomAuthoring>();var current=root.GetComponent<RoomWindowMaskAuthoring>();
            if(current!=null) {current.Validate();RoomArtworkVisibilityBake.VerifyPixels(current);return;}
            var importer=AssetImporter.GetAtPath(CoveragePath) as TextureImporter;
            FactoryContinuationSource.Require(importer!=null,"Не импортирована native window coverage.");
            importer.textureType=TextureImporterType.Default;importer.isReadable=true;importer.sRGBTexture=false;importer.alphaIsTransparency=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=2048;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            var coverage=FactoryContinuationSource.Asset<Texture2D>(CoveragePath);
            FactoryContinuationSource.Require(coverage.isReadable && coverage.width==160 && coverage.height==80,"Native coverage потеряла160×80/readable.");
            var regions=data.manifest.windows.Select(w=>new WindowMaskRegion {sourceRecord=w.recordId,coverage=coverage,artwork=Find(room,data.decor.artwork[w.diffuseArtworkIndex])}).ToArray();
            var windows=regions.Select(r=>r.artwork).ToArray();
            var layers=room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(a=>a.Read(room).order==-29 || windows.Contains(a)).Select(a=>a.GetComponent<MeshRenderer>()).ToArray();
            FactoryContinuationSource.Require(regions.Length==5 && layers.Length==data.geometry.artwork.Count(a=>a.order==-29)+5,"Изменён exact window/background mask набор.");
            FactoryContinuationSource.Require(!AssetDatabase.IsValidFolder(MaskFolder),"Не заменён существующий неполный/ручной mask folder.");
            AssetDatabase.CreateFolder("Assets/Remains/RoomDrafts","FactoryContinuation27WindowMasks");
            current=root.AddComponent<RoomWindowMaskAuthoring>();current.Configure(null,new Rect(0,0,38.4f,20),regions,layers);
            RoomArtworkVisibilityBake.Bake(current,MaskFolder);current.Validate();
        }
        public static void Check(RoomAuthoring room,FactoryContinuationSource.Data data)
        {
            foreach(var placement in data.manifest.decorPlacements)
            {
                var artwork=Find(room,data.decor.artwork[placement.artworkIndex]);var material=artwork.GetComponent<MeshRenderer>().sharedMaterial;
                FactoryContinuationSource.Require(material!=null && material.HasProperty("_Color") && material.GetColor("_Color").a==placement.sourceAlpha,
                    "Source alpha потеряна/применена дважды, record="+placement.recordId+", expected="+placement.sourceAlpha);
            }
            var mask=room.GetComponent<RoomWindowMaskAuthoring>();FactoryContinuationSource.Require(mask!=null && mask.Regions.Length==5,"Нет native пяти window masks.");
            var ids=data.manifest.windows.Select(w=>w.recordId).OrderBy(id=>id,StringComparer.Ordinal);
            FactoryContinuationSource.Require(ids.SequenceEqual(mask.Regions.Select(r=>r.sourceRecord).OrderBy(id=>id,StringComparer.Ordinal)),"Изменены source mask IDs.");
            var expected=room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(a=>a.Read(room).order==-29 || mask.Regions.Any(r=>r.artwork==a)).Select(a=>a.GetComponent<MeshRenderer>()).ToArray();
            FactoryContinuationSource.Require(mask.Layers.Length==expected.Length && expected.All(mask.Layers.Contains),"Потерян exact mask layer binding; backdrop−40 не вырезается.");
            mask.Validate();RoomArtworkVisibilityBake.VerifyPixels(mask);
            var coverage=FactoryContinuationSource.Asset<Texture2D>(CoveragePath);var importer=(TextureImporter)AssetImporter.GetAtPath(CoveragePath);
            FactoryContinuationSource.Require(coverage.width==160 && coverage.height==80 && coverage.isReadable && !importer.sRGBTexture && !importer.alphaIsTransparency && !importer.mipmapEnabled && importer.textureCompression==TextureImporterCompression.Uncompressed,
                "Native mask import policy изменена.");
        }
    }
}
