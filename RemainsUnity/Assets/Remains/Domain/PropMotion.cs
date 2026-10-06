using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    public static class PropMotion
    {
        public static void Step(PropState prop,RoomLayout room,float dt,Vector2? target,IEnumerable<PropState> neighbours=null,PassengerMotionState passenger=null)
        {
            if(dt<=0 || !prop.movable || !prop.hasPhysicalBody) return;
            Step(prop,room,dt,target,neighbours,passenger,new MovementHull());
        }
        // A simulation owner reuses this scratch hull; it never belongs to saved state.
        public static void Step(PropState prop,RoomLayout room,float dt,Vector2? target,IEnumerable<PropState> neighbours,PassengerMotionState passenger,MovementHull hull)
        {
            if(dt<=0 || !prop.movable || !prop.hasPhysicalBody) return;
            var feet=prop.Position-Vector2.up*prop.anchorHeight;
            hull.width=prop.width;hull.height=prop.height;
            feet=PassengerMotion.Carry(room,hull,feet,dt,passenger,!target.HasValue && !prop.held && prop.vy<=0);
            prop.Position=feet+Vector2.up*prop.anchorHeight;
            var origin=prop.Position; // Relative velocity excludes the support displacement; no feedback/drift.
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
