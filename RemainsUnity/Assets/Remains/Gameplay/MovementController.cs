using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class MovementController
    {
        public const float Gravity = 20;
        private readonly RoomLayout room;
        public MovementController(RoomLayout room) => this.room=room;
        public void Step(ActorState actor, Vector2 input, bool jump, float dt)
        {
            if (actor.down) { actor.vx = 0; actor.vy = 0; return; }
            var previous=actor.Position;
            var selected=System.Array.Find(room.ladders,l=>Mathf.Abs(actor.x-l.x)<0.55f && actor.y>=l.bottom-0.1f && actor.y<l.top+0.2f);
            bool ladder = selected!=null && Mathf.Abs(input.y) > 0.1f;
            bool grounded=System.Array.Exists(room.platforms,p=>actor.x>=p.x && actor.x<=p.x+p.width && Mathf.Abs(actor.y-p.y-p.height)<0.03f);
            actor.vx = input.x * 4;
            if (input.x != 0) actor.facing = input.x > 0 ? 1 : -1;
            if (ladder) actor.vy = input.y * 3;
            else
            {
                if (jump && grounded) actor.vy = 8;
                actor.vy -= Gravity * dt;
            }
            actor.Position += actor.Velocity * dt;
            actor.x = Mathf.Clamp(actor.x, room.left, room.right);
            if(!ladder && actor.vy<=0)
                foreach(var platform in room.platforms)
                {
                    float top=platform.y+platform.height;
                    if(actor.x>=platform.x && actor.x<=platform.x+platform.width && previous.y>=top-0.02f && actor.y<=top)
                    { actor.y=top; actor.vy=0; }
                }
            bool supported=System.Array.Exists(room.platforms,p=>actor.x>=p.x && actor.x<=p.x+p.width && Mathf.Abs(actor.y-p.y-p.height)<0.03f);
            ActorPose.Select(actor,ladder ? "climb" : !supported ? "jump" : Mathf.Abs(actor.vx) > 0.1f ? "run" : "idle");
        }
    }
}
