using System;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // One editor-only coverage/UV/material bake for windows and concrete cutouts.
    // Regions retain their full source canvas; only destination atlas bounds clip.
    public static class RoomArtworkVisibilityBake
    {
        public const int PixelsPerUnit=50;
        public static Color32[] Pixels(Rect area,WindowMaskRegion[] regions,out int width,out int height)
        {
            if(regions==null || regions.Length==0)throw new InvalidOperationException("Маска оформления: пустые исходные записи.");
            var sources=new Coverage[regions.Length];
            for(int i=0;i<regions.Length;i++)
            {
                var region=regions[i];
                if(region?.coverage==null || !region.coverage.isReadable)
                    throw new InvalidOperationException("Маска оформления: нет читаемого покрытия, record="+region?.sourceRecord);
                sources[i]=new Coverage(region.sourceRecord,region.bakedBounds,region.coverage.width,region.coverage.height,region.coverage.GetPixels32());
            }
            return Pixels(area,sources,out width,out height);
        }
        public readonly struct Coverage
        {
            public readonly string Record;
            public readonly Rect Bounds;
            public readonly int Width,Height;
            public readonly Color32[] Pixels;
            public Coverage(string record,Rect bounds,int width,int height,Color32[] pixels)
            {Record=record;Bounds=bounds;Width=width;Height=height;Pixels=pixels;}
        }
        // Pure array operation: native texture reads happen only in the explicit capture above.
        public static Color32[] Pixels(Rect area,Coverage[] regions,out int width,out int height)
        {
            if(!Finite(area) || area.width<=0 || area.height<=0 || regions==null || regions.Length==0)
                throw new InvalidOperationException("Маска оформления: неверные границы или пустые исходные записи.");
            width=Mathf.RoundToInt(area.width*PixelsPerUnit);height=Mathf.RoundToInt(area.height*PixelsPerUnit);
            if(width<=0 || height<=0 || width>4096 || height>4096)
                throw new InvalidOperationException("Маска оформления: слишком большие или пустые границы.");
            var pixels=Enumerable.Repeat(new Color32(255,255,255,255),checked(width*height)).ToArray();
            foreach(var region in regions)
            {
                if(string.IsNullOrWhiteSpace(region.Record) || region.Pixels==null || region.Width<=0 || region.Height<=0 || region.Pixels.LongLength!=(long)region.Width*region.Height)
                    throw new InvalidOperationException("Маска оформления: неполное исходное покрытие, record="+region.Record);
                var r=region.Bounds;
                if(!Finite(r) || r.width<=0 || r.height<=0 || r.xMax<=area.xMin || r.xMin>=area.xMax || r.yMax<=area.yMin || r.yMin>=area.yMax)
                    throw new InvalidOperationException("Маска оформления: исходный холст не пересекает комнату, record="+region.Record);
                int left=Mathf.Max(0,Mathf.FloorToInt((r.xMin-area.xMin)*PixelsPerUnit));
                int right=Mathf.Min(width,Mathf.CeilToInt((r.xMax-area.xMin)*PixelsPerUnit));
                int bottom=Mathf.Max(0,Mathf.FloorToInt((r.yMin-area.yMin)*PixelsPerUnit));
                int top=Mathf.Min(height,Mathf.CeilToInt((r.yMax-area.yMin)*PixelsPerUnit));
                var source=region.Pixels;int sourceWidth=region.Width,sourceHeight=region.Height;
                for(int y=bottom;y<top;y++) for(int x=left;x<right;x++)
                {
                    float u=(area.x+(x+.5f)/PixelsPerUnit-r.x)/r.width,v=(area.y+(y+.5f)/PixelsPerUnit-r.y)/r.height;
                    if(u<0 || u>1 || v<0 || v>1) continue;
                    float coverage=SampleCoverage(source,sourceWidth,sourceHeight,u,v);
                    byte value=(byte)Mathf.RoundToInt(pixels[y*width+x].r*(1-coverage));
                    pixels[y*width+x]=new Color32(value,value,value,255);
                }
            }
            return pixels;
        }
        public static Color32[] Pixels(RoomWindowMaskAuthoring component,out int width,out int height)=>Pixels(component.Bounds,component.Regions,out width,out height);
        public static void VerifyPixels(RoomWindowMaskAuthoring component)
        {
            var expected=Pixels(component,out int width,out int height);var actual=component.Visibility;
            if(actual==null || !actual.isReadable || actual.width!=width || actual.height!=height || !actual.GetPixels32().SequenceEqual(expected))
                throw new InvalidOperationException("Сохранённая маска оформления не соответствует source regions: room="+component.name+". Нужна явная пересборка.");
        }
        public static void Bake(RoomWindowMaskAuthoring component,string folder)
        {
            var room=component.GetComponent<RoomAuthoring>()??throw new InvalidOperationException("Маска оформления: нет RoomAuthoring.");
            if(!AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Маска оформления: каталог не подготовлен, path="+folder);
            foreach(var region in component.Regions)
            {
                var a=region.artwork?.Read(room)??throw new InvalidOperationException("Маска оформления: нет рисунка, record="+region.sourceRecord);
                region.bakedBounds=new Rect(a.x,a.y,a.width,a.height);
            }
            var pixels=Pixels(component,out int width,out int height);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Remains/Resources/Presentation/WorldLit.shader")??throw new InvalidOperationException("Маска оформления: нет WorldLit shader.");
            // Preserve the current mesh/material asset identity, including the first
            // warehouse bake. Creating replacements would lose manual GUID bindings.
            var meshPaths=new string[component.Layers.Length];var materialPaths=new string[component.Layers.Length];
            for(int index=0;index<component.Layers.Length;index++)
            {
                var renderer=component.Layers[index];var filter=renderer?.GetComponent<MeshFilter>();
                if(filter?.sharedMesh==null || renderer.sharedMaterial==null || renderer.sharedMaterial.shader!=shader)
                    throw new InvalidOperationException("Маска оформления: неполный layer/shader, index="+index);
                meshPaths[index]=AssetDatabase.GetAssetPath(filter.sharedMesh);materialPaths[index]=AssetDatabase.GetAssetPath(renderer.sharedMaterial);
                if(string.IsNullOrEmpty(meshPaths[index]) || string.IsNullOrEmpty(materialPaths[index]) ||
                    meshPaths.Take(index).Contains(meshPaths[index]) || materialPaths.Take(index).Contains(materialPaths[index]))
                    throw new InvalidOperationException("Маска оформления: слой не имеет отдельных сохранённых mesh/material, index="+index);
            }
            var generated=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            string path=folder+"/visibility.png";
            try {generated.SetPixels32(pixels);generated.Apply();File.WriteAllBytes(path,generated.EncodeToPNG());}
            finally {UnityEngine.Object.DestroyImmediate(generated);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;
            importer.isReadable=true;importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.alphaIsTransparency=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=4096;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path)??throw new InvalidOperationException("Маска оформления: atlas не импортирован, path="+path);
            var bounds=component.Bounds;
            for(int index=0;index<component.Layers.Length;index++)
            {
                var renderer=component.Layers[index];var filter=renderer.GetComponent<MeshFilter>();
                var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name=filter.sharedMesh.name;var vertices=mesh.vertices;var uv=new Vector2[vertices.Length];
                var matrix=room.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                for(int i=0;i<vertices.Length;i++) {var p=matrix.MultiplyPoint3x4(vertices[i]);uv[i]=new Vector2((p.x-bounds.x)/bounds.width,(p.y-bounds.y)/bounds.height);}
                mesh.uv2=uv;var material=new Material(renderer.sharedMaterial);material.name=renderer.sharedMaterial.name;material.shader=shader;
                material.SetTexture("_ArtworkVisibility",texture);material.SetFloat("_ArtworkVisibilityEnabled",1);
                filter.sharedMesh=Save(mesh,meshPaths[index]);renderer.sharedMaterial=Save(material,materialPaths[index]);
            }
            component.Configure(texture,bounds,component.Regions,component.Layers);EditorUtility.SetDirty(component);
        }
        private static float SampleCoverage(Color32[] pixels,int width,int height,float u,float v)
        {
            float x=Mathf.Clamp(u*width-.5f,0,width-1),y=Mathf.Clamp(v*height-.5f,0,height-1);
            int x0=Mathf.FloorToInt(x),y0=Mathf.FloorToInt(y),x1=Mathf.Min(x0+1,width-1),y1=Mathf.Min(y0+1,height-1);
            float lower=Mathf.Lerp(pixels[y0*width+x0].r,pixels[y0*width+x1].r,x-x0);
            float upper=Mathf.Lerp(pixels[y1*width+x0].r,pixels[y1*width+x1].r,x-x0);
            return Mathf.Lerp(lower,upper,y-y0)/255;
        }
        private static bool Finite(Rect rect)=>float.IsFinite(rect.x)&&float.IsFinite(rect.y)&&float.IsFinite(rect.width)&&float.IsFinite(rect.height);
        private static T Save<T>(T value,string path) where T:UnityEngine.Object
        {
            var old=AssetDatabase.LoadAssetAtPath<T>(path);
            if(old==null) {AssetDatabase.CreateAsset(value,path);return value;}
            value.name=old.name;EditorUtility.CopySerialized(value,old);EditorUtility.SetDirty(old);UnityEngine.Object.DestroyImmediate(value);return old;
        }
    }
}
