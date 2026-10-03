using UnityEngine;

namespace Remains.Domain
{
    // Shared segment/rectangle intersection; callers choose which surfaces block their query.
    public static class SurfaceContact
    {
        public static float? Entry(RoomSurface surface,Vector2 start,Vector2 end)
        {
            float enter=0,exit=1;var delta=end-start;
            return Clip(start.x,delta.x,surface.x,surface.x+surface.width,ref enter,ref exit) &&
                Clip(start.y,delta.y,surface.y,surface.y+surface.height,ref enter,ref exit)?enter:null;
        }
        private static bool Clip(float start,float delta,float min,float max,ref float enter,ref float exit)
        {
            if(delta==0) return start>=min && start<=max;
            float a=(min-start)/delta,b=(max-start)/delta;
            enter=Mathf.Max(enter,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));
            return enter<=exit;
        }
    }
    public static class RoomVisibility
    {
        public static bool Clear(RoomLayout room,Vector2 start,Vector2 end)
        {
            foreach(var surface in room.platforms)
                if(!surface.oneWay && SurfaceContact.Entry(surface,start,end).HasValue) return false;
            return true;
        }
    }
}
