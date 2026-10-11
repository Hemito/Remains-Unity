using UnityEngine;

namespace Remains.Domain
{
    // Follow the support envelope only from grounded contact. Flat ledges do not become steps.
    public static class SlopeContactMotion
    {
        public static bool TryFollow(RoomLayout room,MovementHull hull,Vector2 start,float nextX,out float height,RoomSurface excludedSurface=null)
        {
            height=float.NegativeInfinity;
            bool grounded=false;float initialGradient=0,half=hull.width*.5f;
            foreach(var surface in RoomSurfaces.Near(room,new Rect(start.x-half,start.y-RoomSupport.ContactTolerance,
                hull.width,RoomSupport.ContactTolerance*2),excludedSurface))
            {
                if(start.x+half<surface.x || start.x-half>surface.x+surface.width ||
                    Mathf.Abs(start.y-RoomSupport.Top(surface,start.x,hull.width))>RoomSupport.ContactTolerance) continue;
                grounded=true;
                if(surface.slopeDirection!=0) initialGradient=Mathf.Max(initialGradient,surface.height/surface.width);
            }
            if(!grounded) return false;
            float reach=Mathf.Max(initialGradient,RoomSurfaces.MaximumSlopeGradient(room))*Mathf.Abs(nextX-start.x)+RoomSupport.ContactTolerance;
            foreach(var surface in RoomSurfaces.Near(room,new Rect(nextX-half,start.y-reach,hull.width,reach*2),excludedSurface))
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
