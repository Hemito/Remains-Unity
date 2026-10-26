using System;
using System.Collections;
using System.Collections.Generic;
using Remains.Content;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Direct light of a level: one field per steady source (its reach rectangle on the full grid), a bounded cache
    // of those fields and their sum over the whole level. Work follows events, not the camera:
    // - a door change rebuilds only the sources subscribed to that door (the door lies in their light; measured on
    //   the GPU after each build, conservative until the measurement arrives);
    // - a lamp switch changes only its own reach rectangle;
    // - a changed region of the sum is cleared and the overlapping fields are added again in source order, so the
    //   result equals a full rebuild exactly. The old sum stays shown until every field the region needs is ready.
    public sealed class DirectLight : IDisposable
    {
        public const int MaximumSamples=8;
        public const long DefaultCacheBytes=256L*1024*1024;
        private const string ComputeResource="Presentation/DirectLight";
        // Light above this inside a door's rectangle subscribes the source to that door.
        private const float LitThreshold=1e-6f;
        private sealed class Field
        {
            public RenderTexture Texture;
            public bool Stale;
            public long LastUse;
        }
        private readonly LevelLightScene scene;
        private readonly LevelLightSources sources;
        private readonly long cacheBudget;
        private int[] sampleOffsets;
        private ComputeShader compute;
        private int directKernel,clearKernel,addKernel,litKernel;
        private ComputeBuffer samples,doorRects,lit;
        private readonly List<ComputeBuffer> retired=new();
        // Door rectangles uploaded once per door set; one readback slot of door maxima per measurement.
        private string[] doorOrder=Array.Empty<string>();
        private const int LitSlots=32;
        private readonly AsyncGPUReadbackRequest?[] slotRequests=new AsyncGPUReadbackRequest?[LitSlots];
        private int nextSlot;
        private RenderTexture sum;
        private readonly Field[] fields;
        private long cacheBytes,useClock;
        // Door ID → sources whose light reaches it; per source the doors it subscribed to and whether that is measured.
        private readonly Dictionary<string,HashSet<int>> subscribers=new(StringComparer.Ordinal);
        private readonly List<string>[] subscribed;
        private readonly bool[] measured;
        private readonly int[] buildGeneration;
        private readonly bool[] dirty;
        private int dirtyCount;
        private readonly List<RectInt> pendingRegions=new();
        private readonly List<int> overlap=new();
        private bool built,disposed;
        public Texture Sum=>sum;
        public long CacheBytes=>cacheBytes;
        public long SumBytes=>sum==null?0:LightTextureBudget.Size(sum.width,sum.height,8);
        public int Builds {get;private set;}
        public int Adds {get;private set;}
        public int Evictions {get;private set;}
        public int Recompositions {get;private set;}
        public int PendingMeasurements {get;private set;}
        public bool HasWork=>dirtyCount>0 || pendingRegions.Count>0 || MissingRequested();
        // Changes whenever the sum changes (a full build or a recomposed region).
        public long Revision {get;private set;}
        // Fields kept for actor shadows: built when missing, never evicted while requested.
        private readonly HashSet<int> requested=new();
        // A region of the sum changed (after an event, not the full build): bounce light follows it.
        public event Action<RectInt> Recomposed;
        public int CachedFields {get;private set;}

        public DirectLight(LevelLightScene scene,LevelLightSources sources,long cacheBudget=DefaultCacheBytes)
        {
            this.scene=scene??throw new ArgumentNullException(nameof(scene));
            this.sources=sources??throw new ArgumentNullException(nameof(sources));
            if(cacheBudget<=0) throw new ArgumentOutOfRangeException(nameof(cacheBudget));
            this.cacheBudget=cacheBudget;
            int count=sources.Count;
            fields=new Field[count];subscribed=new List<string>[count];measured=new bool[count];buildGeneration=new int[count];dirty=new bool[count];
            try
            {
                if(!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Прямой свет уровня: видеокарта/API без compute-шейдеров.");
                compute=GameAssets.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
                directKernel=compute.FindKernel("Direct");clearKernel=compute.FindKernel("ClearRect");
                addKernel=compute.FindKernel("AddField");litKernel=compute.FindKernel("LitRects");
                // All sources' form points in one buffer, uploaded once (each source reads its own slice).
                var points=new List<Vector4>(sources.Count*MaximumSamples);
                sampleOffsets=new int[sources.Count];
                for(int i=0;i<sources.Count;i++) {sampleOffsets[i]=points.Count;points.AddRange(sources.All[i].Samples);}
                samples=new ComputeBuffer(Math.Max(1,points.Count),16);samples.SetData(points);
                sum=Target(scene.Width,scene.Height,"Свет уровня: сумма прямого света");
            }
            catch {Dispose();throw;}
        }

        // The whole level: every enabled source built and added in order (one build per MoveNext). Shown only when done.
        public IEnumerator BuildAllSteps()
        {
            ThrowIfDisposed();
            // Events before or during the build: sources built after them already see them, earlier ones are
            // invalidated by the events themselves.
            Array.Clear(dirty,0,dirty.Length);dirtyCount=0;pendingRegions.Clear();
            Clear(new RectInt(0,0,scene.Width,scene.Height));
            for(int i=0;i<sources.Count;i++)
            {
                var source=sources.All[i];
                if(!source.Enabled) continue;
                var field=Ensure(i);
                Add(i,field,source.Texels);
                EvictOverBudget(null);
                yield return null;
            }
            built=true;Revision++;
        }
        public void BuildAll()
        {
            var steps=BuildAllSteps();
            try {while(steps.MoveNext()) {}} finally {(steps as IDisposable)?.Dispose();}
        }

        // Doors whose blocking changed (LevelLightScene.SetDoors): their subscribed sources are rebuilt.
        public void OccludersChanged(IReadOnlyList<string> ids,IReadOnlyList<RectInt> texels)
        {
            ThrowIfDisposed();
            if(ids==null || texels==null || ids.Count!=texels.Count) throw new ArgumentException("Прямой свет: изменения дверей без ID или прямоугольников.");
            for(int k=0;k<ids.Count;k++)
            {
                if(subscribers.TryGetValue(ids[k],out var set)) foreach(int index in set) Invalidate(index);
                // Not yet measured sources take every door inside their reach.
                sources.Overlapping(texels[k],overlap);
                foreach(int index in overlap) if(!measured[index]) Invalidate(index);
            }
        }
        // A lamp switched on or off (LevelLightSource.Enabled already set): only its reach rectangle changes.
        public void SourceChanged(int index)
        {
            ThrowIfDisposed();
            var source=sources.All[index];
            if(source.Enabled && (fields[index]==null || fields[index].Stale)) MarkDirty(index);
            pendingRegions.Add(source.Texels);
        }
        private void Invalidate(int index)
        {
            if(fields[index]!=null) fields[index].Stale=true;
            measured[index]=false;
            if(sources.All[index].Enabled) MarkDirty(index);
            pendingRegions.Add(sources.All[index].Texels);
        }
        private void MarkDirty(int index) {if(!dirty[index]) {dirty[index]=true;dirtyCount++;}}

        // Event work, at most maxBuilds field builds per call (each ~1–3 ms of video card). Regions are recomposed
        // only when all their fields are ready, all at once.
        public bool Step(int maxBuilds)
        {
            ThrowIfDisposed();
            if(maxBuilds<=0) throw new ArgumentOutOfRangeException(nameof(maxBuilds));
            if(!built) throw new InvalidOperationException("Прямой свет: шаг события до полной сборки уровня.");
            int budget=maxBuilds;
            // Requested fields first: an actor's shadow waits for them; an event's region waits anyway.
            foreach(int index in requested)
            {
                if(budget==0) break;
                if(!sources.All[index].Enabled || fields[index]!=null && !fields[index].Stale || dirty[index]) continue;
                Build(index);budget--;
            }
            for(int i=0;i<sources.Count && dirtyCount>0 && budget>0;i++)
            {
                if(!dirty[i]) continue;
                dirty[i]=false;dirtyCount--;
                if(!sources.All[i].Enabled) continue;
                Build(i);budget--;
            }
            if(dirtyCount>0 || pendingRegions.Count==0) return HasWork;
            // Fields the regions need but the cache dropped.
            foreach(var region in pendingRegions)
            {
                sources.Overlapping(region,overlap);
                foreach(int index in overlap)
                {
                    if(!sources.All[index].Enabled || fields[index]!=null && !fields[index].Stale) continue;
                    if(budget==0) return true;
                    Build(index);budget--;
                }
            }
            using(Remains.Diagnostics.FrameHitchLog.Section("Свет уровня: пересборка суммы прямого света"))
            {
                foreach(var region in pendingRegions) Recompose(region);
                foreach(var region in pendingRegions) Recomposed?.Invoke(region);
                pendingRegions.Clear();Revision++;
            }
            EvictOverBudget(requested);
            return HasWork;
        }

        // The sources whose fields actor shadows need now (ids of other kinds or unknown ids are ignored).
        public void Request(IReadOnlyCollection<string> ids)
        {
            ThrowIfDisposed();
            requested.Clear();
            foreach(string id in ids) if(sources.TryGet(id,out int index)) requested.Add(index);
        }
        // Whether a source can cast its shadow now: its field is ready, or it emits nothing / is not a level source.
        public bool ReadyOrNone(string id)=>!sources.TryGet(id,out int index) || !sources.All[index].Enabled || fields[index]!=null && !fields[index].Stale;
        private bool MissingRequested()
        {
            foreach(int index in requested) if(sources.All[index].Enabled && (fields[index]==null || fields[index].Stale)) return true;
            return false;
        }

        // A one-frame source (a muzzle flash) into a caller's target at least its reach rectangle in size; no cache,
        // no subscription. Up to TransientSlots per frame keep their own form points.
        public void BuildTransient(LevelLightSource source,RenderTexture target)
        {
            ThrowIfDisposed();
            if(source==null || target==null) throw new ArgumentNullException(source==null?nameof(source):nameof(target));
            var texels=source.Texels;
            if(texels.width>target.width || texels.height>target.height) throw new ArgumentException($"Вспышка {source.Id}: поле {texels.width}×{texels.height} больше цели {target.width}×{target.height}.");
            if(source.Samples.Length>MaximumSamples) throw new ArgumentException("Вспышка: слишком много точек формы "+source.Id);
            int slot=transientSlot;transientSlot=(transientSlot+1)%TransientSlots;
            transientSamples??=new ComputeBuffer(TransientSlots*MaximumSamples,16);
            transientSamples.SetData(source.Samples,0,slot*MaximumSamples,source.Samples.Length);
            Dispatch(source,texels,target,transientSamples,slot*MaximumSamples);
        }
        private const int TransientSlots=16;
        private ComputeBuffer transientSamples;
        private int transientSlot;

        public bool TryGetField(string id,out Texture texture,out RectInt texels)
        {
            texture=null;texels=default;
            if(!sources.TryGet(id,out int index) || fields[index]==null || fields[index].Stale) return false;
            fields[index].LastUse=++useClock;texture=fields[index].Texture;texels=sources.All[index].Texels;return true;
        }
        // Doors a source is subscribed to (diagnostics and checks); null while its measurement is pending.
        public IReadOnlyList<string> SubscribedDoors(int index)=>measured[index]?subscribed[index]:null;
        public void WaitForMeasurements() {AsyncGPUReadback.WaitAllRequests();}

        private void Recompose(RectInt region)
        {
            Clear(region);
            sources.Overlapping(region,overlap);
            foreach(int index in overlap)
            {
                if(!sources.All[index].Enabled) continue;
                var field=fields[index];
                if(field==null || field.Stale) throw new InvalidOperationException("Прямой свет: пересборка области без готового поля "+sources.All[index].Id);
                var clip=Intersect(region,sources.All[index].Texels);
                if(clip.width>0 && clip.height>0) Add(index,field,clip);
            }
            Recompositions++;
        }
        private Field Ensure(int index)
        {
            var field=fields[index];
            if(field!=null && !field.Stale) {field.LastUse=++useClock;return field;}
            return Build(index);
        }
        private Field Build(int index)
        {
            var source=sources.All[index];var texels=source.Texels;
            var field=fields[index];
            if(field==null)
            {
                field=new Field {Texture=Target(texels.width,texels.height,"Прямой свет "+source.Id)};
                fields[index]=field;cacheBytes+=LightTextureBudget.Size(texels.width,texels.height,8);CachedFields++;
            }
            Remains.Diagnostics.FrameHitchLog.Gpu("Свет уровня: прямой свет источника");
            using(Remains.Diagnostics.FrameHitchLog.Section("Свет уровня: прямой свет источника")) Dispatch(source,texels,field.Texture,samples,sampleOffsets[index]);
            field.Stale=false;field.LastUse=++useClock;Builds++;
            Measure(index,field);
            return field;
        }

        private void Dispatch(LevelLightSource source,RectInt texels,RenderTexture target,ComputeBuffer points,int offset)
        {
            compute.SetTexture(directKernel,"_Scene",scene.Texture);
            compute.SetBuffer(directKernel,"_Samples",points);
            compute.SetTexture(directKernel,"_Field",target);
            compute.SetInts("_SceneSize",scene.Width,scene.Height);
            compute.SetVector("_SceneRect",new Vector4(scene.WorldRect.x,scene.WorldRect.y,scene.WorldRect.width,scene.WorldRect.height));
            compute.SetFloat("_PPU",scene.Resolution);
            compute.SetInts("_FieldOrigin",texels.x,texels.y);compute.SetInts("_FieldSize",texels.width,texels.height);
            compute.SetVector("_SourceBounds",new Vector4(source.Bounds.xMin,source.Bounds.yMin,source.Bounds.xMax,source.Bounds.yMax));
            compute.SetVector("_Radiance",source.Radiance);compute.SetFloat("_Radius",source.Reach);
            compute.SetFloat("_ProjectedWidth",source.Width);compute.SetInt("_SampleCount",source.Samples.Length);compute.SetInt("_SampleOffset",offset);
            compute.Dispatch(directKernel,(texels.width+7)/8,(texels.height+7)/8,1);
        }

        // Which doors this field lights: read back asynchronously; until then the source counts as subscribed to
        // every door in its reach.
        private void Measure(int index,Field field)
        {
            Unsubscribe(index);
            measured[index]=false;
            int generation=++buildGeneration[index];
            var texels=sources.All[index].Texels;
            UploadDoors();
            if(doorOrder.Length==0) {subscribed[index]=new List<string>();measured[index]=true;return;}
            int slot=nextSlot;nextSlot=(nextSlot+1)%LitSlots;
            // A slot is reused only after its previous readback finished (the Editor builds a level without frames).
            if(slotRequests[slot] is AsyncGPUReadbackRequest previous && !previous.done) previous.WaitForCompletion();
            compute.SetBuffer(litKernel,"_Rects",doorRects);compute.SetBuffer(litKernel,"_Lit",lit);
            compute.SetTexture(litKernel,"_FieldIn",field.Texture);
            compute.SetInts("_FieldOrigin",texels.x,texels.y);compute.SetInts("_FieldSize",texels.width,texels.height);
            compute.SetInt("_LitOffset",slot*doorOrder.Length);
            compute.Dispatch(litKernel,doorOrder.Length,1,1);
            var ids=doorOrder;PendingMeasurements++;
            slotRequests[slot]=AsyncGPUReadback.Request(lit,ids.Length*4,slot*ids.Length*4,request=>
            {
                PendingMeasurements--;
                if(disposed || generation!=buildGeneration[index]) return;
                if(request.hasError) {Debug.LogWarning($"Прямой свет: подписка источника {sources.All[index].Id} не прочитана; источник остаётся подписан на все двери в своей дальности.");return;}
                var values=request.GetData<uint>();var list=new List<string>();
                for(int k=0;k<ids.Length;k++) if(BitConverter.Int32BitsToSingle((int)values[k])>LitThreshold) list.Add(ids[k]);
                subscribed[index]=list;measured[index]=true;
                foreach(string id in list)
                {
                    if(!subscribers.TryGetValue(id,out var set)) subscribers.Add(id,set=new HashSet<int>());
                    set.Add(index);
                }
            });
        }
        // Door rectangles change only with the door set (ids and bounds), not with their open state.
        private void UploadDoors()
        {
            var doors=scene.Doors;
            bool same=doorOrder.Length==doors.Count;
            if(same) foreach(string id in doorOrder) if(!doors.ContainsKey(id)) {same=false;break;}
            if(same && doorRects!=null && uploadedDoorRevision==scene.DoorSetRevision) return;
            var ids=new List<string>(doors.Keys);ids.Sort(StringComparer.Ordinal);
            var rects=new TexelRect[ids.Count];
            for(int k=0;k<ids.Count;k++) {var t=scene.Texels(doors[ids[k]].Bounds);rects[k]=new TexelRect {x=t.x,y=t.y,width=t.width,height=t.height};}
            if(doorRects!=null) retired.Add(doorRects);
            if(lit!=null) retired.Add(lit);
            doorRects=lit=null;doorOrder=ids.ToArray();uploadedDoorRevision=scene.DoorSetRevision;
            if(ids.Count==0) return;
            doorRects=new ComputeBuffer(ids.Count,16);doorRects.SetData(rects);
            lit=new ComputeBuffer(ids.Count*LitSlots,4);
        }
        private long uploadedDoorRevision=-1;
        private struct TexelRect {public int x,y,width,height;}
        private void Unsubscribe(int index)
        {
            var list=subscribed[index];if(list==null) return;
            foreach(string id in list) if(subscribers.TryGetValue(id,out var set)) set.Remove(index);
            subscribed[index]=null;
        }

        private void Clear(RectInt rect)
        {
            if(rect.width<=0 || rect.height<=0) return;
            compute.SetTexture(clearKernel,"_Sum",sum);
            compute.SetInts("_Clip",rect.xMin,rect.yMin,rect.xMax,rect.yMax);
            compute.Dispatch(clearKernel,(rect.width+7)/8,(rect.height+7)/8,1);
        }
        private void Add(int index,Field field,RectInt clip)
        {
            var texels=sources.All[index].Texels;
            compute.SetTexture(addKernel,"_Sum",sum);compute.SetTexture(addKernel,"_FieldIn",field.Texture);
            compute.SetInts("_FieldOrigin",texels.x,texels.y);
            compute.SetInts("_Clip",clip.xMin,clip.yMin,clip.xMax,clip.yMax);
            compute.Dispatch(addKernel,(clip.width+7)/8,(clip.height+7)/8,1);
            field.LastUse=++useClock;Adds++;
        }
        // Least recently used fields go first; a pending region's fields stay (keep may be null: nothing pending).
        private void EvictOverBudget(HashSet<int> keep)
        {
            while(cacheBytes>cacheBudget)
            {
                int oldest=-1;long best=long.MaxValue;
                for(int i=0;i<fields.Length;i++)
                    if(fields[i]!=null && fields[i].LastUse<best && (keep==null || !keep.Contains(i))) {best=fields[i].LastUse;oldest=i;}
                if(oldest<0) return;
                var texels=sources.All[oldest].Texels;
                LightTextureBudget.Release(fields[oldest].Texture);fields[oldest]=null;
                cacheBytes-=LightTextureBudget.Size(texels.width,texels.height,8);CachedFields--;Evictions++;
            }
        }
        private static RectInt Intersect(RectInt a,RectInt b)
        {
            int x0=Math.Max(a.xMin,b.xMin),y0=Math.Max(a.yMin,b.yMin),x1=Math.Min(a.xMax,b.xMax),y1=Math.Min(a.yMax,b.yMax);
            return new RectInt(x0,y0,Math.Max(0,x1-x0),Math.Max(0,y1-y0));
        }
        private static RenderTexture Target(int width,int height,string name)
        {
            var texture=new RenderTexture(width,height,0,GraphicsFormat.R16G16B16A16_SFloat) {name=name,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            try
            {
                LightTextureBudget.Track(texture,LightTextureBudget.Size(width,height,8));
                if(!texture.Create()) throw new InvalidOperationException("Не создана текстура прямого света "+name);
                return texture;
            }
            catch {LightTextureBudget.Release(texture);throw;}
        }
        private void ThrowIfDisposed() {if(disposed) throw new ObjectDisposedException(nameof(DirectLight));}
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            for(int i=0;i<fields.Length;i++) if(fields[i]!=null) {LightTextureBudget.Release(fields[i].Texture);fields[i]=null;}
            cacheBytes=0;CachedFields=0;
            LightTextureBudget.Release(sum);sum=null;
            AsyncGPUReadback.WaitAllRequests();
            samples?.Release();doorRects?.Release();lit?.Release();transientSamples?.Release();samples=doorRects=lit=transientSamples=null;
            foreach(var buffer in retired) buffer.Release();
            retired.Clear();
        }
    }
}
