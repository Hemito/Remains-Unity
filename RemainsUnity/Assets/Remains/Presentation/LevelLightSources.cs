using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public enum LevelLightSourceKind {Lamp,Window,Screen}

    // One steady emitter of a level: up to eight weighted points of its form (world), radiance, apparent width
    // and reach. Direct light is traced from these points; the field covers the reach rectangle only.
    public sealed class LevelLightSource
    {
        public readonly string Id;
        public readonly LevelLightSourceKind Kind;
        public readonly Vector4[] Samples;     // xy world point, z normalised weight
        public readonly Color Radiance;
        public readonly float Width;           // m, apparent width of the whole form
        public readonly float Reach;           // m, light ends smoothly over the last tenth
        public readonly Rect Bounds;           // world bounds of the form
        public readonly RectInt Texels;        // reach rectangle on the level grid
        public bool Enabled;                   // a dark or broken lamp emits nothing
        public LevelLightSource(string id,LevelLightSourceKind kind,Vector4[] samples,Color radiance,float width,float reach,Rect bounds,RectInt texels,bool enabled)
        {Id=id;Kind=kind;Samples=samples;Radiance=radiance;Width=width;Reach=reach;Bounds=bounds;Texels=texels;Enabled=enabled;}
    }

    // Sources of a level in a fixed order (the summation order of direct light) with a coarse grid index of their
    // reach rectangles: an event looks only at the sources whose light can reach its rectangle.
    public sealed class LevelLightSources
    {
        public const float LampReachScale=2,WindowReach=12,ScreenReach=6;
        private const int IndexCell=64;   // texels per index cell
        private readonly List<LevelLightSource> sources=new();
        private readonly Dictionary<string,int> byId=new(StringComparer.Ordinal);
        private readonly List<int>[] cells;
        private readonly int cellsWide,cellsHigh;
        private readonly int[] stamps;
        private int stamp;
        public IReadOnlyList<LevelLightSource> All=>sources;
        public int Count=>sources.Count;

        public LevelLightSources(IReadOnlyList<LevelLightSource> list,int levelWidth,int levelHeight)
        {
            if(list==null) throw new ArgumentNullException(nameof(list));
            cellsWide=(levelWidth+IndexCell-1)/IndexCell;cellsHigh=(levelHeight+IndexCell-1)/IndexCell;
            cells=new List<int>[cellsWide*cellsHigh];
            foreach(var source in list)
            {
                if(source==null || string.IsNullOrEmpty(source.Id)) throw new ArgumentException("Источник света уровня без ID.");
                if(source.Samples==null || source.Samples.Length==0 || source.Samples.Length>DirectLight.MaximumSamples)
                    throw new ArgumentException($"Источник света {source.Id}: точек формы {source.Samples?.Length ?? 0}, допустимо 1–{DirectLight.MaximumSamples}.");
                if(source.Texels.width<=0 || source.Texels.height<=0) throw new ArgumentException($"Источник света {source.Id}: дальность вне уровня.");
                if(!byId.TryAdd(source.Id,sources.Count)) throw new ArgumentException("Повтор ID источника света: "+source.Id);
                int index=sources.Count;sources.Add(source);
                Cells(source.Texels,out int x0,out int y0,out int x1,out int y1);
                for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++) (cells[y*cellsWide+x]??=new List<int>()).Add(index);
            }
            stamps=new int[sources.Count];
        }
        public bool TryGet(string id,out int index)=>byId.TryGetValue(id,out index);

        // Indices of the sources whose reach rectangle overlaps a texel rectangle, ascending (summation order).
        public void Overlapping(RectInt rect,List<int> output)
        {
            output.Clear();
            if(rect.width<=0 || rect.height<=0) return;
            stamp++;
            Cells(rect,out int x0,out int y0,out int x1,out int y1);
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                var list=cells[y*cellsWide+x];if(list==null) continue;
                foreach(int index in list)
                {
                    if(stamps[index]==stamp) continue;stamps[index]=stamp;
                    if(sources[index].Texels.Overlaps(rect)) output.Add(index);
                }
            }
            output.Sort();
        }
        private void Cells(RectInt rect,out int x0,out int y0,out int x1,out int y1)
        {
            x0=Mathf.Clamp(rect.xMin/IndexCell,0,cellsWide-1);y0=Mathf.Clamp(rect.yMin/IndexCell,0,cellsHigh-1);
            x1=Mathf.Clamp((rect.xMax-1)/IndexCell,0,cellsWide-1);y1=Mathf.Clamp((rect.yMax-1)/IndexCell,0,cellsHigh-1);
        }

        // Normalised ray points of a form: at most eight, spread over the source's own sampler order, weights sum to one.
        public static Vector4[] Normalise(IReadOnlyList<Vector4> points,string id)
        {
            if(points==null || points.Count==0) throw new ArgumentException("Источник света без точек формы: "+id);
            int count=Math.Min(DirectLight.MaximumSamples,points.Count);var selected=new Vector4[count];float sum=0;
            for(int i=0;i<count;i++)
            {
                var point=points[(int)((long)i*points.Count/count)];
                if(!float.IsFinite(point.x) || !float.IsFinite(point.y) || !(point.z>0)) throw new ArgumentException("Некорректная точка формы источника света: "+id);
                selected[i]=point;sum+=point.z;
            }
            for(int i=0;i<count;i++) selected[i].z/=sum;
            return selected;
        }
    }
}
