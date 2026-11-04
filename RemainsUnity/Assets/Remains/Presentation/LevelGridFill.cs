using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Presentation
{
    // Bounded max-path fill on a coarse world grid, reconstructed once into a world-aligned fine texture.
    // A camera only samples Texture. Events update local coarse regions and their fine support; no camera work.
    public sealed class LevelGridFill : IDisposable
    {
        private const int Divisor=2,TileSize=128;
        private readonly LevelLightScene fine;
        private readonly LevelLightSources liveSources;
        private readonly HashSet<int> changedSources=new();
        private readonly List<RectInt> changedGeometry=new();
        private readonly Queue<RectInt> coarseWork=new(),fineWork=new();
        private readonly List<int> nearbySources=new();
        private readonly Dictionary<string,GridLightSeed[]> seedsBySource;
        private readonly GridFillReconstruction reconstruction;
        private GridLightTexture gridTexture;
        private long sourceArrayBytes;
        private bool built,disposed,diagnosticsPending;
        public LevelLightScene CoarseScene {get;private set;}
        public LevelLightSources Sources {get;private set;}
        public GridLightField Field {get;private set;}
        public GridLightSeeds SeedIndex {get;private set;}
        public float[] Absorption {get;private set;}
        public RenderTexture Texture=>reconstruction.Texture;
        public Texture CoarseTexture=>gridTexture?.Texture;
        public Rect WorldRect=>fine.WorldRect;
        public long Revision {get;private set;}
        public long LastGridCells {get;private set;}
        public int LastReconstructedTiles {get;private set;}
        public RectInt LastChangedRect {get;private set;}
        public RectInt LastReconstructedRect {get;private set;}
        public bool HasWork=>!disposed && (changedSources.Count>0 || changedGeometry.Count>0 || coarseWork.Count>0 || fineWork.Count>0);
        public long GpuBytes=>disposed?0:CoarseScene.Bytes+(gridTexture?.Bytes??0)+(long)fine.Width*fine.Height*8;
        public long CpuBytes=>disposed?0:Field.AllocatedBytes+Absorption.LongLength*4+sourceArrayBytes+seedsBySource.Values.Sum(s=>s.LongLength*20);

        public LevelGridFill(LevelLightScene scene,LevelLightSources sources)
        {
            fine=scene??throw new ArgumentNullException(nameof(scene));liveSources=sources??throw new ArgumentNullException(nameof(sources));
            if(fine.Resolution<2 || fine.Resolution%Divisor!=0)
                throw new ArgumentException($"Заполнение уровня: разрешение fine={fine.Resolution}/м нельзя разделить на{Divisor}; область={fine.WorldRect}, размер={fine.Width}×{fine.Height}.");
            try
            {
                int width=(fine.Width+Divisor-1)/Divisor,height=(fine.Height+Divisor-1)/Divisor;
                var all=new RectInt(0,0,width,height);
                var baseAbsorption=GridLightTexture.ReadAbsorption(fine,Divisor,all,true);
                var walls=new float[width,height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)walls[x,y]=baseAbsorption[y*width+x];
                CoarseScene=new LevelLightScene(walls,fine.WorldRect.position,fine.Resolution/Divisor,Array.Empty<LevelLightScene.ObjectSprite>());
                CoarseScene.SetDoors(fine.Doors.Values.ToArray(),null);
                Absorption=GridLightTexture.ReadAbsorption(CoarseScene,1,all);
                var clones=new List<LevelLightSource>(sources.Count);
                foreach(var source in sources.All)
                {
                    var samples=(Vector4[])source.Samples.Clone();sourceArrayBytes+=samples.LongLength*16;
                    int x=source.Texels.xMin/Divisor,y=source.Texels.yMin/Divisor;
                    var rect=new RectInt(x,y,(source.Texels.xMax+Divisor-1)/Divisor-x,(source.Texels.yMax+Divisor-1)/Divisor-y);
                    clones.Add(new LevelLightSource(source.Id,source.Kind,samples,source.Radiance,source.Width,source.Reach,source.Bounds,rect,true));
                }
                Sources=new LevelLightSources(clones,width,height);
                // Disabled lamps need authored seed positions too, for their later activation.
                var placement=GridLightPlacement.Build(CoarseScene,Sources.All,Absorption,1);seedsBySource=placement.BySource;
                SeedIndex=new GridLightSeeds(width,seedsBySource);
                for(int i=0;i<clones.Count;i++){clones[i].Enabled=liveSources.All[i].Enabled;SeedIndex.SetEnabled(clones[i].Id,clones[i].Enabled);}
                Field=new GridLightField(width,height,CoarseScene.Texel,Absorption);
                reconstruction=new GridFillReconstruction(fine.Width,fine.Height,GraphicsFormat.R16G16B16A16_SFloat);
            }
            catch{Dispose();throw;}
        }
        public IEnumerator BuildAllSteps()
        {
            EnsureAlive();built=false;ResetMetrics();
            ApplyPending();coarseWork.Clear();fineWork.Clear();
            Field.Solve(SeedIndex.Query(new RectInt(0,0,Field.Width,Field.Height)));
            if(gridTexture==null)gridTexture=new GridLightTexture(Field);else gridTexture.Update(Field,new RectInt(0,0,Field.Width,Field.Height));
            LastGridCells=Field.RecomputedCells;LastChangedRect=new RectInt(0,0,Field.Width,Field.Height);
            BeginDiagnostics();EnqueueTiles(new RectInt(0,0,fine.Width,fine.Height));yield return null;
            while(fineWork.Count>0){Reconstruct(fineWork.Dequeue());yield return null;}
            built=true;FinishDiagnostics();Revision++;
        }
        public void BuildAll()
        {var steps=BuildAllSteps();try{while(steps.MoveNext()){}}finally{(steps as IDisposable)?.Dispose();}}
        // Enabled has already changed in the live catalog; repeated events collapse to its latest state.
        public void SourceChanged(int index)
        {
            EnsureAlive();if(index<0 || index>=liveSources.Count)throw new ArgumentOutOfRangeException(nameof(index));
            if(!HasWork)ResetMetrics();changedSources.Add(index);
        }
        // Fine scene already contains the new door snapshot. IDs supply diagnostics; only rectangles queue work.
        public void OccludersChanged(IReadOnlyList<string> ids,IReadOnlyList<RectInt> rectangles)
        {
            EnsureAlive();if(ids==null || rectangles==null || ids.Count!=rectangles.Count)throw new ArgumentException("Заполнение уровня: нет согласованных ID/областей геометрии.");
            if(!HasWork)ResetMetrics();
            foreach(var r in rectangles)if(r.width>0 && r.height>0)changedGeometry.Add(r);
        }
        public bool Step(int budget)
        {
            EnsureAlive();if(!built)throw new InvalidOperationException("Заполнение уровня: шаг до начальной сборки.");
            if(budget<=0)throw new ArgumentOutOfRangeException(nameof(budget));
            if(!HasWork)return false;
            BeginDiagnostics();ApplyPending();
            while(budget>0 && coarseWork.Count>0)
            {
                var dirty=coarseWork.Dequeue();var affected=Field.Update(SeedIndex.Query(Field.AffectedRegion(dirty)),dirty);
                gridTexture.Update(Field,affected);LastGridCells+=Field.RecomputedCells;LastChangedRect=Union(LastChangedRect,affected);
                float t=CoarseScene.Texel;var world=new Rect(CoarseScene.WorldRect.x+affected.x*t-.6f,CoarseScene.WorldRect.y+affected.y*t-.6f,affected.width*t+1.2f,affected.height*t+1.2f);
                EnqueueTiles(fine.Texels(world));budget--;
            }
            // All coarse changes precede reconstruction, so every tile reads one coherent latest fill/geometry.
            if(coarseWork.Count==0)while(budget>0 && fineWork.Count>0){Reconstruct(fineWork.Dequeue());budget--;}
            if(!HasWork){FinishDiagnostics();Revision++;}
            return HasWork;
        }
        private void ApplyPending()
        {
            if(changedGeometry.Count>0)
            {
                var changedIds=new List<string>();var coarseRects=CoarseScene.SetDoors(fine.Doors.Values.ToArray(),changedIds);
                // Include both old/new caller rectangles when a known door moved or was removed.
                foreach(var fineRect in changedGeometry)
                {
                    int x=fineRect.xMin/Divisor,y=fineRect.yMin/Divisor;
                    var coarseRect=new RectInt(x,y,(fineRect.xMax+Divisor-1)/Divisor-x,(fineRect.yMax+Divisor-1)/Divisor-y);
                    if(!coarseRects.Contains(coarseRect))coarseRects.Add(coarseRect);
                }
                foreach(var dirty in coarseRects)
                {
                    GridLightTexture.PatchAbsorption(Absorption,Field.Width,dirty,GridLightTexture.ReadAbsorption(CoarseScene,1,dirty));
                    // A closed door may cover a seed. Re-place only emitting forms touching the changed patch.
                    Sources.Overlapping(dirty,nearbySources);
                    var dirtyWorld=new Rect(CoarseScene.WorldRect.x+dirty.x*CoarseScene.Texel,CoarseScene.WorldRect.y+dirty.y*CoarseScene.Texel,dirty.width*CoarseScene.Texel,dirty.height*CoarseScene.Texel);
                    foreach(int index in nearbySources)
                    {
                        var source=Sources.All[index];var form=source.Bounds;form.xMin-=CoarseScene.Texel;form.yMin-=CoarseScene.Texel;form.xMax+=CoarseScene.Texel;form.yMax+=CoarseScene.Texel;
                        if(!form.Overlaps(dirtyWorld))continue;
                        bool enabled=source.Enabled;source.Enabled=true;GridLightPlacement.Result placement;
                        try{placement=GridLightPlacement.Build(CoarseScene,new[]{source},Absorption,1);}finally{source.Enabled=enabled;}
                        if(SeedIndex.TryBounds(source.Id,out var previous))QueueCoarse(previous);
                        SeedIndex.Replace(source.Id,placement.BySource[source.Id]);
                        if(SeedIndex.TryBounds(source.Id,out var current))QueueCoarse(current);
                    }
                    QueueCoarse(dirty);
                }
                changedGeometry.Clear();
            }
            foreach(int index in changedSources)
            {
                var source=Sources.All[index];bool enabled=liveSources.All[index].Enabled;
                if(source.Enabled==enabled)continue;
                source.Enabled=enabled;SeedIndex.SetEnabled(source.Id,enabled);
                if(SeedIndex.TryBounds(source.Id,out var bounds))QueueCoarse(bounds);
                // Missing placements were diagnosed by GridLightPlacement, and never inject a replacement light.
            }
            changedSources.Clear();
        }
        private void EnqueueTiles(RectInt r)
        {
            for(int y=r.yMin;y<r.yMax;y+=TileSize)for(int x=r.xMin;x<r.xMax;x+=TileSize)
                fineWork.Enqueue(new RectInt(x,y,Math.Min(TileSize,r.xMax-x),Math.Min(TileSize,r.yMax-y)));
        }
        private void QueueCoarse(RectInt r)
        {
            if(r.width<=0 || r.height<=0)return;
            // Coalesce only touching local commands; distant events remain independent work items.
            int count=coarseWork.Count;
            for(int i=0;i<count;i++)
            {
                var other=coarseWork.Dequeue();
                if(other.xMin<=r.xMax && other.xMax>=r.xMin && other.yMin<=r.yMax && other.yMax>=r.yMin)r=Union(r,other);
                else coarseWork.Enqueue(other);
            }
            coarseWork.Enqueue(r);
        }
        private void Reconstruct(RectInt rect)
        {
            reconstruction.BuildRegion(gridTexture.Texture,CoarseScene.Texture,CoarseScene.WorldRect,fine.Texture,fine.WorldRect,fine.WorldRect,rect);
            LastReconstructedTiles++;LastReconstructedRect=Union(LastReconstructedRect,rect);
        }
        private void BeginDiagnostics(){if(diagnosticsPending)return;reconstruction.BeginDiagnostics();diagnosticsPending=true;}
        private void FinishDiagnostics(){if(!diagnosticsPending)return;reconstruction.FinishDiagnostics($"уровень {WorldRect}, revision={Revision+1}, tiles={LastReconstructedTiles}");diagnosticsPending=false;}
        private void ResetMetrics(){LastGridCells=0;LastReconstructedTiles=0;LastChangedRect=LastReconstructedRect=default;}
        private void EnsureAlive(){if(disposed)throw new ObjectDisposedException(nameof(LevelGridFill));}
        private static RectInt Union(RectInt a,RectInt b)
        {if(a.width<=0 || a.height<=0)return b;if(b.width<=0 || b.height<=0)return a;int x=Math.Min(a.xMin,b.xMin),y=Math.Min(a.yMin,b.yMin);return new RectInt(x,y,Math.Max(a.xMax,b.xMax)-x,Math.Max(a.yMax,b.yMax)-y);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;reconstruction?.Dispose();gridTexture?.Dispose();CoarseScene?.Dispose();
            coarseWork.Clear();fineWork.Clear();changedSources.Clear();changedGeometry.Clear();
        }
    }
}
