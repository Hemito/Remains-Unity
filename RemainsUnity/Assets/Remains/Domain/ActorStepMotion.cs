using UnityEngine;

namespace Remains.Domain
{
    // A supported, controlled actor may lift over a small ledge. Ordinary
    // terrain motion remains unchanged for falling bodies, props and grenades.
    public static class ActorStepMotion
    {
        public static bool TryMove(RoomLayout room,MovementHull hull,Vector2 start,Vector2 delta,
            Vector2 blocked,float maximumHeight,out Vector2 result)
        {
            result=blocked;
            if(maximumHeight<=0 || Mathf.Abs(delta.x)<.0001f || delta.y>0 ||
                Mathf.Abs(blocked.x-start.x-delta.x)<.0001f || !ActorTerrainMotion.Supported(room,hull,start)) return false;
            float nextX=start.x+delta.x,half=hull.width*.5f,height=0;
            foreach(var surface in room.platforms)
            {
                if(nextX+half<=surface.x || nextX-half>=surface.x+surface.width) continue;
                float rise=RoomSupport.Top(surface,nextX,hull.width)-start.y;
                if(rise>.0001f && rise<=maximumHeight+.0001f) height=Mathf.Max(height,rise);
            }
            if(height==0) return false;
            var lifted=ActorTerrainMotion.Move(room,hull,start,Vector2.up*height,false);
            if(lifted.y<start.y+height-.0001f) return false;
            var across=ActorTerrainMotion.Move(room,hull,lifted,Vector2.right*delta.x,false);
            if(Mathf.Abs(across.x-nextX)>.0001f) return false;
            var landed=ActorTerrainMotion.Move(room,hull,across,Vector2.down*(height-delta.y),false);
            if(landed.y<=start.y+.0001f || !ActorTerrainMotion.IsClear(room,hull,landed) ||
                !ActorTerrainMotion.Supported(room,hull,landed)) return false;
            result=landed;return true;
        }
    }
}
