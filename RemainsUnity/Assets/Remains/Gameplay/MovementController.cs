using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class MovementController
    {
        public const float Gravity = WorldPhysics.Gravity;
        private readonly RoomLayout room;
        private readonly ReleasedTapGesture downTap=new(5f/30);
        private float? droppedSurface;
        public MovementController(RoomLayout room) => this.room=room;
        public void Step(ActorState actor, Vector2 input, bool jump, float dt, bool sprint=false)
        {
            ActorPositionRecovery.Repair(actor,room,"движение");
            bool dropPressed=downTap.Step(input.y<-.1f,dt);
            if(droppedSurface.HasValue && Mathf.Abs(actor.y-droppedSurface.Value)>RoomSupport.ContactTolerance) droppedSurface=null;
            if (actor.down) { actor.vx = 0; actor.vy = 0; return; }
            var previous=actor.Position;
            var hull=Anatomy.Movement(actor.anatomy);
            var selected=System.Array.Find(room.ladders,l=>Mathf.Abs(actor.x-l.x-l.movementOffset)<0.65f && actor.y>=l.bottom-0.1f && actor.y<l.top+0.2f);
            bool ladder = selected!=null && Mathf.Abs(input.y) > 0.1f;
            bool grounded=ActorTerrainMotion.Supported(room,hull,actor.Position);
            bool drop=dropPressed && grounded && ActorTerrainMotion.CanDrop(room,hull,actor.Position);
            if(drop) { droppedSurface=actor.y;actor.vy=-6;ladder=false; }
            actor.vx = input.x * (sprint ? 4 : 2.5f);
            if (input.x != 0) actor.facing = input.x > 0 ? 1 : -1;
            if (ladder)
            {
                actor.vy = input.y * 3;
                if(Mathf.Abs(input.x)<.1f && dt>0) actor.vx=Mathf.Clamp((selected.x+selected.movementOffset-actor.x)/dt,-2.5f,2.5f);
            }
            else
            {
                if (jump && grounded && !drop) actor.vy = 8;
                actor.vy -= Gravity * dt;
            }
            var desired=actor.Velocity*dt;
            actor.Position=ActorTerrainMotion.Move(room,hull,previous,desired,ladder,droppedSurface);
            if(Mathf.Abs(actor.x-previous.x-desired.x)>.0001f) actor.vx=0;
            if(Mathf.Abs(actor.y-previous.y-desired.y)>.0001f) actor.vy=0;
            if(ladder)
            {
                float y=Mathf.Clamp(actor.y,selected.bottom,selected.top);
                if(y!=actor.y) actor.vy=0;
                actor.y=y;
            }
            bool supported=ActorTerrainMotion.Supported(room,hull,actor.Position);
            ActorPose.Select(actor,ladder ? "climb" : !supported ? "jump" : Mathf.Abs(actor.vx) > 0.1f ? sprint ? "run" : "walk" : "idle");
        }
    }
}
