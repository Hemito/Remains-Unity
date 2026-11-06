using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    // Accepted game light: cached polar direct and bounded grid fill, reconstructed once in world space.
    // Legacy ray/bounce is retained only for explicitly requested reference checks.
    // Built once per level, then changed only by events: doors (SetDoors) and lamps (SetDark); Step spreads
    // the resulting field builds over frames.
    public sealed class LevelLight : IDisposable
    {
        public LevelLightScene Scene {get;private set;}
        public LevelLightSources Sources {get;}
        public ILevelDirectLight Direct {get;private set;}
        public BounceLight Bounce {get;private set;}
        public LevelGridFill Fill {get;private set;}
        public bool UsesGridFill {get;}
        private readonly List<string> changedDoors=new();
        private readonly HashSet<int> darkSources=new(),nextDarkSources=new(),changedSources=new();
        private bool publishingLamp;
        // One lamp, at most 131072 fine texels (~36x36m at 10px/m), never the entire level.
        public const int FastLampTexels=128*1024;
        public int FastLampUpdates {get;private set;}
        public event Action<IReadOnlyList<RectInt>> GeometryChanged;

        public LevelLight(LevelLightScene scene,LevelLightSources sources,float albedo,long directCacheBytes=PolarDirectLight.DefaultCacheBytes,bool useGridFill=true)
        {
            Scene=scene??throw new ArgumentNullException(nameof(scene));
            Sources=sources??throw new ArgumentNullException(nameof(sources));
            UsesGridFill=useGridFill;
            for(int i=0;i<sources.Count;i++)if(sources.All[i].Kind==LevelLightSourceKind.Lamp && !sources.All[i].Enabled)darkSources.Add(i);
            try
            {
                if(useGridFill)
                {
                    Direct=new PolarDirectLight(scene,sources,directCacheBytes);
                    Fill=new LevelGridFill(scene,sources);
                }
                else
                {
                    Direct=new DirectLight(scene,sources,directCacheBytes);
                    Bounce=new BounceLight(scene,Direct,albedo);
                    Direct.Recomposed+=Bounce.Changed;
                }
            }
            catch {Dispose();throw;}
        }
        // Initial preparation only. Subsequent work is queued locally by SetDoors/SetDark and budgeted by Step.
        public IEnumerator BuildSteps()
        {
            var direct=Direct.BuildAllSteps();
            try{while(direct.MoveNext())yield return direct.Current;}
            finally{(direct as IDisposable)?.Dispose();}
            var indirect=UsesGridFill?Fill.BuildAllSteps():Bounce.BuildAllSteps();
            try{while(indirect.MoveNext())yield return indirect.Current;}
            finally{(indirect as IDisposable)?.Dispose();}
        }
        public void BuildAll()
        {
            var steps=BuildSteps();
            try {while(steps.MoveNext()) {}} finally {(steps as IDisposable)?.Dispose();}
        }

        public void SetDoors(IReadOnlyList<LevelLightScene.Door> doors)
        {
            var rects=Scene.SetDoors(doors,changedDoors);
            if(rects.Count==0) return;
            GeometryChanged?.Invoke(rects);
            Direct.OccludersChanged(changedDoors,rects);
            if(UsesGridFill)Fill.OccludersChanged(changedDoors,rects);
            else foreach(var rect in rects) Bounce.Changed(rect);
        }
        // Lamps in dark (switched off or broken) emit nothing; the rest of the lamps emit.
        public void SetDark(IReadOnlyCollection<string> dark)
        {
            if(dark==null) throw new ArgumentNullException(nameof(dark));
            nextDarkSources.Clear();
            foreach(string id in dark)
            {
                if(!Sources.TryGet(id,out int i) || Sources.All[i].Kind!=LevelLightSourceKind.Lamp)
                    throw new ArgumentException("Выключение света: неизвестная лампа "+id);
                nextDarkSources.Add(i);
            }
            changedSources.Clear();changedSources.UnionWith(darkSources);changedSources.SymmetricExceptWith(nextDarkSources);
            bool idle=!HasPendingPublication;
            foreach(int i in changedSources)
            {
                Sources.All[i].Enabled=!nextDarkSources.Contains(i);
                if(UsesGridFill)Fill.SourceChanged(i);
            }
            bool fast=false;
            if(idle && UsesGridFill && changedSources.Count==1 && Direct is PolarDirectLight polar)
                foreach(int index in changedSources)
                    if(polar.CanRemoveSource(index,FastLampTexels) && Fill.CanCompleteSourceChanges(1,FastLampTexels))
                    {
                        // Both preflights passed before either texture changes. No partial publication to consumers.
                        publishingLamp=true;
                        try
                        {
                            polar.RemoveSource(index,FastLampTexels);
                            if(!Fill.TryCompleteSourceChanges(1,FastLampTexels))throw new InvalidOperationException("Заполнение лампы изменилось после успешной предварительной проверки: "+Sources.All[index].Id);
                            fast=true;FastLampUpdates++;
                        }
                        finally {publishingLamp=false;}
                    }
            if(!fast)foreach(int index in changedSources)Direct.SourceChanged(index);
            darkSources.Clear();darkSources.UnionWith(nextDarkSources);
        }
        // Event work of one frame: field builds of direct light first; bounce windows once direct light is final.
        public bool HasWork=>Direct.HasWork || (UsesGridFill?Fill.HasWork:Bounce.HasWork);
        public bool HasPendingPublication=>publishingLamp || Direct.HasPendingPublication || (UsesGridFill?Fill.HasWork:Bounce.HasWork);
        public bool Step(int maxBuilds,int bounceStages)
        {
            if(Direct.HasWork) Direct.Step(maxBuilds);
            else if(UsesGridFill)Fill.Step(bounceStages);
            else Bounce.Step(bounceStages);
            return HasWork;
        }

        public void Dispose()
        {
            Bounce?.Dispose();Bounce=null;
            Fill?.Dispose();Fill=null;
            Direct?.Dispose();Direct=null;
            Scene?.Dispose();Scene=null;
        }
    }
}
