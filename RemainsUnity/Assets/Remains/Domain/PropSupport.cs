using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Vertical support uses live physical state, including container dimensions and anchors.
    // No ownership transfer: each simulator resolves only the object it already owns.
    public static class PropSupport
    {
        public static bool Resolve(PropState moving,Vector2 start,ref Vector2 end,IEnumerable<PropState> neighbours)
        {
            if(neighbours==null || end.y>start.y) return false;
            bool supported=false;
            const float epsilon=.0001f;
            foreach(var other in neighbours)
            {
                if(ReferenceEquals(moving,other) || other.level!=moving.level) continue;
                float reach=(moving.width+other.width)*.5f;
                if(Mathf.Abs(end.x-other.x)>=reach-epsilon) continue;
                float top=other.y-other.anchorHeight+other.height;
                if(start.y<top-epsilon || end.y>top+epsilon) continue;
                end.y=Mathf.Max(end.y,top);
                supported=true;
            }
            return supported;
        }
    }
}
