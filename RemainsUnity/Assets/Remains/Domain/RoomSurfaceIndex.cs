using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Immutable broad phase, prepared only when authored geometry is published.
    public sealed class RoomSurfaceIndex
    {
        internal const float CellSize=4;
        internal readonly RoomSurface[] Source;
        private readonly Dictionary<(int x,int y),int[]> cells=new();
        public float MaximumSlopeGradient {get;}
        public RoomSurfaceIndex(RoomSurface[] surfaces)
        {
            Source=surfaces??throw new ArgumentNullException(nameof(surfaces));
            var building=new Dictionary<(int x,int y),List<int>>();
            for(int i=0;i<surfaces.Length;i++)
            {
                var s=surfaces[i];
                if(s==null || !float.IsFinite(s.x) || !float.IsFinite(s.y) || !float.IsFinite(s.width) ||
                    !float.IsFinite(s.height) || s.width<0 || s.height<0)
                    throw new InvalidOperationException($"Некорректная поверхность индекса: index={i}.");
                if(s.slopeDirection!=0 && s.width>0) MaximumSlopeGradient=Mathf.Max(MaximumSlopeGradient,s.height/s.width);
                int left=Cell(s.x),right=Cell(s.x+s.width),bottom=Cell(s.y),top=Cell(s.y+s.height);
                for(int y=bottom;y<=top;y++) for(int x=left;x<=right;x++)
                {
                    if(!building.TryGetValue((x,y),out var bucket)) building.Add((x,y),bucket=new List<int>());
                    bucket.Add(i);
                }
            }
            foreach(var pair in building) cells.Add(pair.Key,pair.Value.ToArray());
        }
        internal static int Cell(float position)=>Mathf.FloorToInt(position/CellSize);
        // Merge the sorted buckets without mutable query buffers. The authored order
        // is preserved, duplicates disappear, and nested queries remain independent.
        internal int Next(int after,int left,int right,int bottom,int top)
        {
            int next=int.MaxValue;
            for(int y=bottom;y<=top;y++) for(int x=left;x<=right;x++)
            {
                if(!cells.TryGetValue((x,y),out var bucket)) continue;
                int low=0,high=bucket.Length;
                while(low<high) {int middle=(low+high)/2;if(bucket[middle]<=after) low=middle+1;else high=middle;}
                if(low<bucket.Length && bucket[low]<next) next=bucket[low];
            }
            return next==int.MaxValue?-1:next;
        }
    }
}
