using UnityEngine;

namespace Remains.Domain
{
    public static class ShotObstacles
    {
        public static float Limit(RoomLayout room,Vector2 start,Vector2 end)
        {
            float nearest=1;
            foreach(var surface in room.platforms)
            {
                if(surface.shotsPassThrough) continue;
                float enter=0,exit=1;var delta=end-start;
                if(Clip(start.x,delta.x,surface.x,surface.x+surface.width,ref enter,ref exit) &&
                    Clip(start.y,delta.y,surface.y,surface.y+surface.height,ref enter,ref exit))
                    nearest=Mathf.Min(nearest,enter);
            }
            return nearest;
        }
        private static bool Clip(float start,float delta,float min,float max,ref float enter,ref float exit)
        {
            if(delta==0) return start>=min && start<=max;
            float a=(min-start)/delta,b=(max-start)/delta;
            enter=Mathf.Max(enter,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));
            return enter<=exit;
        }
    }
}
