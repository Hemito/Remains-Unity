using UnityEngine;

namespace Remains.Domain
{
    public static class RoomSupport
    {
        public const float ContactTolerance=0.02f;

        public static bool ContainsX(RoomSurface surface,float x)=>x>=surface.x && x<=surface.x+surface.width;

        public static bool IsSupported(RoomLayout room,Vector2 position)
        {
            foreach(var surface in room.platforms)
                if(ContainsX(surface,position.x) && Mathf.Abs(position.y-surface.y-surface.height)<=ContactTolerance) return true;
            return false;
        }

        public static bool TryLanding(RoomLayout room,Vector2 previous,Vector2 next,bool climbing,out float height)
        {
            height=float.NegativeInfinity;
            if(next.y>previous.y) return false;
            foreach(var surface in room.platforms)
            {
                if(climbing && surface.oneWay || !ContainsX(surface,next.x)) continue;
                float top=surface.y+surface.height;
                if(previous.y>=top-ContactTolerance && next.y<=top) height=Mathf.Max(height,top);
            }
            return !float.IsNegativeInfinity(height);
        }

        public static bool TryLowestFloor(RoomLayout room,float x,out float height)
        {
            height=float.PositiveInfinity;
            foreach(var surface in room.platforms)
                if(!surface.oneWay && ContainsX(surface,x)) height=Mathf.Min(height,surface.y+surface.height);
            return !float.IsPositiveInfinity(height);
        }
    }
}
