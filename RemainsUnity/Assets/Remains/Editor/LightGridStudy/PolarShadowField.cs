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
        private readonly int build,exact,exactAA,polar,compose,export,rasterBlockers;
        private readonly GridLightTexture fill;
        private readonly GridFillReconstruction reconstructedFill;
        private readonly RenderTexture coarseBlocked;
        private readonly Texture2D fence;
        internal RenderTexture Angles {get;private set;}
        internal RenderTexture Light {get;private set;}
        internal RenderTexture Coarse {get;private set;}
        internal RenderTexture Image {get;private set;}
        internal RenderTexture ExportImage {get;private set;}
        internal PolarShadowCase Case {get;}
        internal Vector2 Source {get;private set;}
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
                polar=compute.FindKernel("Polar");compose=compute.FindKernel("Compose");exactAA=compute.FindKernel("ExactAA");export=compute.FindKernel("Export");
                rasterBlockers=compute.FindKernel("RasterBlockers");
                segments=new ComputeBuffer(scene.Segments.Count,16);segments.SetData(scene.Segments);
                foreach(int kernel in new[]{build,exact,exactAA,polar,compose,rasterBlockers}) compute.SetBuffer(kernel,"_Segments",segments);
                compute.SetInt("_SegmentCount",scene.Segments.Count);compute.SetVector("_WorldSize",new Vector4(32,18,0,0));
                SetSource(scene.Source);compute.SetFloat("_Radius",PolarShadowCase.Radius);SetGain(1);
                Angles=Target(MaximumAngles,1,GraphicsFormat.R32_SFloat,"Угловая карта: резерв до шага 2 см");
                Light=Target(Width,Height,GraphicsFormat.R32G32B32A32_SFloat,"Полярный свет: полный кадр");
                Coarse=Target(160,90,GraphicsFormat.R32G32B32A32_SFloat,"Контроль света: сетка 20 см");
                Image=Target(Width,Height,GraphicsFormat.R32G32B32A32_SFloat,"Сравнение полярных теней");
                var fillData=scene.Fill();fill=new GridLightTexture(fillData);fill.Validate(fillData);
                coarseBlocked=Target(160,90,GraphicsFormat.R32_SFloat,"Заполнение: консервативная маска точных контуров");
                Size(160,90);compute.SetTexture(rasterBlockers,"_BlockedOut",coarseBlocked);compute.Dispatch(rasterBlockers,20,12,1);
                reconstructedFill=new GridFillReconstruction(Width,Height);
                reconstructedFill.Build(fill.Texture,coarseBlocked,new Rect(0,0,32,18),null,default,new Rect(0,0,32,18),segments,scene.Segments.Count);
                compute.SetTexture(compose,"_Fill",reconstructedFill.Texture);
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
            RenderPolar(filtered?-1:0);
        }
        // -1 = legacy two-angle interpolation; 0 = hard; positive = box width in metres at radius15m.
        internal void RenderPolar(float smoothingWidth)
        {
            if(!float.IsFinite(smoothingWidth) || smoothingWidth< -1 || smoothingWidth>.1f) throw new ArgumentOutOfRangeException(nameof(smoothingWidth));
            Size(Width,Height);compute.SetInt("_Filtered",smoothingWidth<0?1:smoothingWidth==0?0:2);
            compute.SetFloat("_SmoothingWidth",Mathf.Max(0,smoothingWidth));compute.SetTexture(polar,"_AngleMap",Angles);
            compute.SetTexture(polar,"_LightOut",Light);compute.Dispatch(polar,Width/8,Height/8,1);
        }
        internal void SetSource(Vector2 source)
        {
            if(!float.IsFinite(source.x) || !float.IsFinite(source.y) || Case.Contains(source))
                throw new ArgumentException("Некорректная позиция тестовой лампы: "+source);
            Source=source;compute.SetVector("_Source",new Vector4(source.x,source.y,0,0));
        }
        internal void SetGain(float gain)
        {
            if(!float.IsFinite(gain) || gain<0) throw new ArgumentOutOfRangeException(nameof(gain));
            compute.SetFloat("_DirectGain",gain);
        }
        internal void RenderReferenceAA()
        {
            Size(Width,Height);compute.SetTexture(exactAA,"_LightOut",Light);compute.Dispatch(exactAA,Width/8,Height/8,1);
        }
        internal void Compose(bool coarse=false)
        {
            Size(Width,Height);compute.SetInt("_Mode",coarse?1:0);compute.SetTexture(compose,"_Light",coarse?Coarse:Light);
            compute.SetTexture(compose,"_Image",Image);compute.Dispatch(compose,Width/8,Height/8,1);
        }
        private void Size(int width,int height)=>compute.SetInts("_Size",width,height);
        internal void SaveImage(string path,Texture2D pixels)
        {
            if(pixels.width!=Width || pixels.height!=Height || pixels.format!=TextureFormat.RGBA32)
                throw new ArgumentException("Неверный буфер PNG полярной пробы.");
            if(ExportImage==null) ExportImage=Target(Width,Height,GraphicsFormat.R8G8B8A8_UNorm,"Полярная проба: sRGB экспорт");
            Size(Width,Height);compute.SetTexture(export,"_LinearImage",Image);compute.SetTexture(export,"_ExportImage",ExportImage);
            compute.Dispatch(export,Width/8,Height/8,1);var previous=RenderTexture.active;
            try {RenderTexture.active=ExportImage;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0,false);System.IO.File.WriteAllBytes(path,pixels.EncodeToPNG());}
            finally {RenderTexture.active=previous;}
        }
        internal int ValidateFillComposition(Color[] direct)
        {
            var grid=reconstructedFill.ReadDiagnostics(Case.Id);var image=Read(Image);int filledShadowSamples=0;
            for(int i=0;i<image.Length;i+=137)
            {
                if(direct[i].a!=0) continue;
                int x=i%Width,y=i/Width;
                var expected=grid[i]*(.08f*.48f*.86f);
                if(expected.r<=.001f) continue;
                filledShadowSamples++;
                if(image[i].r+1e-5f<expected.r)
                    throw new InvalidOperationException($"Не отображается заполнение {Case.Id}, ({x},{y}): получено {image[i].r}, минимум {expected.r}.");
            }
            if(filledShadowSamples==0) throw new InvalidOperationException("Нет контрольных теневых точек с заполнением: "+Case.Id);
            return filledShadowSamples;
        }
        internal int ValidateFillReconstruction()
        {
            // Constant preservation exposes dark interpolation halos independently of the lamp/solver.
            var mask=Read(coarseBlocked);var pixels=new Color[mask.Length];
            for(int i=0;i<pixels.Length;i++) pixels[i]=mask[i].r>=.99f?Color.black:Color.white;
            var constant=new Texture2D(160,90,TextureFormat.RGBAFloat,false,true);int checks=0;
            try
            {
                constant.SetPixels(pixels);constant.Apply(false,false);
                reconstructedFill.Build(constant,coarseBlocked,new Rect(0,0,32,18),null,default,new Rect(0,0,32,18),segments,Case.Segments.Count);
                var image=reconstructedFill.ReadDiagnostics(Case.Id+" / постоянное поле");
                for(int y=0;y<Height;y++) for(int x=0;x<Width;x++)
                {
                    int i=y*Width+x;var p=new Vector2((x+.5f)/Width*32,(y+.5f)/Height*18);
                    if(Case.Contains(p)) continue;
                    if(image[i].a<=0 || Mathf.Abs(image[i].r-1)>1e-5f || Mathf.Abs(image[i].g-1)>1e-5f || Mathf.Abs(image[i].b-1)>1e-5f)
                        throw new InvalidOperationException($"Ореол заполнения {Case.Id}: открытый пиксель ({x},{y}), world={p}, RGB={image[i]}, support={image[i].a}.");
                    checks++;
                }
                return checks;
            }
            finally
            {
                reconstructedFill.Build(fill.Texture,coarseBlocked,new Rect(0,0,32,18),null,default,new Rect(0,0,32,18),segments,Case.Segments.Count);
                Object.DestroyImmediate(constant);
            }
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
            fill?.Dispose();reconstructedFill?.Dispose();segments?.Dispose();
            foreach(var obj in new Object[]{Angles,Light,Coarse,Image,ExportImage,coarseBlocked,compute,fence}) if(obj!=null) Object.DestroyImmediate(obj);
        }
    }
}
