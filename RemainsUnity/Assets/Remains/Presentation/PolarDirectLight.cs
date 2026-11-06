using System;
using System.Collections;
using System.Collections.Generic;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Presentation
{
    // Polar transmission once per emitting sample, then a local field.
    // Cached fields and the sum stay unchanged between events. The supplied Sources belong to this instance.
    public partial class PolarDirectLight : ILevelDirectLight
    {
        public const float OuterPitch=.05f,OuterFilterWidth=.05f;
        public const long DefaultCacheBytes=64L*1024*1024;
        private const string ComputeResource="Presentation/PolarDirectLight";
        private sealed class Field {public RenderTexture Texture;public bool Stale,Temporary;public LinkedListNode<int> Lru;}
        private sealed class Rebuild
        {
            public RectInt Region;
            public readonly List<int> Sources=new();
            public int Next;
            public RenderTexture Texture;
        }
        private readonly LevelLightScene scene;
        private readonly LevelLightSources sources;
        private readonly long cacheBudget;
        private readonly Field[] fields;
        private readonly bool[] publishedEnabled;
        private readonly List<int> overlap=new(),affected=new();
        private readonly List<string> lastBuiltIds=new();
        private readonly LinkedList<int> lru=new();
        private readonly List<RectInt> pendingRegions=new();
        private readonly HashSet<int> requested=new(),rejectedRequests=new(),warnedRequests=new();
        private readonly HashSet<int> unpublished=new();
        private readonly List<int> requestOrder=new();
        private Rebuild rebuild;
        private readonly ComputeShader compute,composition;
        private readonly ComputeBuffer samples,marchDistances;
        private readonly int[] offsets;
        private readonly int buildKernel,fieldKernel,exactKernel,clearKernel,addKernel,clearWorkKernel,addWorkKernel,removeKernel;
        private readonly int maximumAngles,maximumRows,maximumSamples;
        private readonly int detailKernel,detailFlashKernel;
        private readonly float marchStep,maximumRadius;
        private readonly ComputeBuffer transientSamples;
        private RenderTexture map,sum;
        private long cacheBytes;
        private bool built,building,disposed;
        private const int TransientSlots=16;
        private int transientSlot;
        public Texture Sum=>sum;
        public LevelLightScene Scene=>scene;
        public LevelLightSources Sources=>sources;
        public long CacheBytes=>cacheBytes;
        public long SumBytes=>sum==null?0:(long)sum.width*sum.height*16;
        public long ScratchBytes=>map==null?0:(long)maximumAngles*maximumRows*maximumSamples*4+maximumRows*4L+samples.count*16L+DirectLight.MaximumSamples*TransientSlots*16L+(rebuild?.Texture==null?0:(long)rebuild.Texture.width*rebuild.Texture.height*16);
        public long GpuBytes=>disposed?0:SumBytes+CacheBytes+ScratchBytes+DetailMapBytes;
        public float MaximumRadius=>maximumRadius;
        public int LastMapAngles {get;private set;}
        public int LastMapSamples {get;private set;}
        public int LastMapRows {get;private set;}
        public int TransientBuilds {get;private set;}
        public int Builds {get;private set;}
        public int Adds {get;private set;}
        public int Evictions {get;private set;}
        public int CachedFields {get;private set;}
        public int Recompositions {get;private set;}
        public int FastRemovals {get;private set;}
        public int PendingMeasurements=>0;
        public long Revision {get;private set;}
        public bool HasWork=>built && (rebuild!=null || pendingRegions.Count>0 || MissingRequested());
        public bool HasPendingPublication=>built && (rebuild!=null || pendingRegions.Count>0);
        public int LastBuilds {get;private set;}
        public int LastAffectedSources {get;private set;}
        public IReadOnlyList<string> LastBuiltIds=>lastBuiltIds;
        public RectInt LastComposedRect {get;private set;}
        public event Action<RectInt> Recomposed;

        public PolarDirectLight(LevelLightScene scene,LevelLightSources sources,long cacheBudget=DefaultCacheBytes)
        {
            this.scene=scene??throw new ArgumentNullException(nameof(scene));
            this.sources=sources??throw new ArgumentNullException(nameof(sources));
            if(cacheBudget<=0)throw new ArgumentOutOfRangeException(nameof(cacheBudget));
            this.cacheBudget=cacheBudget;fields=new Field[sources.Count];offsets=new int[sources.Count];publishedEnabled=new bool[sources.Count];
            marchStep=.75f/scene.Resolution;
            maximumRadius=12;maximumSamples=DirectLight.MaximumSamples;
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
                compute=Object.Instantiate(GameAssets.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource));
                composition=Object.Instantiate(GameAssets.Load<ComputeShader>("Presentation/DirectLight")??throw new InvalidOperationException("Нет compute-шейдера Presentation/DirectLight."));
                buildKernel=compute.FindKernel("BuildMaps");fieldKernel=compute.FindKernel("Field");exactKernel=compute.FindKernel("ExactField");
                clearWorkKernel=compute.FindKernel("ClearWork");addWorkKernel=compute.FindKernel("AddWork");
                removeKernel=compute.FindKernel("RemoveField");detailKernel=compute.FindKernel("Detail");detailFlashKernel=compute.FindKernel("DetailFlash");
                clearKernel=composition.FindKernel("ClearRect");addKernel=composition.FindKernel("AddField");
                transientSamples=new ComputeBuffer(DirectLight.MaximumSamples*TransientSlots,16);
                samples=new ComputeBuffer(Math.Max(1,points.Count),16);
                if(points.Count>0)samples.SetData(points);else samples.SetData(new[]{Vector4.zero});
                var distances=new float[maximumRows];float at=marchStep*.5f;
                for(int row=0;row<distances.Length;row++,at+=marchStep)distances[row]=at;
                marchDistances=new ComputeBuffer(maximumRows,4);marchDistances.SetData(distances);
                map=Target(maximumAngles,maximumRows,GraphicsFormat.R32_SFloat,"Стационарный свет: рабочие полярные префиксы",maximumSamples);
                // Keep the additive sum in float32: removing a bright lamp must reveal weak neighbours that
                // FP16 accumulation would already have rounded away. Individual cached source fields stay FP16.
                sum=Target(scene.Width,scene.Height,GraphicsFormat.R32G32B32A32_SFloat,"Стационарный полярный свет: точная сумма");
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

        public IEnumerator BuildAllSteps()
        {
            EnsureAlive();ResetMetrics();built=false;building=true;ReleaseRebuild();pendingRegions.Clear();unpublished.Clear();
            ReleaseDetailMaps();
            foreach(var field in fields)if(field!=null)field.Stale=true;
            var all=new RectInt(0,0,scene.Width,scene.Height);Clear(all);
            for(int index=0;index<sources.Count;index++)
            {
                if(!sources.All[index].Enabled)continue;
                var field=Ensure(index);Add(index,field,sources.All[index].Texels);ReleaseTemporary(field);yield return null;
            }
            for(int i=0;i<sources.Count;i++)publishedEnabled[i]=sources.All[i].Enabled;
            built=true;building=false;LastComposedRect=all;LastAffectedSources=sources.Count;Revision++;
            while(HasWork){Step(1);yield return null;}
        }
        public void BuildAll()
        {
            var steps=BuildAllSteps();try{while(steps.MoveNext()){}}finally{(steps as IDisposable)?.Dispose();}
        }
        public void SetLamp(string id,bool enabled)
        {
            EnsureBuilt();
            if(!sources.TryGet(id,out int index) || sources.All[index].Kind!=LevelLightSourceKind.Lamp)
                throw new ArgumentException("Полярный стационарный свет: неизвестная лампа "+id);
            ResetMetrics();if(sources.All[index].Enabled==enabled)return;
            sources.All[index].Enabled=enabled;SourceChanged(index);
        }
        // Caller has already changed Enabled in this instance's source catalog.
        public virtual void SourceChanged(int index)
        {
            EnsureAlive();if(index<0 || index>=sources.Count)throw new ArgumentOutOfRangeException(nameof(index));
            ResetMetrics();LastAffectedSources=1;
            if(built || building){unpublished.Add(index);QueueRegion(sources.All[index].Texels);}
        }
        // Only an OFF transition with a fully published geometry/sum may remove its existing contribution.
        // A missing cached field can be rebuilt for this ONE source on the same geometry; never build neighbours.
        public bool CanRemoveSource(int index,int maximumTexels)
        {
            EnsureAlive();if(index<0 || index>=sources.Count)throw new ArgumentOutOfRangeException(nameof(index));
            var source=sources.All[index];
            return built && !building && !HasPendingPublication && source.Kind==LevelLightSourceKind.Lamp &&
                !source.Enabled && publishedEnabled[index] && fields[index]?.Stale!=true &&
                (long)source.Texels.width*source.Texels.height<=maximumTexels;
        }
        public void RemoveSource(int index,int maximumTexels)
        {
            if(!CanRemoveSource(index,maximumTexels))throw new InvalidOperationException("Быстрое выключение полярного источника не прошло предварительную проверку: "+sources.All[index].Id);
            ResetMetrics();LastAffectedSources=1;var rect=sources.All[index].Texels;
            var field=Ensure(index);
            try
            {
                compute.SetTexture(removeKernel,"_DirectSum",sum);compute.SetTexture(removeKernel,"_RemovedField",field.Texture);
                compute.SetInts("_RemovalOrigin",rect.x,rect.y);compute.SetInts("_RemovalSize",rect.width,rect.height);
                compute.Dispatch(removeKernel,(rect.width+7)/8,(rect.height+7)/8,1);
            }
            finally {ReleaseTemporary(field);}
            publishedEnabled[index]=false;unpublished.Remove(index);LastComposedRect=rect;
            FastRemovals++;Recompositions++;Revision++;Recomposed?.Invoke(rect);
        }
        // Caller first applies exactly the same door event to Scene.SetDoors. Unrelated source maps are not stale.
        public virtual void OccludersChanged(IReadOnlyList<string> ids,IReadOnlyList<RectInt> rectangles)
        {
            EnsureAlive();ResetMetrics();
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
                InvalidateDetailMap(index);
                if(fields[index]!=null)fields[index].Stale=true;
                if(built || building)unpublished.Add(index);
                if(!sources.All[index].Enabled)continue;
                region=hasRegion?Union(region,sources.All[index].Texels):sources.All[index].Texels;hasRegion=true;
            }
            if(hasRegion && (built || building))QueueRegion(region);
        }
        private void QueueRegion(RectInt region)
        {
            // An event arriving midway invalidates the unpublished partial result, never the visible sum.
            if(rebuild!=null){pendingRegions.Add(rebuild.Region);ReleaseRebuild();}
            for(int i=pendingRegions.Count-1;i>=0;i--)
                if(pendingRegions[i].Overlaps(region)){region=Union(region,pendingRegions[i]);pendingRegions.RemoveAt(i);}
            pendingRegions.Add(region);
        }
        public bool Step(int maxBuilds)
        {
            EnsureBuilt();if(maxBuilds<=0)throw new ArgumentOutOfRangeException(nameof(maxBuilds));
            int budget=maxBuilds;
            foreach(int index in requestOrder)
            {
                if(budget==0)break;
                if(!requested.Contains(index) || !sources.All[index].Enabled || fields[index]!=null && !fields[index].Stale)continue;
                var field=Ensure(index);ReleaseTemporary(field);budget--;
            }
            while(rebuild!=null || pendingRegions.Count>0)
            {
                if(rebuild==null)
                {
                    var region=pendingRegions[0];pendingRegions.RemoveAt(0);
                    rebuild=new Rebuild{Region=region,Texture=Target(region.width,region.height,GraphicsFormat.R32G32B32A32_SFloat,"Полярный свет: локальная подготовка суммы")};
                    sources.Overlapping(region,rebuild.Sources);
                    compute.SetTexture(clearWorkKernel,"_Work",rebuild.Texture);compute.SetInts("_WorkSize",region.width,region.height);
                    compute.Dispatch(clearWorkKernel,(region.width+7)/8,(region.height+7)/8,1);
                }
                while(rebuild.Next<rebuild.Sources.Count)
                {
                    int index=rebuild.Sources[rebuild.Next];
                    if(!sources.All[index].Enabled){rebuild.Next++;continue;}
                    bool missing=fields[index]==null || fields[index].Stale;
                    if(missing && budget==0)return true;
                    var field=Ensure(index);if(missing)budget--;
                    AddWork(index,field);ReleaseTemporary(field);rebuild.Next++;
                }
                var complete=rebuild.Region;
                Graphics.CopyTexture(rebuild.Texture,0,0,0,0,complete.width,complete.height,sum,0,0,complete.x,complete.y);
                foreach(int index in rebuild.Sources)
                {
                    var r=sources.All[index].Texels;
                    if(complete.xMin<=r.xMin && complete.yMin<=r.yMin && complete.xMax>=r.xMax && complete.yMax>=r.yMax)
                    {unpublished.Remove(index);publishedEnabled[index]=sources.All[index].Enabled;}
                }
                ReleaseRebuild();LastComposedRect=complete;Recompositions++;Revision++;Recomposed?.Invoke(complete);
                if(budget==0)break;
            }
            return HasWork;
        }
        public void Request(IReadOnlyCollection<string> ids)
        {
            EnsureAlive();if(ids==null)throw new ArgumentNullException(nameof(ids));
            requested.Clear();rejectedRequests.Clear();requestOrder.Clear();
            foreach(string id in ids)if(sources.TryGet(id,out int index) && sources.All[index].Enabled && !requestOrder.Contains(index))requestOrder.Add(index);
            requestOrder.Sort();long pinnedBytes=0;
            foreach(int index in requestOrder)
            {
                long bytes=FieldBytes(index);
                if(bytes>cacheBudget-pinnedBytes)
                {
                    rejectedRequests.Add(index);
                    if(warnedRequests.Add(index))Debug.LogWarning($"Полярный свет: поле тени {sources.All[index].Id} не помещается в закреплённый кэш; уже закреплено={pinnedBytes}, поле={bytes}, бюджет={cacheBudget}. Тень этого источника пропущена до уменьшения запроса.");
                    continue;
                }
                requested.Add(index);pinnedBytes+=bytes;
            }
        }
        public bool ReadyOrNone(string id)=>!sources.TryGet(id,out int index) || !sources.All[index].Enabled || rejectedRequests.Contains(index) || !unpublished.Contains(index) && fields[index]!=null && !fields[index].Stale;
        private bool MissingRequested()
        {
            foreach(int index in requested)if(sources.All[index].Enabled && (fields[index]==null || fields[index].Stale))return true;
            return false;
        }
        // Compatibility diagnostics: polar invalidation deliberately uses conservative spatial subscriptions.
        public IReadOnlyList<string> SubscribedDoors(int index)
        {
            EnsureAlive();if(index<0 || index>=sources.Count)throw new ArgumentOutOfRangeException(nameof(index));
            var result=new List<string>();
            foreach(var pair in scene.Doors)if(sources.All[index].Texels.Overlaps(scene.Texels(pair.Value.Bounds)))result.Add(pair.Key);
            result.Sort(StringComparer.Ordinal);return result;
        }
        public void WaitForMeasurements() {EnsureAlive();} // This backend has no readback subscriptions.
        private void AddWork(int index,Field field)
        {
            var rect=sources.All[index].Texels;var clip=Intersect(rebuild.Region,rect);
            compute.SetTexture(addWorkKernel,"_Work",rebuild.Texture);compute.SetTexture(addWorkKernel,"_WorkField",field.Texture);
            compute.SetInts("_WorkOrigin",rebuild.Region.x,rebuild.Region.y);compute.SetInts("_WorkFieldOrigin",rect.x,rect.y);
            compute.SetInts("_WorkClip",clip.xMin,clip.yMin,clip.xMax,clip.yMax);
            compute.Dispatch(addWorkKernel,(clip.width+7)/8,(clip.height+7)/8,1);Touch(field);Adds++;
        }
        private void ReleaseRebuild(){if(rebuild==null)return;LightTextureBudget.Release(rebuild.Texture);rebuild=null;}
        // One-shot/moving source into a caller-owned local field. No sum/cache mutation and no reflected fill.
        public void BuildTransient(LevelLightSource source,RenderTexture target)
        {
            EnsureAlive();
            if(source==null || target==null)throw new ArgumentNullException(source==null?nameof(source):nameof(target));
            if(source.Samples==null || source.Samples.Length==0 || source.Samples.Length>DirectLight.MaximumSamples)
                throw new ArgumentException("Полярный transient: неверное число точек формы "+source.Id);
            if(!float.IsFinite(source.Reach) || source.Reach<OuterPitch || source.Reach>maximumRadius)
                throw new ArgumentException($"Полярный transient {source.Id}: дальность {source.Reach} вне резерва {OuterPitch}…{maximumRadius} м.");
            if(source.Texels.width>target.width || source.Texels.height>target.height)
                throw new ArgumentException($"Полярный transient {source.Id}: поле {source.Texels.width}×{source.Texels.height} больше цели {target.width}×{target.height}.");
            int offset=transientSlot*DirectLight.MaximumSamples;transientSlot=(transientSlot+1)%TransientSlots;
            transientSamples.SetData(source.Samples,0,offset,source.Samples.Length);Configure(source,transientSamples,offset);
            compute.Dispatch(buildKernel,(Angles(source)+63)/64,source.Samples.Length,1);
            compute.SetTexture(fieldKernel,"_Field",target);compute.Dispatch(fieldKernel,(source.Texels.width+7)/8,(source.Texels.height+7)/8,1);TransientBuilds++;
        }
        public bool TryGetField(string id,out Texture texture,out RectInt texels)
        {
            EnsureBuilt();texture=null;texels=default;
            if(!sources.TryGet(id,out int index) || !sources.All[index].Enabled || unpublished.Contains(index) || fields[index]==null || fields[index].Stale)return false;
            Touch(fields[index]);texture=fields[index].Texture;texels=sources.All[index].Texels;return true;
        }
        private Field Ensure(int index)
        {
            var field=fields[index];
            if(field!=null && !field.Stale){Touch(field);return field;}
            var source=sources.All[index];var texels=source.Texels;
            if(field==null)
            {
                long bytes=FieldBytes(index);bool cached=EvictBefore(bytes);
                field=new Field{Texture=Target(texels.width,texels.height,GraphicsFormat.R16G16B16A16_SFloat,"Полярное поле "+source.Id),Stale=true,Temporary=!cached};
                if(cached){field.Lru=lru.AddLast(index);fields[index]=field;cacheBytes+=bytes;CachedFields++;}
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
            compute.SetBuffer(buildKernel,"_Samples",points);compute.SetBuffer(fieldKernel,"_Samples",points);compute.SetBuffer(exactKernel,"_Samples",points);
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
        private bool EvictBefore(long incomingBytes)
        {
            while(cacheBytes+incomingBytes>cacheBudget)
            {
                var candidate=lru.First;
                while(candidate!=null && requested.Contains(candidate.Value))candidate=candidate.Next;
                // Work rectangles stream one contributor at a time. Pinned actor fields need not be evicted
                // or exceed the persistent cache budget for a non-pinned event contributor.
                if(candidate==null)return false;
                int oldest=candidate.Value;lru.Remove(candidate);
                var texture=fields[oldest].Texture;cacheBytes-=(long)texture.width*texture.height*8;LightTextureBudget.Release(texture);fields[oldest]=null;CachedFields--;Evictions++;
            }
            return true;
        }
        private long FieldBytes(int index)=>(long)sources.All[index].Texels.width*sources.All[index].Texels.height*8;
        private static void ReleaseTemporary(Field field){if(field.Temporary)LightTextureBudget.Release(field.Texture);}
        private void Touch(Field field){if(field.Temporary)return;lru.Remove(field.Lru);lru.AddLast(field.Lru);}
        private void ResetMetrics(){LastBuilds=0;LastAffectedSources=0;LastComposedRect=default;lastBuiltIds.Clear();}
        private void EnsureAlive(){if(disposed)throw new ObjectDisposedException(nameof(PolarDirectLight));}
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
            int bytes=format==GraphicsFormat.R32_SFloat?4:format==GraphicsFormat.R32G32B32A32_SFloat?16:8;
            try{LightTextureBudget.Track(target,(long)width*height*slices*bytes);if(!target.Create())throw new InvalidOperationException("Не создана текстура "+name);return target;}
            catch{LightTextureBudget.Release(target);throw;}
        }
#if UNITY_EDITOR
        public readonly struct MapValidation
        {
            public readonly int Samples,PartialSamples,BlockedSamples,ShapeSamples;
            public readonly float MaximumError;
            public MapValidation(int samples,int partial,int blocked,int shapes,float error)
            {Samples=samples;PartialSamples=partial;BlockedSamples=blocked;ShapeSamples=shapes;MaximumError=error;}
        }
        // Blocking offline oracle: checks every retained radial prefix at selected angles of EVERY shape sample.
        public MapValidation ValidateMapAgainstCpu(string id,float[] absorption,int angleStride=53,float tolerance=.002f)
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
        public RenderTexture ExactFieldForValidation(string id)
        {
            EnsureBuilt();if(!sources.TryGet(id,out int index))throw new ArgumentException("Полярный стационарный свет: нет источника "+id);
            Configure(index);var rect=sources.All[index].Texels;
            var target=Target(rect.width,rect.height,GraphicsFormat.R16G16B16A16_SFloat,"Полярный свет: точный контроль "+id);
            try{compute.SetTexture(exactKernel,"_Field",target);compute.Dispatch(exactKernel,(rect.width+7)/8,(rect.height+7)/8,1);return target;}
            catch{LightTextureBudget.Release(target);throw;}
        }
#endif
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            ReleaseDetailMaps();
            ReleaseRebuild();pendingRegions.Clear();requested.Clear();
            foreach(var field in fields)if(field!=null)LightTextureBudget.Release(field.Texture);
            LightTextureBudget.Release(map);LightTextureBudget.Release(sum);map=sum=null;cacheBytes=0;CachedFields=0;lru.Clear();
            samples?.Dispose();marchDistances?.Dispose();transientSamples?.Dispose();DestroyOwned(compute);DestroyOwned(composition);
        }
        private static void DestroyOwned(Object value){if(value==null)return;if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
