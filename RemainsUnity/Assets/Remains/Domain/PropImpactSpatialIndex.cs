using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Same level/X bucket broad phase as physical support. Accepted sources are indexed on publication,
    // not rebuilt or compared with every actor in the physics tick.
    public sealed class PropImpactSpatialIndex
    {
        private readonly Dictionary<string,PropImpactSample> samples=new(StringComparer.Ordinal);
        private readonly Dictionary<(string level,int cell),List<PropImpactSample>> buckets=new();
        private readonly HashSet<string> visited=new(StringComparer.Ordinal);
        public int CandidateVisits {get;private set;}
        private static int Cell(float x)=>Mathf.FloorToInt(x/PropSupportIndex.CellWidth);
        public void Clear() {samples.Clear();buckets.Clear();}
        public bool Remove(string prop,string sampleId)
        {
            if(!samples.TryGetValue(prop,out var sample) || sample.id!=sampleId) return false;
            samples.Remove(prop);var bounds=sample.Bounds;
            for(int cell=Cell(bounds.xMin);cell<=Cell(bounds.xMax);cell++)
                if(buckets.TryGetValue((sample.level,cell),out var list)) list.Remove(sample);
            return true;
        }
        public void Add(PropImpactSample sample)
        {
            if(samples.TryGetValue(sample.prop,out var previous))
            {
                if(sample.generation<previous.generation || sample.generation==previous.generation && !PropReplication.IsNewer(sample.sequence,previous.sequence)) return;
                var old=previous.Bounds;
                for(int cell=Cell(old.xMin);cell<=Cell(old.xMax);cell++)
                    if(buckets.TryGetValue((previous.level,cell),out var list)) list.Remove(previous);
            }
            samples[sample.prop]=sample;var bounds=sample.Bounds;
            for(int cell=Cell(bounds.xMin);cell<=Cell(bounds.xMax);cell++)
            {
                var key=(sample.level,cell);
                if(!buckets.TryGetValue(key,out var list)) buckets.Add(key,list=new List<PropImpactSample>());
                list.Add(sample);
            }
        }
        public void Query(string level,Rect bounds,List<PropImpactSample> output)
        {
            output.Clear();visited.Clear();CandidateVisits=0;
            for(int cell=Cell(bounds.xMin);cell<=Cell(bounds.xMax);cell++)
                if(buckets.TryGetValue((level,cell),out var list)) foreach(var sample in list)
                {CandidateVisits++;if(visited.Add(sample.prop)) output.Add(sample);}
        }
    }
}
