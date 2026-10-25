using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Decision prototype only. No runtime registration, no assets or scene state are saved.
    public sealed class HybridDirectPrototype : IDisposable
    {
        public sealed class Source
        {
            public string Id;
            public Vector4[] Samples;
            public Color Radiance;
            public float Radius,ProjectedWidth;
            public Rect Bounds;
        }
        public const int MaximumSamples=8;
        private const string ComputePath="Assets/Remains/Editor/LightHybrid/HybridDirect.compute";
        private readonly ComputeShader compute;
        private readonly Rect rect;
        private readonly int width,height;
        private readonly float ppu;
        private Texture scene;
        private readonly HashSet<RenderTexture> targets=new();
        private readonly Texture2D fencePixel;
        public long Bytes {get;private set;}
        public long PeakBytes {get;private set;}
        public string TimingMethod=>"CPU submit plus blocking 1-pixel GPU readback; input queue drained before timer; not GPU timestamp queries.";
        public string Normalization=>"Flatland projected-width/distance flux, normalized area-sample weights, near clamp 0.5 texel, smooth last 10% of explicit radius; no ambient. Initial connected emitter segment inside source bounds is excluded from self absorption.";

        public HybridDirectPrototype(Texture fineScene,Rect worldRect,int fineWidth,int fineHeight,float finePPU)
        {
            rect=worldRect;width=fineWidth;height=fineHeight;ppu=finePPU;
            if(width<=0 || height<=0 || rect.width<=0 || rect.height<=0 || !float.IsFinite(ppu) || ppu<=0 ||
                Mathf.Abs(rect.width*ppu-width)>.02f || Mathf.Abs(rect.height*ppu-height)>.02f)
                throw new ArgumentException("Гибридный прямой свет: плотность и размер сцены не совпадают.");
            compute=AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath)??throw new InvalidOperationException("Нет Editor compute прямого света: "+ComputePath);
            fencePixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            SetScene(fineScene);
        }
        public void SetScene(Texture fineScene)
        {
            if(fineScene==null || fineScene.width!=width || fineScene.height!=height)
                throw new ArgumentException("Гибридный прямой свет: неверный размер поглощающей сцены.");
            scene=fineScene;
        }
        public Rect SourceRect(Source source)
        {
            Validate(source);
            int x0=Mathf.Clamp(Mathf.FloorToInt((source.Bounds.xMin-source.Radius-rect.xMin)*ppu),0,width);
            int y0=Mathf.Clamp(Mathf.FloorToInt((source.Bounds.yMin-source.Radius-rect.yMin)*ppu),0,height);
            int x1=Mathf.Clamp(Mathf.CeilToInt((source.Bounds.xMax+source.Radius-rect.xMin)*ppu),0,width);
            int y1=Mathf.Clamp(Mathf.CeilToInt((source.Bounds.yMax+source.Radius-rect.yMin)*ppu),0,height);
            if(x1<=x0 || y1<=y0) throw new InvalidOperationException("Область источника вне уровня: "+source.Id);
            return new Rect(rect.xMin+x0/ppu,rect.yMin+y0/ppu,(x1-x0)/ppu,(y1-y0)/ppu);
        }
        public RenderTexture Build(Source source,out double gpuFenceMs)
        {
            var bounds=SourceRect(source);var samples=AreaSamples(source.Samples);
            var target=Allocate(Mathf.RoundToInt(bounds.width*ppu),Mathf.RoundToInt(bounds.height*ppu),"Гибрид direct: "+source.Id);
            using var buffer=new ComputeBuffer(samples.Length,16);
            try
            {
                Fence(scene as RenderTexture); // exclude earlier scene work from this source's measured submission.
                buffer.SetData(samples);int kernel=compute.FindKernel("Direct");
                SetSceneUniforms();compute.SetVector("_TargetRect",new Vector4(bounds.x,bounds.y,bounds.width,bounds.height));
                compute.SetInts("_TargetSize",target.width,target.height);
                compute.SetVector("_SourceBounds",new Vector4(source.Bounds.xMin,source.Bounds.yMin,source.Bounds.xMax,source.Bounds.yMax));
                compute.SetVector("_Radiance",source.Radiance);compute.SetFloat("_Radius",source.Radius);
                compute.SetFloat("_ProjectedWidth",source.ProjectedWidth);compute.SetInt("_SampleCount",samples.Length);
                compute.SetBuffer(kernel,"_Samples",buffer);compute.SetTexture(kernel,"_FineScene",scene);compute.SetTexture(kernel,"_Output",target);
                var timer=Stopwatch.StartNew();compute.Dispatch(kernel,(target.width+7)/8,(target.height+7)/8,1);Fence(target);gpuFenceMs=timer.Elapsed.TotalMilliseconds;
                PeakBytes=Math.Max(PeakBytes,Bytes+samples.Length*16L);return target;
            }
            catch {Release(target);throw;}
        }
        public RenderTexture Accumulate(IReadOnlyList<(Source source,RenderTexture field)> fields,Texture bounce=null)
        {
            if(fields==null) throw new ArgumentNullException(nameof(fields));
            var target=Allocate(width,height,"Гибрид: сумма прямого света");
            try
            {
                Clear(target);
                foreach(var entry in fields) Accumulate(entry.source,entry.field,target);
                if(bounce!=null)
                {
                    int kernel=compute.FindKernel("AddBounce");compute.SetInts("_WholeSize",width,height);
                    compute.SetTexture(kernel,"_Whole",target);compute.SetTexture(kernel,"_Bounce",bounce);
                    compute.Dispatch(kernel,(width+7)/8,(height+7)/8,1);
                }
                return target;
            }
            catch {Release(target);throw;}
        }
        public void Clear(RenderTexture target)
        {
            if(target==null) throw new ArgumentNullException(nameof(target));
            int kernel=compute.FindKernel("Clear");compute.SetInts("_WholeSize",target.width,target.height);compute.SetTexture(kernel,"_Whole",target);
            compute.Dispatch(kernel,(target.width+7)/8,(target.height+7)/8,1);
        }
        public void Accumulate(Source source,RenderTexture field,RenderTexture target)
        {
            if(field==null || target==null || target.width!=width || target.height!=height)
                throw new ArgumentException("Гибридный direct: неверный target суммирования.");
            var bounds=SourceRect(source);
            if(field.width!=Mathf.RoundToInt(bounds.width*ppu) || field.height!=Mathf.RoundToInt(bounds.height*ppu))
                throw new InvalidOperationException("Изменилась регистрация кеша direct: "+source.Id);
            int kernel=compute.FindKernel("AddSource");compute.SetInts("_TargetSize",field.width,field.height);compute.SetInts("_WholeSize",width,height);
            compute.SetInts("_Offset",Mathf.RoundToInt((bounds.x-rect.x)*ppu),Mathf.RoundToInt((bounds.y-rect.y)*ppu));
            compute.SetTexture(kernel,"_SourceField",field);compute.SetTexture(kernel,"_Whole",target);
            compute.Dispatch(kernel,(field.width+7)/8,(field.height+7)/8,1);
        }
        public RenderTexture NewWholeTarget(string name)=>Allocate(width,height,name);
        private void SetSceneUniforms()
        {
            compute.SetInts("_FineSize",width,height);compute.SetVector("_WorldRect",new Vector4(rect.x,rect.y,rect.width,rect.height));
            compute.SetFloat("_FinePPU",ppu);
        }
        private RenderTexture Allocate(int w,int h,string name)
        {
            var value=new RenderTexture(w,h,0) {name=name,graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat,enableRandomWrite=true,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            if(!value.Create()) {Object.DestroyImmediate(value);throw new InvalidOperationException("Не выделен гибридный RT: "+name);}
            targets.Add(value);Bytes+=w*(long)h*8;PeakBytes=Math.Max(PeakBytes,Bytes);return value;
        }
        public void Fence(RenderTexture target)
        {
            if(target==null) return;
            var previous=RenderTexture.active;
            try {RenderTexture.active=target;fencePixel.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        public void Release(RenderTexture target)
        {
            if(target==null || !targets.Remove(target)) return;
            Bytes-=target.width*(long)target.height*8;target.Release();Object.DestroyImmediate(target);
        }
        public void Dispose()
        {foreach(var target in targets.ToArray()) Release(target);Object.DestroyImmediate(fencePixel);}
        private static Vector4[] AreaSamples(Vector4[] source)
        {
            if(source==null || source.Length==0) throw new ArgumentException("Нет сэмплов излучающей формы.");
            var valid=source.Where(v=>float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z) && v.z>0).ToArray();
            if(valid.Length!=source.Length) throw new ArgumentException("Некорректный сэмпл излучающей формы.");
            // Preserve spatially dispersed points from the source sampler; no random seed or frame-dependent noise.
            int count=Math.Min(MaximumSamples,valid.Length);var selected=new Vector4[count];float sum=0;
            for(int i=0;i<count;i++) {selected[i]=valid[(int)((long)i*valid.Length/count)];sum+=selected[i].z;}
            if(!(sum>0)) throw new ArgumentException("Нулевая площадь излучающей формы.");
            for(int i=0;i<count;i++) selected[i].z/=sum;return selected;
        }
        private static void Validate(Source source)
        {
            if(source==null || string.IsNullOrEmpty(source.Id) || source.Samples==null || source.Samples.Length==0 ||
                !float.IsFinite(source.Radius) || source.Radius<=0 || !float.IsFinite(source.ProjectedWidth) || source.ProjectedWidth<=0 ||
                source.Bounds.width<=0 || source.Bounds.height<=0 || !float.IsFinite(source.Bounds.xMin) || !float.IsFinite(source.Bounds.yMin) ||
                !float.IsFinite(source.Bounds.xMax) || !float.IsFinite(source.Bounds.yMax) ||
                !float.IsFinite(source.Radiance.r) || !float.IsFinite(source.Radiance.g) || !float.IsFinite(source.Radiance.b) ||
                source.Radiance.r<0 || source.Radiance.g<0 || source.Radiance.b<0)
                throw new ArgumentException("Некорректный источник гибридного direct: "+source?.Id);
        }
    }
}
