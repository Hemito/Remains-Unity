using UnityEngine;

namespace Remains.Domain
{
    public static class PropMotion
    {
        public static void Step(PropState prop,RoomLayout room,float dt,Vector2? target)
        {
            if(dt<=0 || !prop.movable) return;
            var origin=prop.Position;
            var feet=origin-Vector2.up*prop.anchorHeight;
            var hull=new MovementHull {width=prop.width,height=prop.height};
            Vector2 delta;
            if(target.HasValue) delta=(target.Value-origin)*(1-Mathf.Exp(-8*dt));
            else {prop.vy-=WorldPhysics.Gravity*dt;delta=new Vector2(prop.vx,prop.vy)*dt;}
            prop.Position=ActorTerrainMotion.Move(room,hull,feet,delta,false)+Vector2.up*prop.anchorHeight;
            var velocity=(prop.Position-origin)/dt;prop.vx=velocity.x;prop.vy=velocity.y;
            if(!target.HasValue && ActorTerrainMotion.Supported(room,hull,prop.Position-Vector2.up*prop.anchorHeight))
            {prop.vy=0;prop.vx*=Mathf.Exp(-8*dt);}
        }
    }
}
