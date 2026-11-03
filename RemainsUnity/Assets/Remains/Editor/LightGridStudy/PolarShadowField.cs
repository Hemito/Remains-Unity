using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Opaque analytic contours ONLY. Atlas generation and full-screen evaluation are independently measurable.
    internal sealed class PolarShadowField : IDisposable
    {
        internal const int Width=1536,Height=864;
        internal const int MaximumAngles=4713;
        private readonly ComputeShader compute;
        private readonly ComputeBuffer segments;
        private readonly int build,exact,polar,compose;
        private readonly GridLightTexture fill;
        private readonly Texture2D fence;
        internal RenderTexture Angles {get;private set;}
        internal RenderTexture Light {get;private set;}
        internal RenderTexture Coarse {get;private set;}
        internal RenderTexture Image {get;private set;}
        internal PolarShadowCase Case {get;}
        internal int AngleCount {get;private set;}
        internal PolarShadowField(PolarShadowCase scene)
        {
            Case=scene;
            try
            {
                if(!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Проба полярных теней: compute не поддерживается.");
                var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Remains/Editor/LightGridStudy/PolarShadow.compute");
                if(asset==null) throw new InvalidOperationException("Нет Editor-шейдера PolarShadow.compute.");
                compute=Object.Instantiate(asset);build=compute.FindKernel("BuildAngles");exact=compute.FindKernel("Exact");
                polar=compute.FindKernel("Polar");compose=compute.FindKernel("Compose");
                segments=new ComputeBuffer(scene.Segments.Count,16);segments.SetData(scene.Segments);
                foreach(int kernel in new[]{build,exact,polar,compose}) compute.SetBuffer(kernel,"_Segments",segments);
                compute.SetInt("_SegmentCount",scene.Segments.Count);compute.SetVector("_WorldSize",new Vector4(32,18,0,0));
                compute.SetVector("_Source",new Vector4(scene.Source.x,scene.Source.y,0,0));compute.SetFloat("_Radius",PolarShadowCase.Radius);
                Angles=Target(MaximumAngles,1,GraphicsFormat.R32_SFloat,"Угловая карта: резерв до шага 2 см");
                Light=Target(Width,Height,GraphicsFormat.R32G32B32A32_SFloat,"Полярный свет: полный кадр");
                Coarse=Target(160,90,GraphicsFormat.R32G32B32A32_SFloat,"Контроль света: сетка 20 см");
                Image=Target(Width,Height,GraphicsFormat.R32G32B32A32_SFloat,"Сравнение полярных теней");
                var fillData=scene.Fill();fill=new GridLightTexture(fillData);fill.Validate(fillData);
                compute.SetTexture(compose,"_Fill",fill.Texture);
                fence=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            }
            catch {Dispose();throw;}
        }
        internal void Build(float step)
        {
            if(step<.02f || step>.05f) throw new ArgumentOutOfRangeException(nameof(step));
            AngleCount=(int)Math.Ceiling(2*Math.PI*PolarShadowCase.Radius/step);
            if(AngleCount>MaximumAngles) throw new InvalidOperationException("Превышен резерв угловой карты.");
            compute.SetInt("_Angles",AngleCount);compute.SetTexture(build,"_AngleOut",Angles);compute.Dispatch(build,(AngleCount+63)/64,1,1);
        }
        internal void RenderExact(bool coarse=false)
        {
            var target=coarse?Coarse:Light;Size(target.width,target.height);
            compute.SetTexture(exact,"_LightOut",target);compute.Dispatch(exact,(target.width+7)/8,(target.height+7)/8,1);
        }
        internal void RenderPolar(bool filtered)
        {
            Size(Width,Height);compute.SetInt("_Filtered",filtered?1:0);compute.SetTexture(polar,"_AngleMap",Angles);
            compute.SetTexture(polar,"_LightOut",Light);compute.Dispatch(polar,Width/8,Height/8,1);
        }
        internal void Compose(bool coarse=false)
        {
            Size(Width,Height);compute.SetInt("_Mode",coarse?1:0);compute.SetTexture(compose,"_Light",coarse?Coarse:Light);
            compute.SetTexture(compose,"_Image",Image);compute.Dispatch(compose,Width/8,Height/8,1);
        }
        private void Size(int width,int height)=>compute.SetInts("_Size",width,height);
        internal int ValidateFillComposition(Color[] direct)
        {
            var grid=Read(fill.Texture);var image=Read(Image);int filledShadowSamples=0;
            for(int i=0;i<image.Length;i+=137)
            {
                if(direct[i].a!=0) continue;
                int x=i%Width,y=i/Width;float gx=(x+.5f)/Width*160-.5f,gy=(y+.5f)/Height*90-.5f;
                int x0=Mathf.FloorToInt(gx),y0=Mathf.FloorToInt(gy);
                Color At(int px,int py)=>grid[Mathf.Clamp(py,0,89)*160+Mathf.Clamp(px,0,159)];
                var expected=Color.Lerp(Color.Lerp(At(x0,y0),At(x0+1,y0),gx-x0),
                    Color.Lerp(At(x0,y0+1),At(x0+1,y0+1),gx-x0),gy-y0)*(.08f*.48f*.86f);
                if(expected.r<=.001f) continue;
                filledShadowSamples++;
                if(image[i].r+1e-5f<expected.r)
                    throw new InvalidOperationException($"Не отображается заполнение {Case.Id}, ({x},{y}): получено {image[i].r}, минимум {expected.r}.");
            }
            if(filledShadowSamples==0) throw new InvalidOperationException("Нет контрольных теневых точек с заполнением: "+Case.Id);
            return filledShadowSamples;
        }
        internal void Wait(RenderTexture target)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=target;fence.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        internal static Color[] Read(RenderTexture target)
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);return image.GetPixels();}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        private static RenderTexture Target(int width,int height,GraphicsFormat format,string name)
        {
            var target=new RenderTexture(width,height,0,format){name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            if(!target.Create()) {Object.DestroyImmediate(target);throw new InvalidOperationException("Не создана текстура: "+name);}return target;
        }
        public void Dispose()
        {
            fill?.Dispose();segments?.Dispose();
            foreach(var obj in new Object[]{Angles,Light,Coarse,Image,compute,fence}) if(obj!=null) Object.DestroyImmediate(obj);
        }
    }
}
