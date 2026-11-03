using System;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Persistent GPU field; an event uploads only the solver's affected rectangle, not the whole factory.
    public sealed class GridLightTexture : IDisposable
    {
        public RenderTexture Texture {get;private set;}
        public long Bytes=>(long)Texture.width*Texture.height*8;
        public int UploadedCells {get;private set;}
        private readonly Texture2D fence=new(1,1,TextureFormat.RGBAHalf,false,true);

        public GridLightTexture(GridLightField field)
        {
            Texture=new RenderTexture(field.Width,field.Height,0,GraphicsFormat.R16G16B16A16_SFloat)
                {name="Эксперимент: локальная световая сетка",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            try
            {
                if(!Texture.Create()) throw new InvalidOperationException("Не создана GPU-текстура экспериментального света.");
                Update(field,new RectInt(0,0,field.Width,field.Height));
            }
            catch {Dispose();throw;}
        }
        public void Update(GridLightField field,RectInt rect)
        {
            if(field.Width!=Texture.width || field.Height!=Texture.height || rect.width<=0 || rect.height<=0 ||
                rect.xMin<0 || rect.yMin<0 || rect.xMax>field.Width || rect.yMax>field.Height)
                throw new ArgumentException("Неверная область загрузки световой сетки: "+rect);
            UploadedCells=rect.width*rect.height;
            var pixels=new Color[UploadedCells];
            for(int y=0;y<rect.height;y++) Array.Copy(field.Pixels,(rect.y+y)*field.Width+rect.x,pixels,y*rect.width,rect.width);
            var patch=new Texture2D(rect.width,rect.height,TextureFormat.RGBAHalf,false,true);
            var previous=RenderTexture.active;
            try
            {
                patch.SetPixels(pixels);patch.Apply(false,true);
                Graphics.CopyTexture(patch,0,0,0,0,rect.width,rect.height,Texture,0,0,rect.x,rect.y);
                RenderTexture.active=Texture;fence.ReadPixels(new Rect(rect.x,rect.y,1,1),0,0,false);
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(patch);}
        }
        public void Dispose()
        {
            if(Texture!=null) {Texture.Release();Object.DestroyImmediate(Texture);Texture=null;}
            if(fence!=null) Object.DestroyImmediate(fence);
        }

        public void Validate(GridLightField field)
        {
            var previous=RenderTexture.active;
            var image=new Texture2D(field.Width,field.Height,TextureFormat.RGBAHalf,false,true);
            try
            {
                RenderTexture.active=Texture;image.ReadPixels(new Rect(0,0,field.Width,field.Height),0,0,false);
                var raw=image.GetRawTextureData<ushort>();
                for(int i=0;i<field.Pixels.Length;i++) for(int c=0;c<4;c++)
                    if(raw[i*4+c]!=Mathf.FloatToHalf(field.Pixels[i][c]))
                        throw new InvalidOperationException($"GPU-сетка отличается от CPU после локальной загрузки: клетка {i}, канал {c}.");
                Debug.Log("Локальная загрузка GPU-сетки совпала со всем CPU-полем в RGBAHalf.");
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }

        public static float[] ReadAbsorption(LevelLightScene scene,int divisor,RectInt region)
        {
            if(divisor<=0 || scene.Width%divisor!=0 || scene.Height%divisor!=0 || region.width<=0 || region.height<=0 ||
                region.xMin<0 || region.yMin<0 || region.xMax>scene.Width/divisor || region.yMax>scene.Height/divisor)
                throw new ArgumentException("Неверная область чтения поглощения: "+region);
            var previous=RenderTexture.active;
            var image=new Texture2D(region.width*divisor,region.height*divisor,TextureFormat.RFloat,false,true);
            try
            {
                RenderTexture.active=(RenderTexture)scene.Texture;
                image.ReadPixels(new Rect(region.x*divisor,region.y*divisor,image.width,image.height),0,0,false);
                var raw=image.GetRawTextureData<float>();var result=new float[region.width*region.height];
                for(int y=0;y<region.height;y++) for(int x=0;x<region.width;x++)
                {
                    float maximum=0;
                    for(int dy=0;dy<divisor;dy++) for(int dx=0;dx<divisor;dx++) maximum=Mathf.Max(maximum,raw[(y*divisor+dy)*image.width+x*divisor+dx]);
                    result[y*region.width+x]=maximum>=GridLightField.OpaqueThreshold?1:
                        Mathf.Min((float)LightRules.MaxObjectTexelAbsorption,1-Mathf.Pow(1-Mathf.Clamp01(maximum),divisor));
                }
                return result;
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        public static RectInt CoarseRect(LevelLightScene scene,int divisor,Rect world)
        {
            var fine=scene.Texels(world);
            int x=fine.xMin/divisor,y=fine.yMin/divisor;
            return new RectInt(x,y,(fine.xMax+divisor-1)/divisor-x,(fine.yMax+divisor-1)/divisor-y);
        }
        public static void PatchAbsorption(float[] absorption,int width,RectInt region,float[] patch)
        {
            if(patch.Length!=region.width*region.height) throw new ArgumentException("Размер участка поглощения не совпадает с областью.");
            for(int y=0;y<region.height;y++) Array.Copy(patch,y*region.width,absorption,(region.y+y)*width+region.x,region.width);
        }
    }
}
