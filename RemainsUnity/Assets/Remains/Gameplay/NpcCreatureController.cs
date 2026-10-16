using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // AI of the original creature families that are not crawlers, after their classes (times: original frames / 30):
    //   bat   (UnitBat):   hangs (stay) until it notices a target; flies to it; near it (380 px) either hovers 20
    //                      frames and dives at 2.5 × run for 20 frames (attack pose) or chases at run for 100–200
    //                      frames; without a target flies up at 45° to a ceiling and hangs again.
    //   bloat (UnitBloat): drifts at half speed changing direction; every 20–70 frames, with a target, dashes at it at
    //                      run for 20 frames when it is within attr (400 px) and the attch chance hits, else approaches.
    //   slime (UnitSlime): crawls along the floor turning at walls and ledges; one under a ceiling hangs upside down
    //                      (its drip is not carried over yet) and falls when the ceiling is gone.
    // All of them hurt by touch (Unit.attKorp → udarUnit): a contact attack without preparation; the pause after it
    // stands for the victim's 30-frame window between touch hits.
    public sealed class NpcCreatureController
    {
        private const float Frame=1f/30;
        private readonly System.Random random=new();
        private float Range(float minFrames,float maxFrames)=>(minFrames+(float)random.NextDouble()*(maxFrames-minFrames))*Frame;
        private bool Chance(float p)=>random.NextDouble()<p;

        public void Step(ActorState actor,ActorTypeDefinition type,ActorState target,Vector2? known,RoomLayout room,MovementController ground,float dt)
        {
            actor.creature??=Initial(actor,type,room);
            var state=actor.creature;
            state.timer=Mathf.Max(0,state.timer-dt);
            switch(type.behavior)
            {
                case NpcCreatureBehaviours.Bat: Bat(actor,type,state,known,room,dt);break;
                case NpcCreatureBehaviours.Bloat: Bloat(actor,type,state,known,room,dt);break;
                case NpcCreatureBehaviours.Slime: Slime(actor,state,room,ground,dt);break;
                default: throw new InvalidOperationException($"Неизвестное поведение существа {type.behavior}: actor={actor.id}.");
            }
        }

        // While a touch resolves (attackBusy) a flyer keeps gliding on its velocity; walkers use their ground motion.
        public void Coast(ActorState actor,ActorTypeDefinition type,RoomLayout room,float dt)
        {if(NpcCreatureBehaviours.Flies(type.behavior)) FlyerMotion.Step(actor,room,new Vector2(actor.vx,actor.vy),type.motion.acceleration,dt);}

        private NpcCreatureState Initial(ActorState actor,ActorTypeDefinition type,RoomLayout room)
        {
            var hull=Anatomy.Movement(actor);
            if(type.behavior==NpcCreatureBehaviours.Slime)
            {
                // UnitSlime.putLoc: on a floor it crawls; with solid ground 1 m above and none below it hangs.
                bool floor=ActorTerrainMotion.Supported(room,hull,actor.Position,false);
                bool ceiling=!ActorTerrainMotion.IsClear(room,hull,actor.Position+Vector2.up*1f);
                return new NpcCreatureState {mode=!floor && ceiling?"hang":"crawl",inverted=!floor && ceiling,directionX=actor.facing};
            }
            if(type.behavior==NpcCreatureBehaviours.Bat) return new NpcCreatureState {mode="hang",timer=Range(5,15)};
            return new NpcCreatureState {mode="drift",directionX=Chance(.5f)?.7f:-.7f,directionY=Chance(.5f)?.7f:-.7f,timer=Range(5,15)};
        }

        private void Bat(ActorState actor,ActorTypeDefinition type,NpcCreatureState state,Vector2? known,RoomLayout room,float dt)
        {
            var motion=type.motion;
            if(state.mode=="hang")
            {
                actor.vx=actor.vy=0;ActorPose.Select(actor,"idle");
                if(known.HasValue) {state.mode="chase";state.timer=Range(100,200);}
                return;
            }
            Vector2 toTarget=known.HasValue?known.Value+Vector2.up*.7f-Center(actor):Vector2.zero;
            if(!known.HasValue && state.mode!="return") {state.mode="return";state.directionX=Chance(.5f)?.7f:-.7f;state.directionY=.7f;}
            Vector2 wanted;string pose="walk";
            switch(state.mode)
            {
                case "return":
                    wanted=new Vector2(state.directionX,state.directionY)*motion.walkSpeed;break;
                case "hover":
                    wanted=Vector2.zero;
                    if(state.timer<=0) {state.mode="dive";state.timer=20*Frame;state.directionX=toTarget.normalized.x;state.directionY=toTarget.normalized.y;}
                    break;
                case "dive":
                    pose="attack";wanted=new Vector2(state.directionX,state.directionY)*motion.runSpeed*2.5f;
                    if(state.timer<=0) {state.mode="chase";state.timer=Range(100,200);}
                    break;
                default:   // chase
                    float run=toTarget.magnitude<380/50f?motion.runSpeed:motion.walkSpeed;
                    wanted=toTarget.normalized*run+Jitter(motion);
                    if(state.timer<=0)
                    {
                        if(toTarget.magnitude<380/50f && Chance(.5f)) {state.mode="hover";state.timer=20*Frame;}
                        else state.timer=Range(100,200);
                    }
                    break;
            }
            var contact=FlyerMotion.Step(actor,room,wanted,motion.acceleration*(state.mode=="dive"?3:1),dt);
            if(state.mode=="return")
            {
                if(contact.Ceiling) {state.mode="hang";actor.vx=actor.vy=0;}
                else if(contact.BlockedX) state.directionX=-state.directionX;
                else if(contact.Floor) state.directionY=.7f;
            }
            if(contact.BlockedX && state.mode=="dive") {state.mode="chase";state.timer=Range(100,200);}
            ActorPose.Select(actor,pose);
        }

        private void Bloat(ActorState actor,ActorTypeDefinition type,NpcCreatureState state,Vector2? known,RoomLayout room,float dt)
        {
            var motion=type.motion;
            if(state.timer<=0)
            {
                state.timer=Range(20,70);
                if(known.HasValue)
                {
                    var toTarget=known.Value+Vector2.up*.7f-Center(actor);
                    if(toTarget.magnitude<motion.dashRange && Chance(motion.dashChance)) {state.mode="dash";state.timer=20*Frame;}
                    else state.mode="approach";
                    state.directionX=toTarget.normalized.x;state.directionY=toTarget.normalized.y;
                }
                else
                {
                    if(Chance(.1f)) {state.directionX=Chance(.5f)?.7f:-.7f;state.directionY=Chance(.5f)?.7f:-.7f;}
                    state.mode="drift";
                }
            }
            float speed=state.mode=="dash"?motion.runSpeed:state.mode=="approach"?motion.walkSpeed:motion.walkSpeed*.5f;
            var contact=FlyerMotion.Step(actor,room,new Vector2(state.directionX,state.directionY)*speed+Jitter(motion),motion.acceleration,dt);
            if(contact.BlockedX) state.directionX=-state.directionX;
            if(contact.BlockedY) state.directionY=-state.directionY;
            ActorPose.Select(actor,"walk");
        }

        private void Slime(ActorState actor,NpcCreatureState state,RoomLayout room,MovementController ground,float dt)
        {
            var hull=Anatomy.Movement(actor);
            if(state.mode=="hang")
            {
                actor.vx=actor.vy=0;ActorPose.Select(actor,"walk");
                if(ActorTerrainMotion.IsClear(room,hull,actor.Position+Vector2.up*1f)) {state.mode="crawl";state.inverted=false;}
                return;
            }
            if(state.directionX==0) state.directionX=actor.facing;
            float direction=Mathf.Sign(state.directionX);
            // UnitSlime: it turns at walls (shX) and does not walk off ledges (Unit stay/turn).
            var ahead=actor.Position+Vector2.right*(direction*(hull.width*.5f+.1f));
            bool supported=ActorTerrainMotion.Supported(room,hull,actor.Position,false);
            if(supported && !ActorTerrainMotion.Supported(room,hull,ahead,false)) direction=-direction;
            float before=actor.x;
            ground.Step(actor,new Vector2(direction,0),false,dt);
            if(supported && Mathf.Abs(actor.x-before)<.0005f) direction=-direction;
            state.directionX=direction;
            ActorPose.Select(actor,"walk");
        }

        private Vector2 Jitter(CreatureMotionSettings motion)=>new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*motion.walkSpeed*.3f;
        private static Vector2 Center(ActorState actor)=>actor.Position+Vector2.up*(Anatomy.Movement(actor).height*.5f);
    }
}
