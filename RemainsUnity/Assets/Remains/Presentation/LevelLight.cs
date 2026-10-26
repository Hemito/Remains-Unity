using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    // Light of a whole level (hybrid: per-source direct light on the full grid, bounce later on a half grid).
    // Built once per level, then changed only by events: doors (SetDoors) and lamps (SetDark); Step spreads
    // the resulting field builds over frames.
    public sealed class LevelLight : IDisposable
    {
        public LevelLightScene Scene {get;private set;}
        public LevelLightSources Sources {get;}
        public DirectLight Direct {get;private set;}
        public BounceLight Bounce {get;private set;}
        private readonly List<string> changedDoors=new();

        public LevelLight(LevelLightScene scene,LevelLightSources sources,float albedo,long directCacheBytes=DirectLight.DefaultCacheBytes)
        {
            Scene=scene??throw new ArgumentNullException(nameof(scene));
            Sources=sources??throw new ArgumentNullException(nameof(sources));
            try
            {
                Direct=new DirectLight(scene,sources,directCacheBytes);
                Bounce=new BounceLight(scene,Direct,albedo);
                Direct.Recomposed+=Bounce.Changed;
            }
            catch {Dispose();throw;}
        }
        // Direct light of every source, then the bounce of the whole level (one stage per MoveNext).
        public IEnumerator BuildSteps()
        {
            var direct=Direct.BuildAllSteps();
            while(direct.MoveNext()) yield return null;
            var bounce=Bounce.BuildAllSteps();
            while(bounce.MoveNext()) yield return null;
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
            foreach(var rect in rects) Bounce.Changed(rect);
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
            }
        }
        private static bool Contains(IReadOnlyCollection<string> set,string id)
        {
            if(set is HashSet<string> hash) return hash.Contains(id);
            foreach(string item in set) if(item==id) return true;
            return false;
        }
        // Event work of one frame: field builds of direct light first; bounce windows once direct light is final.
        public bool Step(int maxBuilds,int bounceStages)
        {
            if(Direct.HasWork) Direct.Step(maxBuilds);
            else Bounce.Step(bounceStages);
            return Direct.HasWork || Bounce.HasWork;
        }

        public void Dispose()
        {
            Bounce?.Dispose();Bounce=null;
            Direct?.Dispose();Direct=null;
            Scene?.Dispose();Scene=null;
        }
    }
}
