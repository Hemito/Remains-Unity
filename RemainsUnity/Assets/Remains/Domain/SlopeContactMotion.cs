using UnityEngine;

namespace Remains.Domain
{
    // Follow the support envelope only from grounded contact. Flat ledges do not become steps.
    public static class SlopeContactMotion
    {
        public static bool TryFollow(RoomLayout room,MovementHull hull,Vector2 start,float nextX,out float height)
        {
            height=float.NegativeInfinity;
            bool grounded=false;float initialGradient=0,half=hull.width*.5f;
            foreach(var surface in room.platforms)
            {
                if(start.x+half<surface.x || start.x-half>surface.x+surface.width ||
                    Mathf.Abs(start.y-RoomSupport.Top(surface,start.x,hull.width))>RoomSupport.ContactTolerance) continue;
                grounded=true;
                if(surface.slopeDirection!=0) initialGradient=Mathf.Max(initialGradient,surface.height/surface.width);
            }
            if(!grounded) return false;
            foreach(var surface in room.platforms)
            {
                if(nextX+half<=surface.x || nextX-half>=surface.x+surface.width ||
                    initialGradient==0 && surface.slopeDirection==0) continue;
                float gradient=surface.slopeDirection==0?initialGradient:Mathf.Max(initialGradient,surface.height/surface.width);
                float top=RoomSupport.Top(surface,nextX,hull.width);
                if(Mathf.Abs(top-start.y)<=gradient*Mathf.Abs(nextX-start.x)+RoomSupport.ContactTolerance)
                    height=Mathf.Max(height,top);
            }
            return !float.IsNegativeInfinity(height);
        }
    }
}
