using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public static class ActorHoldMotion
    {
        public static void Step(ActorState body,RoomLayout room,float dt)
        {
            if(dt<=0 || !ActorHoldRules.Active(body)) return;
            body.climbing=false;body.jumpSupportRemaining=0;
            var hull=Anatomy.Movement(body);var origin=body.Position;
            var target=body.hold.Target-Vector2.up*(hull.height*.5f);
            target.y=Mathf.Clamp(target.y,room.bottom,room.top-hull.height);
            var delta=TelekinesisFollow.Displacement(origin,target,dt);
            body.Position=ActorTerrainMotion.Move(room,hull,origin,delta,ignoreOneWay:delta.y<0);
            body.Velocity=(body.Position-origin)/dt;
            ActorPose.Select(body,body.down?"down":"jump");
        }
    }
}
