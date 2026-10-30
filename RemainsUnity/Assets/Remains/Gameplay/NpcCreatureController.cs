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
    //   spritebot (UnitSpriteBot): the exception — it shoots its own gun (robolaser); see SpriteBot.
    //   spider mine (UnitMsp): no attack of its own — it runs to its target and blows itself up (Detonates); see SpiderMine.
    //   roller (UnitRoller): rests, then rolls at its target and touches it with a hop; see Roller.
    public sealed class NpcCreatureController
    {
        private const float Frame=1f/30;
        // UnitSpriteBot: calm (green eye, vision 0.3) until zlo turns it hostile for good (red eye, the pose of that name).
        public const string Calm="calm",Hostile="hostile";
        private const float SpriteBotElasticity=.8f;
        // UnitMsp aiState: 0/1 asleep (pose idle), 2 woken (pose "wake"), 3 running at the target, 4 searching (pose "wake").
        public const string Asleep="sleep",Woken="wake",Running="run",Searching="search";
        // UnitRoller aiState 0 resting, 1 rolling; its picture is charged (the pose of that name) near its target.
        public const string Resting="rest",Rolling="roll",Charged="charged";
        private readonly System.Random random=new();
        private float Range(float minFrames,float maxFrames)=>(minFrames+(float)random.NextDouble()*(maxFrames-minFrames))*Frame;
        private bool Chance(float p)=>random.NextDouble()<p;

        // Returns whether the creature attacks this step if it can: touch families whenever their target is in reach,
        // a spritebot only at its own random moments.
        public bool Step(ActorState actor,ActorTypeDefinition type,ActorState target,Vector2? known,RoomLayout room,MovementController ground,float dt)
        {
            actor.creature??=Initial(actor,type,room);
            var state=actor.creature;
            state.timer=Mathf.Max(0,state.timer-dt);
            switch(type.behavior)
            {
                case NpcCreatureBehaviours.Bat: Bat(actor,type,state,known,room,dt);return true;
                case NpcCreatureBehaviours.Bloat: Bloat(actor,type,state,known,room,dt);return true;
                case NpcCreatureBehaviours.Slime: Slime(actor,state,room,ground,dt);return true;
                case NpcCreatureBehaviours.SpriteBot: return SpriteBot(actor,type,state,known,room,dt);
                case NpcCreatureBehaviours.SpiderMine: SpiderMine(actor,state,known,room,ground,dt);return false;
                case NpcCreatureBehaviours.Roller: return Roller(actor,type,state,known,room,ground,dt);
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
            // UnitRoller.setNull: resting, its first decision within 40–90 frames.
            if(type.behavior==NpcCreatureBehaviours.Roller) return new NpcCreatureState {mode=Resting,timer=Range(40,90),directionX=actor.facing};
            if(type.behavior==NpcCreatureBehaviours.SpiderMine) return new NpcCreatureState {mode=Asleep,cling=Cling(actor,room),directionX=actor.facing};
            // UnitSpriteBot.setNull: calm, the first decision in 5–55 frames.
            if(type.behavior==NpcCreatureBehaviours.SpriteBot) return new NpcCreatureState {mode=Calm,timer=Range(5,55)};
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

        // UnitSpriteBot.control: every decision (aiTCh) sets the velocity at once — with a target at run × 0.7–1.3 for
        // 20–70 frames, straight at it when it is farther than 500 px, else in a random direction; without one at
        // walk × 0.5–1.5 for 30–180 frames in a random direction — and the velocity decays by 0.95 a frame (forces);
        // a wall bounces it back at elast 0.8. Noticing a target (findCel → zlo) makes it hostile for good and brings the
        // next decision within 3 frames; a hostile one with a target fires with a 0.1 chance a frame (the gun's rapid
        // limits the rate). Returns whether it wants to fire this step.
        private bool SpriteBot(ActorState actor,ActorTypeDefinition type,NpcCreatureState state,Vector2? known,RoomLayout room,float dt)
        {
            var motion=type.motion;
            if(known.HasValue && state.mode!=Hostile) {state.mode=Hostile;state.timer=Mathf.Min(state.timer,3*Frame);}
            if(state.timer<=0)
            {
                Vector2 direction;float speed;
                if(known.HasValue)
                {
                    var toTarget=known.Value+Vector2.up*.55f-Center(actor);
                    speed=motion.runSpeed*(.7f+.6f*(float)random.NextDouble());state.timer=Range(20,70);
                    direction=toTarget.magnitude>500/50f?toTarget.normalized:RandomDirection();
                }
                else {speed=motion.walkSpeed*(.5f+(float)random.NextDouble());state.timer=Range(30,180);direction=RandomDirection();}
                actor.vx=direction.x*speed;actor.vy=direction.y*speed;
            }
            float drag=Mathf.Pow(.95f,dt/Frame);
            var velocity=new Vector2(actor.vx*drag,actor.vy*drag);
            actor.vx=velocity.x;actor.vy=velocity.y;
            var contact=FlyerMotion.Step(actor,room,velocity,0,dt);
            if(contact.BlockedX) actor.vx=-velocity.x*SpriteBotElasticity;
            if(contact.BlockedY) actor.vy=-velocity.y*SpriteBotElasticity;
            if(known.HasValue && Mathf.Abs(known.Value.x-actor.x)>.01f) actor.facing=known.Value.x>actor.x?1:-1;
            bool hostile=state.mode==Hostile;
            ActorPose.Select(actor,hostile?Hostile:"idle");
            return hostile && known.HasValue && Chance(1-Mathf.Pow(.9f,dt/Frame));
        }
        // UnitMsp.putLoc: with no floor under it, a mine with solid ground 20 px above its top hangs on that ceiling, else one
        // with a wall beside it clings to that wall; it is moved against the surface it holds and stays fixed there.
        private static int Cling(ActorState actor,RoomLayout room)
        {
            var hull=Anatomy.Movement(actor);
            if(ActorTerrainMotion.Supported(room,hull,actor.Position,false)) return NpcCreatureState.NoCling;
            (int cling,Vector2 toward)[] surfaces={(NpcCreatureState.Ceiling,Vector2.up*(20/50f)),(NpcCreatureState.LeftWall,Vector2.left*(40/50f)),(NpcCreatureState.RightWall,Vector2.right*(40/50f))};
            foreach(var (cling,toward) in surfaces)
            {
                if(ActorTerrainMotion.IsClear(room,hull,actor.Position+toward)) continue;
                actor.Position=ActorTerrainMotion.Move(room,hull,actor.Position,toward,true,bodyTops:false);actor.vx=actor.vy=0;
                return cling;
            }
            return NpcCreatureState.NoCling;
        }
        // UnitMsp.control, times in frames / 30. Asleep it waits (fixed where it clings, else standing); noticing a target
        // wakes it for 30–40 frames, then — the target still known — it lets go of its surface and runs at it; every 15
        // frames it turns to the target when it is more than 40 px aside and, nine times in ten, drops through platforms
        // to a target more than 80 px below or jumps (one in five) to one more than 40 px above; at a wall it jumps,
        // turning back now and then. Losing the target it searches standing for maxSpok × 10 frames, then sleeps.
        // Next to the target it blows up (Detonates). directionY holds its platform drop (throu) between the 15-frame turns.
        private void SpiderMine(ActorState actor,NpcCreatureState state,Vector2? known,RoomLayout room,MovementController ground,float dt)
        {
            switch(state.mode)
            {
                case Asleep:
                    if(state.cling==NpcCreatureState.NoCling) ground.Step(actor,Vector2.zero,false,dt);
                    if(known.HasValue) {state.mode=Woken;state.timer=Range(30,40);}
                    ActorPose.Select(actor,state.mode==Asleep?"idle":Woken);
                    return;
                case Woken:
                    if(state.cling==NpcCreatureState.NoCling) ground.Step(actor,Vector2.zero,false,dt);
                    if(state.timer<=0)
                    {
                        if(known.HasValue) {state.mode=Running;state.cling=NpcCreatureState.NoCling;state.timer=0;Turn(actor,state,known.Value);}
                        else state.mode=Asleep;
                    }
                    ActorPose.Select(actor,state.mode==Asleep?"idle":Woken);
                    return;
                case Searching:
                    ground.Step(actor,Vector2.zero,false,dt);
                    if(known.HasValue) state.mode=Running;
                    else if(state.timer<=0) state.mode=Asleep;
                    ActorPose.Select(actor,state.mode==Asleep?"idle":Woken);
                    if(state.mode!=Running) return;
                    break;
            }
            if(!known.HasValue) {state.mode=Searching;state.timer=30*10*Frame;ActorPose.Select(actor,Woken);return;}
            bool jump=false;
            if(state.timer<=0)
            {
                state.timer=15*Frame;
                float below=actor.y-known.Value.y;
                if(Chance(.9f)) {if(below>80/50f) state.directionY=1;jump=below<-40/50f && Chance(.2f);}
                else state.directionY=0;
                Turn(actor,state,known.Value);
            }
            if(actor.y<=room.bottom+80/50f) state.directionY=0;
            var hull=Anatomy.Movement(actor);
            // Unit turnX at a wall ahead: it jumps, and with a small chance turns back instead.
            if(ActorTerrainMotion.Supported(room,hull,actor.Position,false) && !ActorTerrainMotion.IsClear(room,hull,actor.Position+Vector2.right*(state.directionX*.05f)))
            {
                if(Chance(.03f)) state.directionX=-state.directionX;
                else jump=true;
            }
            ground.Step(actor,new Vector2(state.directionX,0),jump,dt,dropThrough:state.directionY>0);
            ActorPose.Select(actor,ActorTerrainMotion.Supported(room,hull,actor.Position,false)?"walk":"jump");
        }
        private static void Turn(ActorState actor,NpcCreatureState state,Vector2 known)
        {
            float aside=known.x-actor.x;
            if(Mathf.Abs(aside)>40/50f || state.directionX==0) state.directionX=aside>=0?1:-1;
        }
        // UnitRoller.control, times in frames / 30: every 40–90 frames it decides to rest (no target for maxSpok checks
        // of 10 frames: directionY holds that interest in seconds) or to roll. Rolling, every 15 frames it turns to a target
        // more than 100 px aside and, nine times in ten, drops through platforms to one more than 80 px below or hops
        // (0.5–1 of its jump, one in two) to one more than 40 px above; it hops half its jump off a ledge (one in two) and
        // at a wall (turning back now and then). A target within 100 px across and 80 px up or down is touched (attKorp)
        // with a 0.3 hop. Resting it brakes. Its picture charges within 200 px of the target and calms beyond 600 px.
        // Returns whether it touches this step.
        private bool Roller(ActorState actor,ActorTypeDefinition type,NpcCreatureState state,Vector2? known,RoomLayout room,MovementController ground,float dt)
        {
            float before=state.timer+dt;
            state.directionY=known.HasValue?30*10*Frame:Mathf.Max(0,state.directionY-dt);
            if(state.timer<=0) {state.mode=state.directionY>0?Rolling:Resting;state.timer=Range(40,90);}
            var hull=Anatomy.Movement(actor);
            bool grounded=ActorTerrainMotion.Supported(room,hull,actor.Position,false);
            float hop=0;bool drop=false,touch=false;
            if(state.mode==Rolling)
            {
                var aim=known??actor.Position+Vector2.right*state.directionX;
                if(Mathf.FloorToInt(before/(15*Frame))!=Mathf.FloorToInt(state.timer/(15*Frame)))
                {
                    float below=actor.y-aim.y;
                    if(Chance(.9f)) {drop=below>80/50f;if(below<-40/50f && Chance(.5f)) hop=.5f+.5f*(float)random.NextDouble();}
                    if(aim.x-actor.x>100/50f) state.directionX=1;
                    else if(aim.x-actor.x<-100/50f) state.directionX=-1;
                }
                var ahead=actor.Position+Vector2.right*(state.directionX*(hull.width*.5f+.1f));
                if(grounded && !drop && actor.y-aim.y<=40/50f && !ActorTerrainMotion.Supported(room,hull,ahead,false) && Chance(.5f)) hop=Mathf.Max(hop,.5f);
                if(grounded && !ActorTerrainMotion.IsClear(room,hull,actor.Position+Vector2.right*(state.directionX*.05f)))
                {
                    if(Chance(.03f)) state.directionX=-state.directionX;
                    else hop=1;
                }
                if(known.HasValue && Mathf.Abs(known.Value.x-actor.x)<100/50f && Mathf.Abs(known.Value.y-actor.y)<80/50f) {touch=true;hop=Mathf.Max(hop,.3f);}
            }
            // Unit.jump(k): from the ground the vertical speed is k of its jump.
            if(hop>0 && grounded) actor.vy=type.motion.jumpSpeed*hop;
            ground.Step(actor,new Vector2(state.mode==Rolling?state.directionX:0,0),false,dt,dropThrough:drop);
            float distance=known.HasValue?Vector2.Distance(known.Value,actor.Position):float.MaxValue;
            bool charged=state.mode!=Resting && (distance<200/50f || actor.pose==Charged && distance<=600/50f);
            ActorPose.Select(actor,charged?Charged:"idle");
            return touch;
        }

        // UnitMsp.control: a running mine whose target's centre is within 100 px of its feet dies — its death is the blast.
        public static bool Detonates(ActorState actor,ActorState target)
        {
            if(actor.creature?.mode!=Running || target==null || target.level!=actor.level || !ActorPresence.IsPresent(target)) return false;
            var centre=target.Position+Vector2.up*(Anatomy.Movement(target).height*.5f);
            return (centre-actor.Position).sqrMagnitude<(100/50f)*(100/50f);
        }

        private Vector2 RandomDirection() {float angle=(float)random.NextDouble()*Mathf.PI*2;return new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));}

        private Vector2 Jitter(CreatureMotionSettings motion)=>new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*motion.walkSpeed*.3f;
        private static Vector2 Center(ActorState actor)=>actor.Position+Vector2.up*(Anatomy.Movement(actor).height*.5f);
    }
}
