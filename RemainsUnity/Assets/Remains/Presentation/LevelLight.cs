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

        public LevelLight(LevelLightScene scene,LevelLightSources sources,float albedo,long directCacheBytes=PolarDirectLight.DefaultCacheBytes,bool useGridFill=true)
        {
            Scene=scene??throw new ArgumentNullException(nameof(scene));
            Sources=sources??throw new ArgumentNullException(nameof(sources));
            UsesGridFill=useGridFill;
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
            Direct.OccludersChanged(changedDoors,rects);
            if(UsesGridFill)Fill.OccludersChanged(changedDoors,rects);
            else foreach(var rect in rects) Bounce.Changed(rect);
        }
        // Lamps in dark (switched off or broken) emit nothing; the rest of the lamps emit.
        public void SetDark(IReadOnlyCollection<string> dark)
        {
            if(dark==null) throw new ArgumentNullException(nameof(dark));
            for(int i=0;i<Sources.Count;i++)
            {
                var source=Sources.All[i];
                if(source.Kind!=LevelLightSourceKind.Lamp) continue;
                bool enabled=!Contains(dark,source.Id);
                if(enabled==source.Enabled) continue;
                source.Enabled=enabled;
                Direct.SourceChanged(i);
                if(UsesGridFill)Fill.SourceChanged(i);
            }
        }
        private static bool Contains(IReadOnlyCollection<string> set,string id)
        {
            if(set is HashSet<string> hash) return hash.Contains(id);
            foreach(string item in set) if(item==id) return true;
            return false;
        }
        // Event work of one frame: field builds of direct light first; bounce windows once direct light is final.
        public bool HasWork=>Direct.HasWork || (UsesGridFill?Fill.HasWork:Bounce.HasWork);
        public bool HasPendingPublication=>Direct.HasPendingPublication || (UsesGridFill?Fill.HasWork:Bounce.HasWork);
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
