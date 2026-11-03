using System;
using System.Collections;
using System.Collections.Generic;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Editor-only alternative to DirectLight: polar transmission once per emitting sample, then a local field.
    // Cached fields and the sum stay unchanged between events. The supplied Sources belong to this instance.
    internal sealed class StationaryPolarLight : IDisposable
    {
        internal const float OuterPitch=.05f,OuterFilterWidth=.05f;
        internal const long DefaultCacheBytes=64L*1024*1024;
        private const string ComputePath="Assets/Remains/Editor/LightGridStudy/StationaryPolarLight.compute";
        private sealed class Field {internal RenderTexture Texture;internal bool Stale;internal LinkedListNode<int> Lru;}
        private readonly LevelLightScene scene;
        private readonly LevelLightSources sources;
        private readonly long cacheBudget;
        private readonly Field[] fields;
        private readonly List<int> overlap=new(),affected=new();
        private readonly List<string> lastBuiltIds=new();
        private readonly LinkedList<int> lru=new();
        private readonly ComputeShader compute,composition;
        private readonly ComputeBuffer samples,marchDistances;
        private readonly int[] offsets;
        private readonly int buildKernel,fieldKernel,exactKernel,clearKernel,addKernel;
        private readonly int maximumAngles,maximumRows,maximumSamples;
        private readonly float marchStep,maximumRadius;
        private readonly ComputeBuffer transientSamples;
        private RenderTexture map,sum;
        private long cacheBytes;
        private bool built,disposed;
        internal Texture Sum=>sum;
        internal LevelLightScene Scene=>scene;
        internal LevelLightSources Sources=>sources;
        internal long CacheBytes=>cacheBytes;
        internal long SumBytes=>sum==null?0:(long)sum.width*sum.height*8;
        internal long ScratchBytes=>map==null?0:(long)maximumAngles*maximumRows*maximumSamples*4+maximumRows*4L+samples.count*16L+DirectLight.MaximumSamples*16L;
        internal long GpuBytes=>disposed?0:SumBytes+CacheBytes+ScratchBytes;
        internal float MaximumRadius=>maximumRadius;
        internal int LastMapAngles {get;private set;}
        internal int LastMapSamples {get;private set;}
        internal int LastMapRows {get;private set;}
        internal int TransientBuilds {get;private set;}
        internal int Builds {get;private set;}
        internal int Adds {get;private set;}
        internal int Evictions {get;private set;}
        internal int CachedFields {get;private set;}
        internal int LastBuilds {get;private set;}
        internal int LastAffectedSources {get;private set;}
        internal IReadOnlyList<string> LastBuiltIds=>lastBuiltIds;
        internal RectInt LastComposedRect {get;private set;}
        internal event Action<RectInt> Recomposed;

        internal StationaryPolarLight(LevelLightScene scene,LevelLightSources sources,long cacheBudget=DefaultCacheBytes)
        {
            this.scene=scene??throw new ArgumentNullException(nameof(scene));
            this.sources=sources??throw new ArgumentNullException(nameof(sources));
            if(cacheBudget<=0)throw new ArgumentOutOfRangeException(nameof(cacheBudget));
            this.cacheBudget=cacheBudget;fields=new Field[sources.Count];offsets=new int[sources.Count];
            marchStep=.75f/scene.Resolution;
            maximumRadius=OuterPitch;maximumSamples=DirectLight.MaximumSamples;
            var points=new List<Vector4>();
            for(int i=0;i<sources.Count;i++)
            {
                var source=sources.All[i];
                if(!float.IsFinite(source.Reach) || source.Reach<OuterPitch || !float.IsFinite(source.Width) || source.Width<=0)
                    throw new ArgumentException("Полярный стационарный свет: неверная дальность/ширина источника "+source.Id);
                if((long)source.Texels.width*source.Texels.height*8>cacheBudget)
                    throw new ArgumentException($"Полярный стационарный свет: поле {source.Id} больше бюджета {cacheBudget} байт.");
                maximumRadius=Math.Max(maximumRadius,source.Reach);maximumSamples=Math.Max(maximumSamples,source.Samples.Length);
                offsets[i]=points.Count;points.AddRange(source.Samples);
            }
            maximumAngles=(int)Math.Ceiling(2*Math.PI*maximumRadius/OuterPitch);
            maximumRows=(int)Math.Ceiling(maximumRadius/marchStep)+1;
            if(maximumAngles>SystemInfo.maxTextureSize || maximumRows>SystemInfo.maxTextureSize)
                throw new ArgumentException($"Полярный стационарный свет: резерв {maximumAngles}×{maximumRows}×{maximumSamples} превышает maxTextureSize={SystemInfo.maxTextureSize}.");
            try
            {
                if(!SystemInfo.supportsComputeShaders)throw new InvalidOperationException("Полярный стационарный свет: compute-шейдеры не поддерживаются.");
                compute=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath)??throw new InvalidOperationException("Нет Editor compute-шейдера "+ComputePath));
                composition=Object.Instantiate(GameAssets.Load<ComputeShader>("Presentation/DirectLight")??throw new InvalidOperationException("Нет compute-шейдера Presentation/DirectLight."));
                buildKernel=compute.FindKernel("BuildMaps");fieldKernel=compute.FindKernel("Field");exactKernel=compute.FindKernel("ExactField");
                clearKernel=composition.FindKernel("ClearRect");addKernel=composition.FindKernel("AddField");
                transientSamples=new ComputeBuffer(DirectLight.MaximumSamples,16);
                samples=new ComputeBuffer(Math.Max(1,points.Count),16);
                if(points.Count>0)samples.SetData(points);else samples.SetData(new[]{Vector4.zero});
                var distances=new float[maximumRows];float at=marchStep*.5f;
                for(int row=0;row<distances.Length;row++,at+=marchStep)distances[row]=at;
                marchDistances=new ComputeBuffer(maximumRows,4);marchDistances.SetData(distances);
                map=Target(maximumAngles,maximumRows,GraphicsFormat.R32_SFloat,"Стационарный свет: рабочие полярные префиксы",maximumSamples);
                sum=Target(scene.Width,scene.Height,GraphicsFormat.R16G16B16A16_SFloat,"Стационарный полярный свет: сумма");
                compute.SetInts("_SceneSize",scene.Width,scene.Height);
                compute.SetVector("_SceneRect",new Vector4(scene.WorldRect.x,scene.WorldRect.y,scene.WorldRect.width,scene.WorldRect.height));
                compute.SetFloat("_PPU",scene.Resolution);compute.SetFloat("_MarchStep",marchStep);
                foreach(int kernel in new[]{buildKernel,fieldKernel,exactKernel})
                {
                    compute.SetTexture(kernel,"_Scene",scene.Texture);compute.SetBuffer(kernel,"_Samples",samples);
                }
                compute.SetTexture(buildKernel,"_MapOut",map);compute.SetBuffer(buildKernel,"_MarchDistances",marchDistances);
                compute.SetTexture(fieldKernel,"_Map",map);compute.SetBuffer(fieldKernel,"_MarchDistances",marchDistances);
            }
            catch{Dispose();throw;}
        }

        internal IEnumerator BuildAllSteps()
        {
            EnsureAlive();ResetMetrics();built=false;
            foreach(var field in fields)if(field!=null)field.Stale=true;
            var all=new RectInt(0,0,scene.Width,scene.Height);Clear(all);
            for(int index=0;index<sources.Count;index++)
            {
                if(!sources.All[index].Enabled)continue;
                Add(index,Ensure(index),sources.All[index].Texels);yield return null;
            }
            built=true;LastComposedRect=all;LastAffectedSources=sources.Count;
        }
        internal void BuildAll()
        {
            var steps=BuildAllSteps();try{while(steps.MoveNext()){}}finally{(steps as IDisposable)?.Dispose();}
        }
        internal void SetLamp(string id,bool enabled)
        {
            EnsureBuilt();
            if(!sources.TryGet(id,out int index) || sources.All[index].Kind!=LevelLightSourceKind.Lamp)
                throw new ArgumentException("Полярный стационарный свет: неизвестная лампа "+id);
            ResetMetrics();if(sources.All[index].Enabled==enabled)return;
            sources.All[index].Enabled=enabled;SourceChanged(index);
        }
        // Caller has already changed Enabled in this instance's source catalog.
        internal void SourceChanged(int index)
        {
            EnsureBuilt();if(index<0 || index>=sources.Count)throw new ArgumentOutOfRangeException(nameof(index));
            ResetMetrics();LastAffectedSources=1;Recompose(sources.All[index].Texels);
        }
        // Caller first applies exactly the same door event to Scene.SetDoors. Unrelated source maps are not stale.
        internal void OccludersChanged(IReadOnlyList<string> ids,IReadOnlyList<RectInt> rectangles)
        {
            EnsureBuilt();ResetMetrics();
            if(ids==null || rectangles==null || ids.Count!=rectangles.Count)
                throw new ArgumentException("Полярный стационарный свет: изменения геометрии без согласованных ID/прямоугольников.");
            affected.Clear();var selected=new HashSet<int>();
            foreach(var rectangle in rectangles)
            {
                sources.Overlapping(rectangle,overlap);
                foreach(int index in overlap)if(selected.Add(index))affected.Add(index);
            }
            affected.Sort();LastAffectedSources=affected.Count;
            bool hasRegion=false;RectInt region=default;
            foreach(int index in affected)
            {
                if(fields[index]!=null)fields[index].Stale=true;
                if(!sources.All[index].Enabled)continue;
                region=hasRegion?Union(region,sources.All[index].Texels):sources.All[index].Texels;hasRegion=true;
            }
            if(hasRegion)Recompose(region);
        }
        // One-shot/moving source into a caller-owned local field. No sum/cache mutation and no reflected fill.
        internal void BuildTransient(LevelLightSource source,RenderTexture target)
        {
            EnsureAlive();
            if(source==null || target==null)throw new ArgumentNullException(source==null?nameof(source):nameof(target));
            if(source.Samples==null || source.Samples.Length==0 || source.Samples.Length>DirectLight.MaximumSamples)
                throw new ArgumentException("Полярный transient: неверное число точек формы "+source.Id);
            if(!float.IsFinite(source.Reach) || source.Reach<OuterPitch || source.Reach>maximumRadius)
                throw new ArgumentException($"Полярный transient {source.Id}: дальность {source.Reach} вне резерва {OuterPitch}…{maximumRadius} м.");
            if(source.Texels.width>target.width || source.Texels.height>target.height)
                throw new ArgumentException($"Полярный transient {source.Id}: поле {source.Texels.width}×{source.Texels.height} больше цели {target.width}×{target.height}.");
            transientSamples.SetData(source.Samples,0,0,source.Samples.Length);Configure(source,transientSamples,0);
            compute.Dispatch(buildKernel,(Angles(source)+63)/64,source.Samples.Length,1);
            compute.SetTexture(fieldKernel,"_Field",target);compute.Dispatch(fieldKernel,(source.Texels.width+7)/8,(source.Texels.height+7)/8,1);TransientBuilds++;
        }
        internal bool TryGetField(string id,out Texture texture,out RectInt texels)
        {
            EnsureBuilt();texture=null;texels=default;
            if(!sources.TryGet(id,out int index) || !sources.All[index].Enabled || fields[index]==null || fields[index].Stale)return false;
            Touch(fields[index]);texture=fields[index].Texture;texels=sources.All[index].Texels;return true;
        }
        // Explicit shadow/diagnostic request, never from an implicit per-frame asset lookup. Does not change the sum.
        internal Texture EnsureField(string id,out RectInt texels)
        {
            EnsureBuilt();
            if(!sources.TryGet(id,out int index))throw new ArgumentException("Полярный стационарный свет: нет источника "+id);
            texels=sources.All[index].Texels;return Ensure(index).Texture;
        }
        private void Recompose(RectInt region)
        {
            Clear(region);sources.Overlapping(region,overlap);
            foreach(int index in overlap)
            {
                if(!sources.All[index].Enabled)continue;
                Add(index,Ensure(index),Intersect(region,sources.All[index].Texels));
            }
            LastComposedRect=region;Recomposed?.Invoke(region);
        }
        private Field Ensure(int index)
        {
            var field=fields[index];
            if(field!=null && !field.Stale){Touch(field);return field;}
            var source=sources.All[index];var texels=source.Texels;
            if(field==null)
            {
                long bytes=(long)texels.width*texels.height*8;EvictBefore(bytes);
                field=new Field{Texture=Target(texels.width,texels.height,GraphicsFormat.R16G16B16A16_SFloat,"Полярное поле "+source.Id),Stale=true,Lru=lru.AddLast(index)};
                fields[index]=field;cacheBytes+=bytes;CachedFields++;
            }
            Configure(index);compute.Dispatch(buildKernel,(Angles(source)+63)/64,source.Samples.Length,1);
            compute.SetTexture(fieldKernel,"_Field",field.Texture);compute.Dispatch(fieldKernel,(texels.width+7)/8,(texels.height+7)/8,1);
            field.Stale=false;Touch(field);Builds++;LastBuilds++;lastBuiltIds.Add(source.Id);return field;
        }
        private static int Angles(LevelLightSource source)=>(int)Math.Ceiling(2*Math.PI*source.Reach/OuterPitch);
        private int Rows(LevelLightSource source)=>(int)Math.Ceiling(source.Reach/marchStep)+1;
        private void Configure(int index)
        {
            Configure(sources.All[index],samples,offsets[index]);
        }
        private void Configure(LevelLightSource source,ComputeBuffer points,int offset)
        {
            var texels=source.Texels;
            foreach(int kernel in new[]{buildKernel,fieldKernel,exactKernel})compute.SetBuffer(kernel,"_Samples",points);
            LastMapAngles=Angles(source);LastMapRows=Rows(source);LastMapSamples=source.Samples.Length;
            compute.SetInt("_SampleOffset",offset);compute.SetInt("_SampleCount",source.Samples.Length);
            compute.SetInt("_Angles",Angles(source));compute.SetInt("_RadialSamples",Rows(source));
            compute.SetFloat("_Radius",source.Reach);compute.SetFloat("_ProjectedWidth",source.Width);
            compute.SetVector("_Radiance",source.Radiance);
            compute.SetVector("_SourceBounds",new Vector4(source.Bounds.xMin,source.Bounds.yMin,source.Bounds.xMax,source.Bounds.yMax));
            compute.SetInts("_FieldOrigin",texels.x,texels.y);compute.SetInts("_FieldSize",texels.width,texels.height);
        }
        private void Clear(RectInt region)
        {
            composition.SetTexture(clearKernel,"_Sum",sum);composition.SetInts("_Clip",region.xMin,region.yMin,region.xMax,region.yMax);
            composition.Dispatch(clearKernel,(region.width+7)/8,(region.height+7)/8,1);
        }
        private void Add(int index,Field field,RectInt clip)
        {
            if(clip.width<=0 || clip.height<=0)return;
            var texels=sources.All[index].Texels;composition.SetTexture(addKernel,"_Sum",sum);composition.SetTexture(addKernel,"_FieldIn",field.Texture);
            composition.SetInts("_FieldOrigin",texels.x,texels.y);composition.SetInts("_Clip",clip.xMin,clip.yMin,clip.xMax,clip.yMax);
            composition.Dispatch(addKernel,(clip.width+7)/8,(clip.height+7)/8,1);Touch(field);Adds++;
        }
        private void EvictBefore(long incomingBytes)
        {
            while(cacheBytes+incomingBytes>cacheBudget)
            {
                if(lru.First==null)throw new InvalidOperationException($"Полярный стационарный свет: невозможно освободить кэш, занято={cacheBytes}, запрос={incomingBytes}, предел={cacheBudget}.");
                int oldest=lru.First.Value;lru.RemoveFirst();
                var texture=fields[oldest].Texture;cacheBytes-=(long)texture.width*texture.height*8;LightTextureBudget.Release(texture);fields[oldest]=null;CachedFields--;Evictions++;
            }
        }
        private void Touch(Field field){lru.Remove(field.Lru);lru.AddLast(field.Lru);}
        private void ResetMetrics(){LastBuilds=0;LastAffectedSources=0;LastComposedRect=default;lastBuiltIds.Clear();}
        private void EnsureAlive(){if(disposed)throw new ObjectDisposedException(nameof(StationaryPolarLight));}
        private void EnsureBuilt(){EnsureAlive();if(!built)throw new InvalidOperationException("Полярный стационарный свет: сначала требуется BuildAll.");}
        private static RectInt Union(RectInt a,RectInt b)
        {int x=Math.Min(a.xMin,b.xMin),y=Math.Min(a.yMin,b.yMin);return new RectInt(x,y,Math.Max(a.xMax,b.xMax)-x,Math.Max(a.yMax,b.yMax)-y);}
        private static RectInt Intersect(RectInt a,RectInt b)
        {int x=Math.Max(a.xMin,b.xMin),y=Math.Max(a.yMin,b.yMin);return new RectInt(x,y,Math.Max(0,Math.Min(a.xMax,b.xMax)-x),Math.Max(0,Math.Min(a.yMax,b.yMax)-y));}
        private static RenderTexture Target(int width,int height,GraphicsFormat format,string name,int slices=1)
        {
            var target=new RenderTexture(width,height,0,format){name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            if(slices>1){target.dimension=TextureDimension.Tex2DArray;target.volumeDepth=slices;}
            // The prefix shader always uses Texture2DArray, even for a catalog with one sample per source.
            if(format==GraphicsFormat.R32_SFloat){target.dimension=TextureDimension.Tex2DArray;target.volumeDepth=slices;target.filterMode=FilterMode.Point;}
            try{LightTextureBudget.Track(target,(long)width*height*slices*(format==GraphicsFormat.R32_SFloat?4:8));if(!target.Create())throw new InvalidOperationException("Не создана текстура "+name);return target;}
            catch{LightTextureBudget.Release(target);throw;}
        }
        internal readonly struct MapValidation
        {
            internal readonly int Samples,PartialSamples,BlockedSamples,ShapeSamples;
            internal readonly float MaximumError;
            internal MapValidation(int samples,int partial,int blocked,int shapes,float error)
            {Samples=samples;PartialSamples=partial;BlockedSamples=blocked;ShapeSamples=shapes;MaximumError=error;}
        }
        // Blocking offline oracle: checks every retained radial prefix at selected angles of EVERY shape sample.
        internal MapValidation ValidateMapAgainstCpu(string id,float[] absorption,int angleStride=53,float tolerance=.002f)
        {
            EnsureBuilt();
            if(!sources.TryGet(id,out int index))throw new ArgumentException("Проверка полярной карты: нет источника "+id);
            if(absorption==null || absorption.Length!=scene.Width*scene.Height || angleStride<1 || tolerance<=0)
                throw new ArgumentException("Проверка полярной карты: неверный размер поглощения/шаг/допуск.");
            var source=sources.All[index];Configure(index);int angles=Angles(source),rows=Rows(source);
            compute.Dispatch(buildKernel,(angles+63)/64,source.Samples.Length,1);
            var request=AsyncGPUReadback.Request(map,0,TextureFormat.RFloat);request.WaitForCompletion();
            if(request.hasError)throw new InvalidOperationException("Проверка полярной карты: ошибка чтения GPU источника "+id);
            int count=0,partial=0,blocked=0;float errorMaximum=0;
            for(int sample=0;sample<source.Samples.Length;sample++)
            {
            // Texture arrays expose one NativeArray per layer; GetData() alone is only layer zero.
            var data=request.GetData<float>(sample);
            if(data.Length!=maximumRows*maximumAngles)
                throw new InvalidOperationException($"Проверка полярной карты: {id}, слой={sample}, элементов={data.Length}, ожидается={maximumRows*maximumAngles}.");
            for(int angle=0;angle<angles;angle+=angleStride)
            {
                float a=(angle+.5f)*(2*Mathf.PI)/angles;var direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                var origin=new Vector2(source.Samples[sample].x,source.Samples[sample].y);
                float transmission=1,at=marchStep*.5f;bool stopped=false,leaving=true;
                for(int row=0;row<rows;row++,at+=marchStep)
                {
                    if(!stopped)
                    {
                        Vector2 world=origin+direction*at;int x=Mathf.FloorToInt((world.x-scene.WorldRect.x)*scene.Resolution),y=Mathf.FloorToInt((world.y-scene.WorldRect.y)*scene.Resolution);
                        if(x<0 || y<0 || x>=scene.Width || y>=scene.Height){transmission=0;stopped=true;}
                        else
                        {
                            float alpha=Mathf.Clamp01(absorption[y*scene.Width+x]);
                            bool emitter=world.x>=source.Bounds.xMin && world.x<=source.Bounds.xMax && world.y>=source.Bounds.yMin && world.y<=source.Bounds.yMax;
                            if(!(leaving && emitter && alpha>=.99f))
                            {
                                leaving=false;
                                if(alpha>=.99f){transmission=0;stopped=true;}
                                else if(alpha>.0001f)transmission*=Mathf.Pow(Mathf.Max(0,1-alpha),marchStep*scene.Resolution);
                                if(transmission<.0001f){transmission=0;stopped=true;}
                            }
                        }
                    }
                    float actual=data[row*maximumAngles+angle],error=Mathf.Abs(actual-transmission);
                    if(!float.IsFinite(actual) || error>tolerance)
                        throw new InvalidOperationException($"Проверка полярной карты: {id}, точка={sample}/{source.Samples.Length}, угол={angle}/{angles}, ряд={row}/{rows}, d={at}, GPU={actual}, CPU={transmission}, ошибка={error}, допуск={tolerance}.");
                    count++;if(transmission==0)blocked++;else if(transmission<.9999f)partial++;errorMaximum=Math.Max(errorMaximum,error);
                }
            }
            }
            return new MapValidation(count,partial,blocked,source.Samples.Length,errorMaximum);
        }
        internal RenderTexture ExactFieldForValidation(string id)
        {
            EnsureBuilt();if(!sources.TryGet(id,out int index))throw new ArgumentException("Полярный стационарный свет: нет источника "+id);
            Configure(index);var rect=sources.All[index].Texels;
            var target=Target(rect.width,rect.height,GraphicsFormat.R16G16B16A16_SFloat,"Полярный свет: точный контроль "+id);
            try{compute.SetTexture(exactKernel,"_Field",target);compute.Dispatch(exactKernel,(rect.width+7)/8,(rect.height+7)/8,1);return target;}
            catch{LightTextureBudget.Release(target);throw;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var field in fields)if(field!=null)LightTextureBudget.Release(field.Texture);
            LightTextureBudget.Release(map);LightTextureBudget.Release(sum);map=sum=null;cacheBytes=0;CachedFields=0;lru.Clear();
            samples?.Dispose();marchDistances?.Dispose();transientSamples?.Dispose();if(compute!=null)Object.DestroyImmediate(compute);if(composition!=null)Object.DestroyImmediate(composition);
        }
    }
}





