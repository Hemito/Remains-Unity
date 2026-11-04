using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    // The renderer consumes source fields and their sum; implementations own all rebuild scheduling/cache state.
    public interface ILevelDirectLight : IDisposable
    {
        Texture Sum {get;}
        long CacheBytes {get;}
        long SumBytes {get;}
        long Revision {get;}
        int Builds {get;}
        int Adds {get;}
        int Evictions {get;}
        int Recompositions {get;}
        int PendingMeasurements {get;}
        int CachedFields {get;}
        bool HasWork {get;}
        bool HasPendingPublication {get;}
        event Action<RectInt> Recomposed;
        IEnumerator BuildAllSteps();
        void BuildAll();
        void SourceChanged(int index);
        void OccludersChanged(IReadOnlyList<string> ids,IReadOnlyList<RectInt> texels);
        bool Step(int maxBuilds);
        void Request(IReadOnlyCollection<string> ids);
        bool ReadyOrNone(string id);
        bool TryGetField(string id,out Texture texture,out RectInt texels);
        void BuildTransient(LevelLightSource source,RenderTexture target);
        void WaitForMeasurements();
        IReadOnlyList<string> SubscribedDoors(int index);
    }
}
