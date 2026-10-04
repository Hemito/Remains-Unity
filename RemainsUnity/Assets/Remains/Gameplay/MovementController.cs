using Remains.Domain;
using Remains.Content;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class MovementController
    {
        private readonly RoomLayout room;
        public RoomLayout Room=>room;
        private readonly ReleasedTapGesture downTap;
        private readonly MovementSettings settings;
        private readonly SprintPreparation sprintPreparation=new();
        private float? droppedSurface;
        public MovementController(RoomLayout room,MovementSettings settings=null)
        {
            this.room=room;this.settings=settings??MovementSettings.Load();this.settings.Validate();
            downTap=new ReleasedTapGesture(this.settings.doubleTapSeconds);
        }
        public void Step(ActorState actor, Vector2 input, bool jump, float dt, bool sprint=false,bool jumpHeld=false)
        {
            ActorPositionRecovery.Repair(actor,room,"движение");
            bool dropPressed=downTap.Step(input.y<-.1f,dt);
            float accelerationTime=sprintPreparation.AccelerationTime(actor,input.x,sprint,dt,settings.sprintPreparationSeconds);
            if(droppedSurface.HasValue && Mathf.Abs(actor.y-droppedSurface.Value)>RoomSupport.ContactTolerance) droppedSurface=null;
            if (actor.down) { PassiveActorMotion.Step(actor,room,dt,settings);return; }
            var previous=actor.Position;
            var hull=Anatomy.Movement(actor);
            var selected=System.Array.Find(room.ladders,l=>Mathf.Abs(actor.x-l.x-l.movementOffset)<0.65f && actor.y>=l.bottom-0.1f && actor.y<l.top+0.2f);
            bool wasClimbing=actor.climbing && selected!=null;
            bool detach=jump && input.y<=.1f;
            bool grounded=ActorTerrainMotion.Supported(room,hull,actor.Position);
            // Up can catch a ladder in flight; down starts from support or continues an existing hold.
            bool ladder = selected!=null && !detach && (wasClimbing || input.y>.1f || grounded && input.y<-.1f);
            bool drop=dropPressed && grounded && ActorTerrainMotion.CanDrop(room,hull,actor.Position);
            bool wasCrouched=actor.crouched;
            CrouchMotion.Step(actor,room,settings,input,jump,grounded,ladder,drop,dt);
            hull=Anatomy.Movement(actor);
            if(actor.crouched) {ladder=false;jump=false;}
            if(grounded || drop || ladder) actor.jumpSupportRemaining=0;
            if(drop) { droppedSurface=actor.y;actor.vy=-settings.dropSpeed;ladder=false; }
            actor.vx = HorizontalMotion.Step(actor.vx,input.x,grounded || ladder,sprint,dt,settings,actor.crouched,actor.crouched?dt:accelerationTime);
            if (input.x != 0 && !ladder) actor.facing = input.x > 0 ? 1 : -1;
            bool launched=jump && (grounded || wasClimbing) && !drop && !ladder;
            float verticalDistance;
            if (ladder)
            {
                actor.facing=(int)selected.facing;
                actor.vy = input.y * settings.climbSpeed * (input.y<0?settings.climbDownMultiplier:1);
                if(Mathf.Abs(input.x)<.1f && dt>0) actor.vx=Mathf.Clamp((selected.x+selected.movementOffset-actor.x)/dt,-settings.walkSpeed,settings.walkSpeed);
                verticalDistance=actor.vy*dt;
            }
            else
            {
                verticalDistance=VerticalMotion.Step(actor,launched,jumpHeld,dt,settings);
            }
            var desired=new Vector2(actor.vx*dt,verticalDistance);
            actor.Position=ActorTerrainMotion.Move(room,hull,previous,desired,ladder,droppedSurface);
            if(Mathf.Abs(actor.x-previous.x-desired.x)>.0001f) actor.vx=0;
            if(Mathf.Abs(actor.y-previous.y-desired.y)>.0001f) {actor.vy=0;actor.jumpSupportRemaining=0;}
            if(ladder)
            {
                float y=Mathf.Clamp(actor.y,selected.bottom,selected.top);
                if(y!=actor.y) actor.vy=0;
                actor.y=y;
            }
            bool supported=ActorTerrainMotion.Supported(room,hull,actor.Position);
            if(ladder && Mathf.Abs(actor.x-selected.x-selected.movementOffset)>=.65f) ladder=false;
            if(ladder && supported && Mathf.Abs(input.y)<.1f &&
                (actor.y<=selected.bottom+.001f || actor.y>=selected.top-.001f)) ladder=false;
            actor.climbing=ladder;
            StanceAnimation.Select(actor,actor.crouched?(Mathf.Abs(actor.vx)>.1f?"crawl":"crouch"):ladder ? "climb" : !supported ? AirbornePose.Select(actor,launched,grounded || wasClimbing || drop) : Mathf.Abs(actor.vx) > 0.1f ? sprint ? "run" : "walk" : "idle",wasCrouched);
        }
    }
}
