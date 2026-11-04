using System;
using System.Collections.Generic;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Presentation
{
    // Shared shape-to-grid preparation for the plain and directional hybrid experiments.
    public static class GridLightPlacement
    {
        public sealed class Result
        {
            public readonly List<GridLightSeed> All=new();
            public readonly Dictionary<string,GridLightSeed[]> BySource=new(StringComparer.Ordinal);
            public int Unseeded;
            public float MaxRadiance;
        }
        public static Result Build(LevelLightScene scene,IReadOnlyList<LevelLightSource> sources,float[] absorption,int divisor)
        {
            if(divisor<=0 || scene.Width%divisor!=0 || scene.Height%divisor!=0 || absorption.Length!=(scene.Width/divisor)*(scene.Height/divisor))
                throw new ArgumentException("Размер сетки размещения источников не согласован со сценой.");
            int w=scene.Width/divisor,h=scene.Height/divisor;float size=scene.Texel*divisor;var result=new Result();
            foreach(var source in sources)
            {
                if(!source.Enabled) continue;
                // Fixed seed strength from unobstructed irradiance at 1 m; weights describe shape, not brightness shares for max transport.
                Color color=source.Radiance*(2*Mathf.Atan(source.Width*.5f));result.MaxRadiance=Mathf.Max(result.MaxRadiance,color.maxColorComponent);
                int before=result.All.Count;
                foreach(var sample in source.Samples)
                {
                    int cx=Mathf.FloorToInt((sample.x-scene.WorldRect.x)/size),cy=Mathf.FloorToInt((sample.y-scene.WorldRect.y)/size);
                    int radius=Mathf.CeilToInt(Mathf.Max(source.Bounds.width,source.Bounds.height)/size)+1;
                    int best=-1;float distance=float.PositiveInfinity;
                    for(int y=Math.Max(0,cy-radius);y<=Math.Min(h-1,cy+radius);y++) for(int x=Math.Max(0,cx-radius);x<=Math.Min(w-1,cx+radius);x++)
                    {
                        if(absorption[y*w+x]>=GridLightField.OpaqueThreshold) continue;
                        var p=scene.WorldRect.position+new Vector2((x+.5f)*size,(y+.5f)*size);
                        if(p.x<source.Bounds.xMin-size || p.x>source.Bounds.xMax+size || p.y<source.Bounds.yMin-size || p.y>source.Bounds.yMax+size) continue;
                        float d=(p-new Vector2(sample.x,sample.y)).sqrMagnitude;
                        if(d<distance) {distance=d;best=y*w+x;}
                    }
                    if(best>=0) result.All.Add(new GridLightSeed(best,color));
                }
                if(result.All.Count==before)
                {
                    result.Unseeded++;
                    Debug.LogWarning($"Заполнение света: источник {source.Id}, форма {source.Bounds}, не имеет свободной клетки излучения; пропущен.");
                }
                result.BySource.Add(source.Id,result.All.GetRange(before,result.All.Count-before).ToArray());
            }
            return result;
        }
    }
}
