using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    public static class PropMotion
    {
        public static void Step(PropState prop,RoomLayout room,float dt,Vector2? target,IEnumerable<PropState> neighbours=null)
        {
            if(dt<=0 || !prop.movable) return;
            var origin=prop.Position;
            var feet=origin-Vector2.up*prop.anchorHeight;
            var hull=new MovementHull {width=prop.width,height=prop.height};
            Vector2 delta;
            if(target.HasValue) delta=TelekinesisFollow.Displacement(origin,target.Value,dt);
            else {prop.vy-=WorldPhysics.Gravity*dt;delta=new Vector2(prop.vx,prop.vy)*dt;}
            bool lowerThroughPlatform=target.HasValue && delta.y<0;
            var moved=ActorTerrainMotion.Move(room,hull,feet,delta,ignoreOneWay:lowerThroughPlatform);
            bool supportedByProp=PropSupport.Resolve(prop,feet,ref moved,neighbours);
            prop.Position=moved+Vector2.up*prop.anchorHeight;
            var velocity=(prop.Position-origin)/dt;prop.vx=velocity.x;prop.vy=velocity.y;
            if(!target.HasValue && (supportedByProp || ActorTerrainMotion.Supported(room,hull,prop.Position-Vector2.up*prop.anchorHeight)))
            {prop.vy=0;prop.vx*=Mathf.Exp(-8*dt);}
        }
    }
}
