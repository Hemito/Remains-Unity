using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Emitting and absorbing shapes of sprites on the light grid, measured once per sprite and texel size
    // (a GPU render and a synchronous readback; at level preparation, never per frame). The silhouette is the
    // lamp emission raster of the light scene shader (alpha above its cut), counted where it exceeds one half.
    public static class LightSpriteShapes
    {
        public sealed class Shape
        {
            public double Area;              // m², silhouette texels × texel²
            public Vector3[] Columns;        // up to 8 weighted column centroids (local sprite space): actor shadow sources
            public Vector3[] Strata;         // up to 8 weighted points of a 4×2 split, each on the mask: direct light rays
        }
        public sealed class Glow
        {
            public Color Color;              // mean colour of the glowing pixels
            public Vector3[] Points;         // up to 8 energy-weighted column centroids, sprite uv (0..1)
            public float AreaFraction;       // glowing share of the sprite rectangle
        }
        private const string SceneShaderResource="Presentation/LightScene";
        private static readonly Dictionary<(Sprite sprite,float texel),Shape> shapes=new();
        private static readonly Dictionary<(Sprite sprite,float threshold),Glow> glows=new();
        private static Material material;
        private static Mesh quad;
        public static int Readbacks {get;private set;}

        public static Shape Measure(Sprite sprite,float texel)
        {
            if(sprite==null) throw new ArgumentNullException(nameof(sprite));
            if(!(texel>0)) throw new ArgumentOutOfRangeException(nameof(texel));
            if(shapes.TryGetValue((sprite,texel),out var cached)) return cached;
            Remains.Diagnostics.FrameHitchLog.Gpu("Свет: форма спрайта (чтение с видеокарты)");
            Prepare();
            var bounds=sprite.bounds;
            int w=Mathf.CeilToInt(bounds.size.x/texel)+2,h=Mathf.CeilToInt(bounds.size.y/texel)+2;
            var target=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var copy=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true);
            var previous=RenderTexture.active;
            try
            {
                using(var cmd=new CommandBuffer{name="Форма спрайта для света"})
                {
                    cmd.SetRenderTarget(target);
                    cmd.ClearRenderTarget(false,true,Color.clear);
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(bounds.min.x-texel,bounds.min.x-texel+w*texel,bounds.min.y-texel,bounds.min.y-texel+h*texel,-1,1),false));
                    var block=new MaterialPropertyBlock();
                    block.SetTexture("_MainTex",sprite.texture);
                    block.SetColor("_Emission",Color.white);
                    cmd.DrawMesh(quad,Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),material,0,0,block);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                RenderTexture.active=target;
                copy.ReadPixels(new Rect(0,0,w,h),0,0);
                Readbacks++;
                var pixels=copy.GetPixels();
                int count=0;var columnSums=new Vector2[8];var columnCounts=new int[8];
                var strata=new List<Vector2>[8];var strataSums=new Vector2[8];
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(pixels[y*w+x].r>.5f)
                {
                    count++;var point=new Vector2(bounds.min.x-texel+(x+.5f)*texel,bounds.min.y-texel+(y+.5f)*texel);
                    int column=Mathf.Min(7,x*8/w);columnCounts[column]++;columnSums[column]+=point;
                    int stratum=Mathf.Min(3,x*4/w)+4*Mathf.Min(1,y*2/h);
                    (strata[stratum]??=new List<Vector2>()).Add(point);strataSums[stratum]+=point;
                }
                var columns=new List<Vector3>(8);
                for(int i=0;i<8;i++) if(columnCounts[i]>0)
                    columns.Add(new Vector3(columnSums[i].x/columnCounts[i],columnSums[i].y/columnCounts[i],(float)columnCounts[i]/count));
                var points=new List<Vector3>(8);
                for(int i=0;i<8;i++)
                {
                    if(strata[i]==null) continue;
                    // A stratum centroid may land in the hole of a window frame: the representative stays on the mask.
                    var center=strataSums[i]/strata[i].Count;var selected=strata[i][0];float best=float.PositiveInfinity;
                    foreach(var candidate in strata[i]) {float d=(candidate-center).sqrMagnitude;if(d<best) {best=d;selected=candidate;}}
                    points.Add(new Vector3(selected.x,selected.y,(float)strata[i].Count/count));
                }
                var shape=new Shape {Area=Math.Max(1,count)*(double)texel*texel,Columns=columns.ToArray(),Strata=points.ToArray()};
                shapes[(sprite,texel)]=shape;
                return shape;
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        // Bright pixels of a glowing screen or a flash (luminance above the threshold, smoothed over 0.15) on the sprite's
        // own rectangle of its texture, downscaled to at most 128×128: the light scene shader glows the same pixels.
        public static Glow MeasureGlow(Sprite sprite,float threshold)
        {
            if(sprite==null) throw new ArgumentNullException(nameof(sprite));
            if(glows.TryGetValue((sprite,threshold),out var cached)) return cached;
            Remains.Diagnostics.FrameHitchLog.Gpu("Свет: яркие пиксели экрана (чтение с видеокарты)");
            var rect=sprite.textureRect;var texture=sprite.texture;
            int width=Mathf.Clamp(Mathf.RoundToInt(rect.width),1,128),height=Mathf.Clamp(Mathf.RoundToInt(rect.height),1,128);
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            try
            {
                Graphics.Blit(texture,target,new Vector2(rect.width/texture.width,rect.height/texture.height),new Vector2(rect.x/texture.width,rect.y/texture.height));
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);Readbacks++;
                var pixels=image.GetPixels();var sums=new Vector2[8];var energies=new float[8];float energy=0,area=0;Color color=Color.clear;
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    var c=pixels[y*width+x];float luminance=.2126f*c.r+.7152f*c.g+.0722f*c.b;
                    float t=Mathf.Clamp01((luminance-threshold)/.15f),coverage=c.a*t*t*(3-2*t),e=luminance*coverage;
                    if(e<=0) continue;int bin=Mathf.Min(7,x*8/width);
                    sums[bin]+=new Vector2((x+.5f)/width,(y+.5f)/height)*e;energies[bin]+=e;
                    energy+=e;area+=coverage;color+=c*coverage;
                }
                var points=new List<Vector3>(8);
                for(int i=0;i<8;i++) if(energies[i]>0) points.Add(new Vector3(sums[i].x/energies[i],sums[i].y/energies[i],energies[i]/energy));
                var glow=new Glow {Points=points.ToArray(),Color=area>0?color/area:Color.clear,AreaFraction=area/(width*height)};
                glows[(sprite,threshold)]=glow;
                return glow;
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }

        private static void Prepare()
        {
            if(material==null)
                material=new Material(GameAssets.Load<Shader>(SceneShaderResource)??throw new InvalidOperationException("Нет шейдера "+SceneShaderResource)) {name="Форма спрайта для света",hideFlags=HideFlags.HideAndDontSave};
            if(quad==null) quad=UnitQuad("Квадрат формы спрайта");
        }
        public static Mesh UnitQuad(string name)
        {
            var mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};
            mesh.SetVertices(new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(1,1,0)});
            mesh.SetUVs(0,new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)});
            mesh.SetColors(new[]{Color.white,Color.white,Color.white,Color.white});
            mesh.SetTriangles(new[]{0,2,1,1,2,3},0);
            return mesh;
        }
    }
}
