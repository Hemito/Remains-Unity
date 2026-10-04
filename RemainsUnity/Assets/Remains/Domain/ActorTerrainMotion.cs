using UnityEngine;

namespace Remains.Domain
{
    // Axis-ordered swept rectangle. Actor position is the middle of its feet.
    public static class ActorTerrainMotion
    {
        private const float Epsilon=.0001f;
        public static bool IsClear(RoomLayout room,MovementHull hull,Vector2 position)
        {
            float half=hull.width*.5f;
            if(!float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                position.x-half<room.left-Epsilon || position.x+half>room.right+Epsilon ||
                position.y<room.bottom-Epsilon || position.y+hull.height>room.top+Epsilon) return false;
            foreach(var surface in room.platforms)
                if(!surface.oneWay && position.x+half>surface.x+Epsilon && position.x-half<surface.x+surface.width-Epsilon &&
                    position.y+hull.height>surface.y+Epsilon && position.y<surface.y+surface.height-Epsilon) return false;
            return true;
        }
        public static Vector2 Move(RoomLayout room,MovementHull hull,Vector2 start,Vector2 delta,bool ignoreOneWay,float? droppedSurface=null)
        {
            float half=hull.width*.5f;
            float x=Mathf.Clamp(start.x+delta.x,room.left+half,room.right-half);
            foreach(var surface in room.platforms)
            {
                if(surface.oneWay || start.y>=surface.y+surface.height-Epsilon || start.y+hull.height<=surface.y+Epsilon) continue;
                float left=surface.x-half,right=surface.x+surface.width+half;
                if(delta.x>0 && start.x<=left+Epsilon && x>left) x=left;
                if(delta.x<0 && start.x>=right-Epsilon && x<right) x=right;
            }
            float baseY=start.y;
            if(delta.y<=0 && !ignoreOneWay && !droppedSurface.HasValue)
                if(SlopeContactMotion.TryFollow(room,hull,start,x,out float height))
                {
                    if(IsClear(room,hull,new Vector2(x,height))) baseY=height;
                    else x=start.x; // A ceiling must not push the actor through the ramp.
                }
            float y=baseY+delta.y;
            foreach(var surface in room.platforms)
            {
                if(x+half<=surface.x+Epsilon || x-half>=surface.x+surface.width-Epsilon) continue;
                if(surface.slopeDirection!=0 && droppedSurface.HasValue) continue;
                float floor=RoomSupport.Top(surface,x,hull.width),ceiling=surface.y-hull.height;
                if(surface.oneWay && droppedSurface.HasValue && Mathf.Abs(floor-droppedSurface.Value)<=RoomSupport.ContactTolerance) continue;
                if(delta.y<=0 && !(ignoreOneWay && surface.oneWay) && baseY>=floor-Epsilon && y<floor) y=floor;
                if(delta.y>0 && !surface.oneWay && start.y<=ceiling+Epsilon && y>ceiling) y=ceiling;
            }
            return new Vector2(x,y);
        }
        public static bool Supported(RoomLayout room,MovementHull hull,Vector2 position)
        {
            foreach(var surface in room.platforms)
                if(position.x+hull.width*.5f>surface.x+Epsilon && position.x-hull.width*.5f<surface.x+surface.width-Epsilon &&
                    Mathf.Abs(position.y-RoomSupport.Top(surface,position.x,hull.width))<=RoomSupport.ContactTolerance) return true;
            return false;
        }

        public static bool CanDrop(RoomLayout room,MovementHull hull,Vector2 position)
        {
            bool supported=false;
            foreach(var surface in room.platforms)
            {
                if(position.x+hull.width*.5f<=surface.x+Epsilon || position.x-hull.width*.5f>=surface.x+surface.width-Epsilon ||
                    Mathf.Abs(position.y-RoomSupport.Top(surface,position.x,hull.width))>RoomSupport.ContactTolerance) continue;
                if(!surface.oneWay) return false;
                supported=true;
            }
            return supported;
        }
    }
}
