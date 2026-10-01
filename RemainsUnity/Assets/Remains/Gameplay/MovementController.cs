using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class MovementController
    {
        public const float Gravity = WorldPhysics.Gravity;
        private readonly RoomLayout room;
        public MovementController(RoomLayout room) => this.room=room;
        public void Step(ActorState actor, Vector2 input, bool jump, float dt, bool sprint=false)
        {
            ActorPositionRecovery.Repair(actor,room,"движение");
            if (actor.down) { actor.vx = 0; actor.vy = 0; return; }
            var previous=actor.Position;
            var selected=System.Array.Find(room.ladders,l=>Mathf.Abs(actor.x-l.x)<0.55f && actor.y>=l.bottom-0.1f && actor.y<l.top+0.2f);
            bool ladder = selected!=null && Mathf.Abs(input.y) > 0.1f;
            bool grounded=RoomSupport.IsSupported(room,actor.Position);
            actor.vx = input.x * (sprint ? 4 : 2.5f);
            if (input.x != 0) actor.facing = input.x > 0 ? 1 : -1;
            if (ladder) actor.vy = input.y * 3;
            else
            {
                if (jump && grounded) actor.vy = 8;
                actor.vy -= Gravity * dt;
            }
            actor.Position += actor.Velocity * dt;
            actor.x = Mathf.Clamp(actor.x, room.left, room.right);
            if(ladder)
            {
                float y=Mathf.Clamp(actor.y,selected.bottom,selected.top);
                if(y!=actor.y) actor.vy=0;
                actor.y=y;
            }
            if(RoomSupport.TryLanding(room,previous,actor.Position,ladder,out float landing)) {actor.y=landing;actor.vy=0;}
            bool supported=RoomSupport.IsSupported(room,actor.Position);
            ActorPose.Select(actor,ladder ? "climb" : !supported ? "jump" : Mathf.Abs(actor.vx) > 0.1f ? sprint ? "run" : "walk" : "idle");
        }
    }
}
