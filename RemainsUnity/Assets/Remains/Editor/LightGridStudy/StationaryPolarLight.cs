using System.Collections.Generic;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // The studies call synchronous event commands, but all rendering/math is the runtime implementation.
    internal sealed class StationaryPolarLight : PolarDirectLight
    {
        internal StationaryPolarLight(LevelLightScene scene,LevelLightSources sources,long cacheBudget=DefaultCacheBytes)
            :base(scene,sources,cacheBudget) {}
        public override void SourceChanged(int index) {base.SourceChanged(index);Finish();}
        public override void OccludersChanged(IReadOnlyList<string> ids,IReadOnlyList<RectInt> rectangles)
        {base.OccludersChanged(ids,rectangles);Finish();}
        private void Finish()
        {
            int steps=0;
            while(HasWork)if(Step(64) && ++steps>100000)throw new System.InvalidOperationException("Полярный Editor-стенд: локальное событие не завершилось.");
        }
    }
}
