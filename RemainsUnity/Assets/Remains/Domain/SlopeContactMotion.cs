using UnityEngine;

namespace Remains.Domain
{
    // Follow the support envelope only from grounded contact. Flat ledges do not become steps.
    public static class SlopeContactMotion
    {
        private const float FlatHeightTolerance=.0001f;

        public static bool TryFollow(RoomLayout room,MovementHull hull,Vector2 start,float nextX,out float height,RoomSurface excludedSurface=null)
            =>TryFollow(room,hull,start,nextX,out height,excludedSurface,keepFlatPath:false);

        internal static bool TryFollow(RoomLayout room,MovementHull hull,Vector2 start,float nextX,out float height,RoomSurface excludedSurface,bool keepFlatPath)
        {
            height=float.NegativeInfinity;
            bool grounded=false,flatStart=false;float initialGradient=0,half=hull.width*.5f;
            foreach(var surface in RoomSurfaces.Near(room,new Rect(start.x-half,start.y-RoomSupport.ContactTolerance,
                hull.width,RoomSupport.ContactTolerance*2),excludedSurface))
            {
                if(start.x+half<surface.x || start.x-half>surface.x+surface.width ||
                    Mathf.Abs(start.y-RoomSupport.Top(surface,start.x,hull.width))>RoomSupport.ContactTolerance) continue;
                grounded=true;
                if(surface.slopeDirection!=0) initialGradient=Mathf.Max(initialGradient,surface.height/surface.width);
                else if(keepFlatPath && OnFlat(surface,start.y) &&
                    start.x+half>surface.x+FlatHeightTolerance && start.x-half<surface.x+surface.width-FlatHeightTolerance)
                    flatStart=true;
            }
            if(!grounded) return false;
            float travel=nextX-start.x;
            float distance=Mathf.Abs(travel);
            float reach=Mathf.Max(initialGradient,RoomSurfaces.MaximumSlopeGradient(room))*distance+RoomSupport.ContactTolerance;
            float leadingEdge=flatStart?nextX+Mathf.Sign(travel)*half:0;
            bool flatAhead=flatStart && travel==0;
            foreach(var surface in RoomSurfaces.Near(room,new Rect(nextX-half,start.y-reach,hull.width,reach*2),excludedSurface))
            {
                // At a required ramp the floor can still support the trailing half of the hull.
                if(flatStart && !flatAhead && OnFlat(surface,start.y) &&
                    leadingEdge>=surface.x-FlatHeightTolerance && leadingEdge<=surface.x+surface.width+FlatHeightTolerance &&
                    (travel>0?leadingEdge<surface.x+surface.width-FlatHeightTolerance:leadingEdge>surface.x+FlatHeightTolerance))
                    flatAhead=true;
                if(nextX+half<=surface.x || nextX-half>=surface.x+surface.width ||
                    initialGradient==0 && surface.slopeDirection==0) continue;
                float gradient=surface.slopeDirection==0?initialGradient:Mathf.Max(initialGradient,surface.height/surface.width);
                float top=RoomSupport.Top(surface,nextX,hull.width);
                if(Mathf.Abs(top-start.y)<=gradient*distance+RoomSupport.ContactTolerance)
                    height=Mathf.Max(height,top);
            }
            if(flatAhead && height>start.y) height=float.NegativeInfinity;
            return !float.IsNegativeInfinity(height);
        }

        private static bool OnFlat(RoomSurface surface,float y)
            =>surface.slopeDirection==0 && Mathf.Abs(surface.y+surface.height-y)<=FlatHeightTolerance;
    }
}
