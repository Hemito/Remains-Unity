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
        public static Vector2 Move(RoomLayout room,MovementHull hull,Vector2 start,Vector2 delta,bool climbing)
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
            float y=start.y+delta.y;
            foreach(var surface in room.platforms)
            {
                if(x+half<=surface.x+Epsilon || x-half>=surface.x+surface.width-Epsilon) continue;
                float floor=surface.y+surface.height,ceiling=surface.y-hull.height;
                if(delta.y<=0 && !(climbing && surface.oneWay) && start.y>=floor-Epsilon && y<floor) y=floor;
                if(delta.y>0 && !surface.oneWay && start.y<=ceiling+Epsilon && y>ceiling) y=ceiling;
            }
            return new Vector2(x,y);
        }
        public static bool Supported(RoomLayout room,MovementHull hull,Vector2 position)
        {
            foreach(var surface in room.platforms)
                if(position.x+hull.width*.5f>surface.x+Epsilon && position.x-hull.width*.5f<surface.x+surface.width-Epsilon &&
                    Mathf.Abs(position.y-surface.y-surface.height)<=RoomSupport.ContactTolerance) return true;
            return false;
        }
    }
}
