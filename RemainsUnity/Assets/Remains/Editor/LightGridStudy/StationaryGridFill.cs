using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Editor comparison: the same bounded, hue-preserving 20 cm fill for either direct-light method.
    // It owns no direct fields or composite, so its payload can be counted independently.
    internal sealed class StationaryGridFill : IDisposable
    {
        private readonly Dictionary<string,int> doorById=new(StringComparer.Ordinal);
        private LevelLightScene.Door[] doors;
        private GridLightTexture gridTexture;
        private long sourceArrayBytes,seedArrayBytes;
        private bool built,disposed;

        internal LevelLightScene Scene {get;private set;}
        internal LevelLightSources Sources {get;private set;}
        internal GridLightField Field {get;private set;}
        internal GridLightSeeds SeedIndex {get;private set;}
        internal float[] Absorption {get;private set;}
        internal RenderTexture Texture=>gridTexture?.Texture;
        internal long LastGridCells {get;private set;}
        internal RectInt LastChangedRect {get;private set;}
        // Array/texture payloads only; excludes CLR object headers, dictionaries and driver overhead.
        internal long CpuBytes=>disposed?0:(Field?.AllocatedBytes??0)+(Absorption?.LongLength??0)*4+sourceArrayBytes+seedArrayBytes;
        internal long GpuBytes=>disposed?0:(Scene?.Bytes??0)+(gridTexture?.Bytes??0);
        internal long AllocatedBytes=>CpuBytes+GpuBytes;
        private RectInt AllCells=>new(0,0,Scene.Width,Scene.Height);

        internal StationaryGridFill(LevelLightScene originalFineScene,IEnumerable<LevelLightSource> originalSources)
        {
            if(originalFineScene==null)throw new ArgumentNullException(nameof(originalFineScene));
            if(originalSources==null)throw new ArgumentNullException(nameof(originalSources));
            if(originalFineScene.Resolution%2!=0 || originalFineScene.Width%2!=0 || originalFineScene.Height%2!=0)
                throw new ArgumentException("Заполнение полярного стенда: исходная сетка и разрешение должны делиться на два.");
            try
            {
                int width=originalFineScene.Width/2,height=originalFineScene.Height/2;
                var sourceList=originalSources.ToArray();
                doors=originalFineScene.Doors.Values.ToArray();
                var openDoors=doors.Select(d=>new LevelLightScene.Door(d.Id,d.Bounds,d.Opacity,false)).ToArray();
                float[] baseAbsorption;
                // LevelLightScene does not expose its base texture. Read exactly that geometry with doors
                // temporarily open, as in GridDirectHybrid; restore even when readback throws.
                // The caller constructs this helper before building direct fields: fine Revision may advance.
                try
                {
                    originalFineScene.SetDoors(openDoors,null);
                    baseAbsorption=GridLightTexture.ReadAbsorption(originalFineScene,2,new RectInt(0,0,width,height));
                }
                finally {originalFineScene.SetDoors(doors,null);}
                var walls=new float[width,height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)walls[x,y]=baseAbsorption[y*width+x];
                Scene=new LevelLightScene(walls,originalFineScene.WorldRect.position,originalFineScene.Resolution/2,Array.Empty<LevelLightScene.ObjectSprite>());
                Scene.SetDoors(doors,null);
                for(int i=0;i<doors.Length;i++)doorById.Add(doors[i].Id,i);
                Absorption=GridLightTexture.ReadAbsorption(Scene,1,AllCells);

                var clones=new List<LevelLightSource>(sourceList.Length);
                foreach(var source in sourceList)
                {
                    if(source==null)throw new ArgumentException("Заполнение полярного стенда: пустой источник в каталоге.");
                    // Keep every original point, including long lamps. Max propagation does not multiply
                    // intensity by the number of these seeds; the shape only changes their positions.
                    var samples=(Vector4[])source.Samples.Clone();sourceArrayBytes+=samples.LongLength*16;
                    int x=source.Texels.xMin/2,y=source.Texels.yMin/2;
                    var texels=new RectInt(x,y,(source.Texels.xMax+1)/2-x,(source.Texels.yMax+1)/2-y);
                    clones.Add(new LevelLightSource(source.Id,source.Kind,samples,source.Radiance,source.Width,source.Reach,source.Bounds,texels,source.Enabled));
                }
                Sources=new LevelLightSources(clones,width,height);
                // Prepare disabled source positions too, without changing the caller's source instances.
                var enabled=clones.Select(s=>s.Enabled).ToArray();
                foreach(var source in clones)source.Enabled=true;
                GridLightPlacement.Result placement;
                try {placement=GridLightPlacement.Build(Scene,Sources.All,Absorption,1);}
                finally {for(int i=0;i<clones.Count;i++)clones[i].Enabled=enabled[i];}
                SeedIndex=new GridLightSeeds(width,placement.BySource);
                foreach(var pair in placement.BySource)seedArrayBytes+=pair.Value.LongLength*20;
                foreach(var source in clones)if(!source.Enabled)SeedIndex.SetEnabled(source.Id,false);
                Field=new GridLightField(width,height,Scene.Texel,Absorption);
            }
            catch {Dispose();throw;}
        }

        internal void BuildAll()
        {
            EnsureAlive();Field.Solve(SeedIndex.Query(AllCells));
            if(gridTexture==null)gridTexture=new GridLightTexture(Field);else gridTexture.Update(Field,AllCells);
            LastGridCells=Field.RecomputedCells;LastChangedRect=AllCells;built=true;
        }

        internal void SetLamp(string id,bool enabled)
        {
            EnsureBuilt();ResetMetrics();
            if(!Sources.TryGet(id,out int index) || Sources.All[index].Kind!=LevelLightSourceKind.Lamp)
                throw new ArgumentException("Заполнение полярного стенда: неизвестная лампа "+id);
            var source=Sources.All[index];if(source.Enabled==enabled)return;
            // Bounds validates that this authored source really has seeds before mutating state.
            var changed=SeedIndex.Bounds(id);source.Enabled=enabled;SeedIndex.SetEnabled(id,enabled);Update(changed);
        }

        internal void SetDoor(string id,bool blocks)
        {
            EnsureBuilt();ResetMetrics();
            if(!doorById.TryGetValue(id,out int index))throw new ArgumentException("Заполнение полярного стенда: неизвестная дверь "+id);
            var old=doors[index];if(old.Blocks==blocks)return;
            doors[index]=new LevelLightScene.Door(old.Id,old.Bounds,old.Opacity,blocks);
            Scene.SetDoors(doors,null);
            var changed=Scene.Texels(old.Bounds);
            if(changed.width<=0 || changed.height<=0)
                throw new InvalidOperationException($"Заполнение полярного стенда: дверь {id}, границы {old.Bounds}, не имеет клеток на сетке {Scene.WorldRect}.");
            GridLightTexture.PatchAbsorption(Absorption,Field.Width,changed,GridLightTexture.ReadAbsorption(Scene,1,changed));
            Update(changed);
        }

        private void Update(RectInt changed)
        {
            var affected=Field.Update(SeedIndex.Query(Field.AffectedRegion(changed)),changed);
            gridTexture.Update(Field,affected);LastGridCells=Field.RecomputedCells;LastChangedRect=affected;
        }
        private void ResetMetrics(){LastGridCells=0;LastChangedRect=default;}
        private void EnsureAlive(){if(disposed)throw new ObjectDisposedException(nameof(StationaryGridFill));}
        private void EnsureBuilt(){EnsureAlive();if(!built)throw new InvalidOperationException("Заполнение полярного стенда: сначала требуется BuildAll.");}

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            gridTexture?.Dispose();gridTexture=null;Scene?.Dispose();Scene=null;
        }
    }
}
