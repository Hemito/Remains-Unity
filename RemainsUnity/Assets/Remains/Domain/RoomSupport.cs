using UnityEngine;

namespace Remains.Domain
{
    public static class RoomSupport
    {
        public const float ContactTolerance=0.02f;

        public static bool ContainsX(RoomSurface surface,float x)=>x>=surface.x && x<=surface.x+surface.width;
        // Highest contact under a horizontal footprint; one rule for actors, bodies and objects.
        public static float Top(RoomSurface surface,float x,float width=0)
        {
            if(surface.slopeDirection==0) return surface.y+surface.height;
            float sample=Mathf.Clamp(x+Mathf.Sign(surface.slopeDirection)*width*.5f,surface.x,surface.x+surface.width);
            float fraction=(sample-surface.x)/surface.width;
            return surface.y+surface.height*(surface.slopeDirection>0?fraction:1-fraction);
        }

        public static bool IsSupported(RoomLayout room,Vector2 position)
        {
            foreach(var surface in RoomSurfaces.Near(room,new Rect(position.x,position.y-ContactTolerance,0,ContactTolerance*2)))
                if(ContainsX(surface,position.x) && Mathf.Abs(position.y-Top(surface,position.x))<=ContactTolerance) return true;
            return false;
        }

        public static bool TryLanding(RoomLayout room,Vector2 previous,Vector2 next,bool climbing,out float height)
        {
            height=float.NegativeInfinity;
            if(next.y>previous.y) return false;
            foreach(var surface in RoomSurfaces.Near(room,Rect.MinMaxRect(next.x,next.y,next.x,previous.y+ContactTolerance)))
            {
                if(climbing && surface.oneWay || !ContainsX(surface,next.x)) continue;
                float top=Top(surface,next.x);
                if(previous.y>=top-ContactTolerance && next.y<=top) height=Mathf.Max(height,top);
            }
            return !float.IsNegativeInfinity(height);
        }

        public static bool TryLowestFloor(RoomLayout room,float x,out float height,bool includeOneWay=false,float width=0)
        {
            height=float.PositiveInfinity;
            foreach(var surface in RoomSurfaces.All(room))
                if((includeOneWay || !surface.oneWay) && ContainsX(surface,x)) height=Mathf.Min(height,Top(surface,x,width));
            return !float.IsPositiveInfinity(height);
        }
    }
}
