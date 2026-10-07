using Remains.Domain;
using Remains.Content;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class MovementController
    {
        private readonly RoomLayout room;
        public RoomLayout Room=>room;
        public event System.Action<ActorState> PositionRecovered;
        private readonly ReleasedTapGesture downTap;
        private readonly MovementSettings settings;
        private readonly LadderEntryPose ladderEntry;
        private readonly StandingSupportPose standingPose=new();
        private readonly SlopeLocomotionPose slopePose=new();
        private readonly SprintPreparation sprintPreparation=new();
        private readonly PassengerMotionRegistry passengers=new();
        private float? droppedSurface;
        public MovementController(RoomLayout room,MovementSettings settings=null,LadderEntryPose ladderEntry=null)
        {
            this.room=room;this.settings=settings??MovementSettings.Load();this.settings.Validate();
            this.ladderEntry=ladderEntry??new LadderEntryPose();
            downTap=new ReleasedTapGesture(this.settings.doubleTapSeconds);
        }
        public void Step(ActorState actor, Vector2 input, bool jump, float dt, bool sprint=false,bool jumpHeld=false,PassengerMotionState passenger=null,float? lookAtX=null)
        {
            if(lookAtX.HasValue && !float.IsFinite(lookAtX.Value))
                throw new System.InvalidOperationException($"Некорректная цель взгляда: actor={actor?.id}, room={room.id}, lookAtX={lookAtX.Value}.");
            if(ActorPositionRecovery.Repair(actor,room,"движение")) PositionRecovered?.Invoke(actor);
            bool dropPressed=downTap.Step(input.y<-.1f,dt);
            float accelerationTime=sprintPreparation.AccelerationTime(actor,input.x,sprint,dt,settings.sprintPreparationSeconds);
            if(droppedSurface.HasValue && Mathf.Abs(actor.y-droppedSurface.Value)>RoomSupport.ContactTolerance) droppedSurface=null;
            if(room.dynamicSurfaces?.Count>0) passenger??=passengers.ForActor(actor);
            if (ActorSessionRules.IsPassive(actor)) { PassiveActorMotion.Step(actor,room,dt,settings,passenger);return; }
            var previous=actor.Position;
            var hull=Anatomy.Movement(actor);
            var selected=System.Array.Find(room.ladders,l=>Mathf.Abs(actor.x-l.x-l.movementOffset)<0.65f && actor.y>=l.bottom-0.1f && actor.y<l.top+0.2f);
            bool wasClimbing=actor.climbing && selected!=null;
            bool detach=jump && input.y<=.1f;
            bool grounded=ActorTerrainMotion.Supported(room,hull,actor.Position) || PassengerMotion.PreviouslySupported(room,hull,actor.Position,dt);
            bool endpoint=selected!=null && (actor.y<=selected.bottom+.001f || actor.y>=selected.top-.001f);
            bool stepOff=wasClimbing && (grounded || endpoint) && Mathf.Abs(input.x)>.1f && Mathf.Abs(input.y)<.1f;
            // Up can catch a ladder in flight; down starts from support or continues an existing hold.
            bool ladder = selected!=null && !detach && !stepOff && (wasClimbing ||
                input.y>.1f && actor.y<selected.top-.001f || grounded && input.y<-.1f && actor.y>selected.bottom+.001f);
            bool drop=dropPressed && grounded && ActorTerrainMotion.CanDrop(room,hull,actor.Position);
            bool wasCrouched=actor.crouched;
            CrouchMotion.Step(actor,room,settings,input,jump,grounded,ladder,drop,dt);
            hull=Anatomy.Movement(actor);
            if(actor.crouched) {ladder=false;jump=false;}
            actor.Position=PassengerMotion.Carry(room,hull,actor.Position,dt,passenger,
                !ladder && !drop && !(jump && (grounded || wasClimbing)) && actor.vy<=0 && !ActorHoldRules.Active(actor));
            previous=actor.Position;
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
                // A held body stays at its grip even while horizontal input is pressed.
                actor.vx=dt>0?Mathf.Clamp((selected.x+selected.movementOffset-actor.x)/dt,-settings.walkSpeed,settings.walkSpeed):0;
                var nextBody=actor.Position+new Vector2(actor.vx*dt,actor.vy*dt);
                if(wasClimbing && actor.vy<0 && LadderPoseContact.ShouldRelease(actor,room,nextBody))
                {
                    ladder=false;
                    verticalDistance=VerticalMotion.Step(actor,false,jumpHeld,dt,settings);
                }
                else verticalDistance=actor.vy*dt;
            }
            else
            {
                verticalDistance=VerticalMotion.Step(actor,launched,jumpHeld,dt,settings);
            }
            var desired=new Vector2(actor.vx*dt,verticalDistance);
            actor.Position=ActorTerrainMotion.Move(room,hull,previous,desired,ladder,droppedSurface);
            if(grounded && !actor.crouched && !ladder && !drop && !launched &&
                ActorStepMotion.TryMove(room,hull,previous,desired,actor.Position,settings.stepHeight,out var stepped))
                actor.Position=stepped;
            if(Mathf.Abs(actor.x-previous.x-desired.x)>.0001f) actor.vx=0;
            if(Mathf.Abs(actor.y-previous.y-desired.y)>.0001f) {actor.vy=0;actor.jumpSupportRemaining=0;}
            if(ladder)
            {
                float y=Mathf.Clamp(actor.y,selected.bottom,selected.top);
                actor.y=y;
            }
            bool supported=ActorTerrainMotion.Supported(room,hull,actor.Position);
            if(ladder && Mathf.Abs(actor.x-selected.x-selected.movementOffset)>=.65f) ladder=false;
            if(ladder && supported && Mathf.Abs(input.y)<.1f &&
                (actor.y<=selected.bottom+.001f || actor.y>=selected.top-.001f)) ladder=false;
            if(ladder && (input.y>.1f && actor.y>=selected.top-.001f || input.y<-.1f && actor.y<=selected.bottom+.001f))
            {
                ladder=false;
                // A real landing stops the climb; an unsupported end releases the
                // same body into normal gravity with its existing climb momentum.
                if(supported) actor.vy=0;
            }
            actor.climbing=ladder;
            if(!ladder && lookAtX.HasValue) actor.facing=lookAtX.Value>=actor.x?1:-1;
            StanceAnimation.Select(actor,actor.crouched?(Mathf.Abs(actor.vx)>.1f?"crawl":"crouch"):ladder ? ladderEntry.Select(actor,room) : !supported ? AirbornePose.Select(actor,launched,grounded || wasClimbing || drop) : Mathf.Abs(actor.vx) > 0.1f ? sprint ? "run" : "walk" : "idle",wasCrouched,standingPose,room,slopePose);
        }
    }
}
