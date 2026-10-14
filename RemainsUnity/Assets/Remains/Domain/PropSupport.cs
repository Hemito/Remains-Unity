using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Vertical support uses live physical state, including container dimensions and anchors.
    // No ownership transfer: each simulator resolves only the object it already owns.
    public static class PropSupport
    {
        // Read-only observation of the same support rule used by physical movement.
        public static bool Supported(PropState moving,Vector2 feet,IEnumerable<PropState> neighbours)
        {
            var end=feet;
            return Resolve(moving,feet,ref end,neighbours);
        }
        public static bool Resolve(PropState moving,Vector2 start,ref Vector2 end,IEnumerable<PropState> neighbours)
        {
            if(!moving.hasPhysicalBody || neighbours==null || end.y>start.y) return false;
            if(neighbours is PropSupportIndex index) return ResolveIterator(moving,start,ref end,index.Near(moving,end.x).GetEnumerator());
            if(neighbours is WorldPropView live) return ResolveIterator(moving,start,ref end,live.GetEnumerator());
            return ResolveIterator(moving,start,ref end,neighbours.GetEnumerator());
        }
        private static bool ResolveIterator<T>(PropState moving,Vector2 start,ref Vector2 end,T iterator) where T:IEnumerator<PropState>
        {
            bool supported=false;
            const float epsilon=.0001f;
            try
            {
                while(iterator.MoveNext())
                {
                    var other=iterator.Current;
                    if(!other.hasPhysicalBody || ReferenceEquals(moving,other) || other.level!=moving.level) continue;
                    float reach=(moving.width+other.width)*.5f;
                    if(Mathf.Abs(end.x-other.x)>=reach-epsilon) continue;
                    float top=other.y-other.anchorHeight+other.height;
                    if(start.y<top-epsilon || end.y>top+epsilon) continue;
                    end.y=Mathf.Max(end.y,top);
                    supported=true;
                }
            }
            finally {iterator.Dispose();}
            return supported;
        }
    }
}
