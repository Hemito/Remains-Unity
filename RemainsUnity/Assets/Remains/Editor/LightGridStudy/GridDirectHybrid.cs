using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Editor-only study: independent 20cm directional direct light, plus a weak hue-preserving grid FLOOR.
    // Reuses production DirectLight and absorption rules; no runtime installation or bounce solver.
    public sealed class GridDirectHybrid : IDisposable
    {
        private const string ComputePath="Assets/Remains/Editor/LightGridStudy/GridHybridComposite.compute";
        private const long DirectCacheBytes=64L*1024*1024;
        private readonly List<string> changedDoorIds=new();
        private readonly Dictionary<string,int> doorById=new(StringComparer.Ordinal);
        private LevelLightScene.Door[] doors;
        private ComputeShader compute;
        private Texture2D fence;
        private int kernel;
        private bool built,disposed,hasCompositeRegion;
        private RectInt compositeRegion;
        private long sourceArrayBytes,seedArrayBytes;
        public LevelLightScene Scene {get;private set;}
        public LevelLightSources Sources {get;private set;}
        public DirectLight Direct {get;private set;}
        public GridLightField Field {get;private set;}
        public GridLightSeeds SeedIndex {get;private set;}
        public float[] Absorption {get;private set;}
        public GridLightTexture GridTexture {get;private set;}
        public RenderTexture Texture {get;private set;}
        public float FillGain {get;private set;}=.08f;
        public long LastGridCells {get;private set;}
        public int LastDirectBuilds {get;private set;}
        public RectInt LastComposedRect {get;private set;}
        public long GpuBytes=>disposed?0:Scene.Bytes+Direct.SumBytes+Direct.CacheBytes+(GridTexture?.Bytes??0)+(long)Scene.Width*Scene.Height*8;
        // Array payloads, including cloned shape points and per-source seed arrays; excludes object/list/dictionary
        // headers and the seed bucket index, just as the other Editor studies' payload accounting does.
        public long CpuBytes=>disposed?0:Field.AllocatedBytes+Absorption.LongLength*4+sourceArrayBytes+seedArrayBytes;

        public GridDirectHybrid(LevelLightScene originalFineScene,IReadOnlyList<LevelLightSource> originalSources)
        {
            if(originalFineScene==null || originalSources==null) throw new ArgumentNullException("Исходные данные гибридного света отсутствуют.");
            if(originalFineScene.Resolution%2!=0 || originalFineScene.Width%2!=0 || originalFineScene.Height%2!=0)
                throw new ArgumentException("Гибрид света: исходная сетка и разрешение должны делиться на два.");
            try
            {
                int width=originalFineScene.Width/2,height=originalFineScene.Height/2;
                var originalDoors=originalFineScene.Doors.Values.ToArray();
                var openDoors=originalDoors.Select(d=>new LevelLightScene.Door(d.Id,d.Bounds,d.Opacity,false)).ToArray();
                float[] baseAbsorption;
                // Obtain BASE walls/objects. A closed fine door must not be permanently baked into coarse walls.
                // This mutates the input scene only during construction, always restoring its original doors.
                try
                {
                    originalFineScene.SetDoors(openDoors,null);
                    baseAbsorption=GridLightTexture.ReadAbsorption(originalFineScene,2,new RectInt(0,0,width,height));
                }
                finally {originalFineScene.SetDoors(originalDoors,null);}
                var walls=new float[width,height];
                for(int y=0;y<height;y++) for(int x=0;x<width;x++) walls[x,y]=baseAbsorption[y*width+x];
                Scene=new LevelLightScene(walls,originalFineScene.WorldRect.position,originalFineScene.Resolution/2,Array.Empty<LevelLightScene.ObjectSprite>());
                doors=originalDoors;Scene.SetDoors(doors,null);
                for(int i=0;i<doors.Length;i++) doorById.Add(doors[i].Id,i);
                Absorption=GridLightTexture.ReadAbsorption(Scene,1,new RectInt(0,0,width,height));
                var clones=new List<LevelLightSource>(originalSources.Count);
                foreach(var source in originalSources)
                {
                    if(source==null) throw new ArgumentException("Гибрид света: пустой источник в каталоге.");
                    var samples=ReduceShape(source.Samples,source.Id);sourceArrayBytes+=samples.LongLength*16;
                    int x=source.Texels.xMin/2,y=source.Texels.yMin/2;
                    var texels=new RectInt(x,y,(source.Texels.xMax+1)/2-x,(source.Texels.yMax+1)/2-y);
                    clones.Add(new LevelLightSource(source.Id,source.Kind,samples,source.Radiance,source.Width,source.Reach,source.Bounds,texels,source.Enabled));
                }
                Sources=new LevelLightSources(clones,width,height);
                // Prepare placements for disabled sources too: later SetLamp(on) must have real cached positions.
                var enabled=clones.Select(s=>s.Enabled).ToArray();
                foreach(var source in clones) source.Enabled=true;
                GridLightPlacement.Result placement;
                try {placement=GridLightPlacement.Build(Scene,Sources.All,Absorption,1);}
                finally {for(int i=0;i<clones.Count;i++) clones[i].Enabled=enabled[i];}
                SeedIndex=new GridLightSeeds(width,placement.BySource);
                foreach(var pair in placement.BySource) seedArrayBytes+=pair.Value.LongLength*20;
                foreach(var source in clones) if(!source.Enabled) SeedIndex.SetEnabled(source.Id,false);
                Field=new GridLightField(width,height,Scene.Texel,Absorption);
                Direct=new DirectLight(Scene,Sources,DirectCacheBytes);Direct.Recomposed+=IncludeComposite;
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
                if(shader==null) throw new InvalidOperationException("Гибрид света: отсутствует Editor compute "+ComputePath);
                compute=Object.Instantiate(shader);compute.name="Гибрид света: независимый композит";kernel=compute.FindKernel("Compose");
                Texture=new RenderTexture(width,height,0,GraphicsFormat.R16G16B16A16_SFloat)
                    {name="Гибрид света: прямой свет и слабое заполнение",enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                if(!Texture.Create()) throw new InvalidOperationException("Гибрид света: не создана текстура композита.");
                fence=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            }
            catch {Dispose();throw;}
        }

        public void BuildAll()
        {
            EnsureAlive();ResetMetrics();int before=Direct.Builds;
            Direct.BuildAll();Direct.WaitForMeasurements();Fence(Direct.Sum);
            Field.Solve(SeedIndex.Query(AllCells));
            if(GridTexture==null) GridTexture=new GridLightTexture(Field);else GridTexture.Update(Field,AllCells);
            LastGridCells=Field.RecomputedCells;LastDirectBuilds=Direct.Builds-before;built=true;
            Composite(AllCells);
        }
        public void SetLamp(string id,bool enabled)
        {
            EnsureBuilt();ResetMetrics();
            if(!Sources.TryGet(id,out int index) || Sources.All[index].Kind!=LevelLightSourceKind.Lamp)
                throw new ArgumentException("Гибрид света: неизвестная лампа "+id);
            var source=Sources.All[index];if(source.Enabled==enabled) return;
            int before=Direct.Builds;source.Enabled=enabled;Direct.SourceChanged(index);SeedIndex.SetEnabled(id,enabled);
            FinishDirect();UpdateGrid(SeedIndex.Bounds(id));LastDirectBuilds=Direct.Builds-before;
            CompositePending();
        }
        public void SetDoor(string id,bool blocks)
        {
            EnsureBuilt();ResetMetrics();
            if(!doorById.TryGetValue(id,out int index)) throw new ArgumentException("Гибрид света: неизвестная дверь "+id);
            var old=doors[index];if(old.Blocks==blocks) return;
            doors[index]=new LevelLightScene.Door(old.Id,old.Bounds,old.Opacity,blocks);
            int before=Direct.Builds;var rectangles=Scene.SetDoors(doors,changedDoorIds);
            Direct.OccludersChanged(changedDoorIds,rectangles);FinishDirect();
            var changed=Scene.Texels(old.Bounds);
            GridLightTexture.PatchAbsorption(Absorption,Field.Width,changed,GridLightTexture.ReadAbsorption(Scene,1,changed));
            UpdateGrid(changed);LastDirectBuilds=Direct.Builds-before;CompositePending();
        }
        // Explicit preview command. A gain change affects every output pixel, so this intentionally recomposes all.
        public void Compose(float fillGain=.08f)
        {
            EnsureBuilt();if(!float.IsFinite(fillGain) || fillGain<0 || fillGain>1) throw new ArgumentOutOfRangeException(nameof(fillGain),"Гибрид света: коэффициент заполнения должен быть от0 до1.");
            FillGain=fillGain;ResetMetrics();Composite(AllCells);
        }
        private RectInt AllCells=>new RectInt(0,0,Scene.Width,Scene.Height);
        private void UpdateGrid(RectInt changed)
        {
            var affected=Field.Update(SeedIndex.Query(Field.AffectedRegion(changed)),changed);
            GridTexture.Update(Field,affected);LastGridCells=Field.RecomputedCells;IncludeComposite(affected);
        }
        private void FinishDirect()
        {
            int steps=0;while(Direct.HasWork) if(Direct.Step(64) && ++steps>100000) throw new InvalidOperationException("Гибрид света: прямой свет не завершил локальное событие.");
            Direct.WaitForMeasurements();
        }
        private void ResetMetrics() {LastGridCells=0;LastDirectBuilds=0;LastComposedRect=new RectInt();hasCompositeRegion=false;}
        private void IncludeComposite(RectInt region)
        {
            if(region.width<=0 || region.height<=0) return;
            if(!hasCompositeRegion) {compositeRegion=region;hasCompositeRegion=true;return;}
            int x=Math.Min(region.xMin,compositeRegion.xMin),y=Math.Min(region.yMin,compositeRegion.yMin);
            compositeRegion=new RectInt(x,y,Math.Max(region.xMax,compositeRegion.xMax)-x,Math.Max(region.yMax,compositeRegion.yMax)-y);
        }
        private void CompositePending() {if(hasCompositeRegion) Composite(compositeRegion);}
        private void Composite(RectInt area)
        {
            compute.SetTexture(kernel,"_Direct",Direct.Sum);compute.SetTexture(kernel,"_Grid",GridTexture.Texture);compute.SetTexture(kernel,"_Output",Texture);
            compute.SetInts("_Clip",area.xMin,area.yMin,area.xMax,area.yMax);compute.SetFloat("_FillGain",FillGain);
            compute.Dispatch(kernel,(area.width+7)/8,(area.height+7)/8,1);Fence(Texture);
            LastComposedRect=area;hasCompositeRegion=false;
        }
        private void Fence(UnityEngine.Texture target)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=(RenderTexture)target;fence.ReadPixels(new Rect(0,0,1,1),0,0,false);}
            finally {RenderTexture.active=previous;}
        }
        private void EnsureAlive() {if(disposed) throw new ObjectDisposedException(nameof(GridDirectHybrid));}
        private void EnsureBuilt() {EnsureAlive();if(!built) throw new InvalidOperationException("Гибрид света: сначала требуется BuildAll.");}

        // Deterministic weighted spatial k-means. A cluster is replaced by its total weight and weighted centroid;
        // the sum of weights and the whole form's centroid are preserved, unlike simply dropping four samples.
        private static Vector4[] ReduceShape(Vector4[] points,string id)
        {
            var normalized=LevelLightSources.Normalise(points,id);int n=normalized.Length,k=Math.Min(4,n);
            if(n<=4) return normalized;
            var centers=new Vector2[k];var selected=new bool[n];int first=0;
            for(int i=1;i<n;i++) if(normalized[i].z>normalized[first].z) first=i;
            centers[0]=new Vector2(normalized[first].x,normalized[first].y);selected[first]=true;
            for(int c=1;c<k;c++)
            {
                int best=-1;double score=-1;
                for(int i=0;i<n;i++) if(!selected[i])
                {
                    var p=new Vector2(normalized[i].x,normalized[i].y);double distance=double.PositiveInfinity;
                    for(int j=0;j<c;j++) distance=Math.Min(distance,(p-centers[j]).sqrMagnitude);
                    double value=distance*normalized[i].z;if(value>score) {score=value;best=i;}
                }
                selected[best]=true;centers[c]=new Vector2(normalized[best].x,normalized[best].y);
            }
            var weights=new double[k];var xs=new double[k];var ys=new double[k];
            for(int iteration=0;iteration<12;iteration++)
            {
                Array.Clear(weights,0,k);Array.Clear(xs,0,k);Array.Clear(ys,0,k);
                for(int i=0;i<n;i++)
                {
                    var p=new Vector2(normalized[i].x,normalized[i].y);int best=0;float distance=(p-centers[0]).sqrMagnitude;
                    for(int c=1;c<k;c++) {float d=(p-centers[c]).sqrMagnitude;if(d<distance) {distance=d;best=c;}}
                    weights[best]+=normalized[i].z;xs[best]+=normalized[i].x*(double)normalized[i].z;ys[best]+=normalized[i].y*(double)normalized[i].z;
                }
                float movement=0;
                for(int c=0;c<k;c++) if(weights[c]>0)
                {
                    var center=new Vector2((float)(xs[c]/weights[c]),(float)(ys[c]/weights[c]));movement=Math.Max(movement,(center-centers[c]).sqrMagnitude);centers[c]=center;
                }
                if(movement<.0000000001f) break;
            }
            var output=new List<Vector4>(k);
            for(int c=0;c<k;c++) if(weights[c]>0) output.Add(new Vector4(centers[c].x,centers[c].y,(float)weights[c],0));
            var result=LevelLightSources.Normalise(output,id);Vector2 before=Vector2.zero,after=Vector2.zero;
            foreach(var p in normalized) before+=new Vector2(p.x,p.y)*p.z;
            foreach(var p in result) after+=new Vector2(p.x,p.y)*p.z;
            if((before-after).sqrMagnitude>.000001f) throw new InvalidOperationException($"Гибрид света: кластеризация изменила центр формы источника {id}: {before} → {after}.");
            return result;
        }
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            if(Direct!=null) {Direct.Recomposed-=IncludeComposite;Direct.Dispose();Direct=null;}
            GridTexture?.Dispose();GridTexture=null;Scene?.Dispose();Scene=null;
            if(Texture!=null) {Texture.Release();Object.DestroyImmediate(Texture);Texture=null;}
            if(compute!=null) Object.DestroyImmediate(compute);compute=null;
            if(fence!=null) Object.DestroyImmediate(fence);fence=null;
        }
    }
}
