using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Physics scratch broad phase. Each body occupies one horizontal bucket;
    // live positions remain authoritative, and stepped bodies update their bucket.
    public sealed class PropSupportIndex : IEnumerable<PropState>
    {
        private const float CellWidth=4;
        private readonly Dictionary<(string level,int cell),List<PropState>> buckets=new();
        private readonly Dictionary<string,float> halfWidths=new(StringComparer.Ordinal);
        private WorldPropView source;
        public void Prepare(WorldPropView props,Func<string,bool> includeLevel=null)
        {
            source=props;
            foreach(var bucket in buckets.Values) bucket.Clear();
            halfWidths.Clear();
            foreach(var prop in props)
            {
                if(!prop.hasPhysicalBody || includeLevel!=null && !includeLevel(prop.level)) continue;
                Add(prop,Cell(prop.x));
                halfWidths.TryGetValue(prop.level,out float width);
                halfWidths[prop.level]=Mathf.Max(width,prop.width*.5f);
            }
        }
        private static int Cell(float x)=>Mathf.FloorToInt(x/CellWidth);
        private void Add(PropState prop,int cell)
        {
            var key=(prop.level,cell);
            if(!buckets.TryGetValue(key,out var bucket)) buckets.Add(key,bucket=new List<PropState>());
            bucket.Add(prop);
        }
        public void Moved(PropState prop,float previousX)
        {
            int previous=Cell(previousX),current=Cell(prop.x);
            if(previous==current || !prop.hasPhysicalBody) return;
            buckets[(prop.level,previous)].Remove(prop);Add(prop,current);
        }
        public IEnumerable<PropState> Near(PropState moving,float x)
        {
            if(!halfWidths.TryGetValue(moving.level,out float width)) yield break;
            float reach=width+moving.width*.5f;
            int first=Cell(x-reach),last=Cell(x+reach);
            for(int cell=first;cell<=last;cell++)
                if(buckets.TryGetValue((moving.level,cell),out var bucket))
                    for(int i=0;i<bucket.Count;i++) yield return bucket[i];
        }
        public IEnumerator<PropState> GetEnumerator()=>source.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
