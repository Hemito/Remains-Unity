using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Editor
{
    // Shape placement is prepared once. Lamp events query only seed buckets around the changed source.
    public sealed class GridLightSeeds
    {
        private const int BucketSize=32;
        private readonly int width;
        private readonly Dictionary<string,GridLightSeed[]> sources;
        private readonly HashSet<string> disabled=new(StringComparer.Ordinal);
        private readonly Dictionary<Vector2Int,List<(string source,GridLightSeed seed)>> buckets=new();
        public GridLightSeeds(int width,Dictionary<string,GridLightSeed[]> sources)
        {
            this.width=width;this.sources=sources;
            foreach(var pair in sources) foreach(var seed in pair.Value)
            {
                var key=new Vector2Int(seed.Index%width/BucketSize,seed.Index/width/BucketSize);
                if(!buckets.TryGetValue(key,out var entries)) buckets.Add(key,entries=new());
                entries.Add((pair.Key,seed));
            }
        }
        public void SetEnabled(string id,bool enabled)
        {
            if(!sources.ContainsKey(id)) throw new ArgumentException("В сетке нет источника "+id);
            if(enabled) disabled.Remove(id);else disabled.Add(id);
        }
        public RectInt Bounds(string id)
        {
            if(!sources.TryGetValue(id,out var seeds) || seeds.Length==0) throw new ArgumentException("Источник без клеток излучения: "+id);
            int x0=int.MaxValue,y0=int.MaxValue,x1=0,y1=0;
            foreach(var seed in seeds)
            {int x=seed.Index%width,y=seed.Index/width;x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x+1);y1=Math.Max(y1,y+1);}
            return new RectInt(x0,y0,x1-x0,y1-y0);
        }
        public List<GridLightSeed> Query(RectInt region)
        {
            var result=new List<GridLightSeed>();
            for(int by=region.yMin/BucketSize;by<=(region.yMax-1)/BucketSize;by++)
                for(int bx=region.xMin/BucketSize;bx<=(region.xMax-1)/BucketSize;bx++)
                    if(buckets.TryGetValue(new Vector2Int(bx,by),out var entries)) foreach(var entry in entries)
                    {
                        var p=new Vector2Int(entry.seed.Index%width,entry.seed.Index/width);
                        if(region.Contains(p) && !disabled.Contains(entry.source)) result.Add(entry.seed);
                    }
            return result;
        }
    }
}
