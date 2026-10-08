using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    [Serializable] public readonly struct FactoryRouteMotionWitness
    {
        public readonly string kind,phase,pose;
        public readonly float x,y,vx,vy,bodyX,bodyY,surfaceX,surfaceY,surfaceWidth,surfaceHeight;
        public readonly bool climbing,supported,bodyBound,bodyContact;
        public readonly int slopeDirection;
        internal FactoryRouteMotionWitness(string observation,string stage,ActorState actor,bool onSupport,RoomSurface surface)
        {
            kind=observation;phase=stage;pose=actor.pose;x=actor.x;y=actor.y;vx=actor.vx;vy=actor.vy;
            climbing=actor.climbing;supported=onSupport;
            var body=ActorBodySupport.Sample(actor);
            bodyX=body.BodyPosition.x;bodyY=body.BodyPosition.y;bodyBound=body.IsBound;bodyContact=body.HasContact;
            surfaceX=surface?.x??0;surfaceY=surface?.y??0;surfaceWidth=surface?.width??0;surfaceHeight=surface?.height??0;
            slopeDirection=surface?.slopeDirection??0;
        }
    }

    // Opt-in intent sequence over the installed level. The caller owns identity,
    // pause, combat, destruction and shared UI barriers; this class never moves a body.
    public sealed class FactoryContinuationRouteMotion
    {
        public enum Phase { StairsApproach,Stairs,Seam,RightGapApproach,RightGap,DestructionApproach,Destruction,
            L3Approach,L3Up,CupApproach,Cup,L3Return,L3Down,LeftGapApproach,LeftGap,ReturnSeam,ReturnStairs,WarehouseReturn,Complete }
        public const string SeptumId="factory:0:original:plant:27:uvp4NyXI7hKM1Ynw";
        private const float BottomX=125.38f,LadderX=173.02f,DestructionX=163.2f,CupX=169.2f;
        private readonly RoomLayout room;
        private readonly FactoryRouteMovement movement;
        private readonly RoomSurface[] slopes=new RoomSurface[4];
        private readonly List<FactoryRouteMotionWitness> witnesses=new();
        private readonly IReadOnlyList<FactoryRouteMotionWitness> witnessView;
        private ActorState observedActor;
        private bool jumped,rightRising,leftRising;
        private int ascendingSlopes,descendingSlopes;
        private int recoverySlope=-1,recoveryAttempts;
        private bool recoveryUp,recoveryRetreat;
        private float recoverySeconds,lastStepSeconds;
        public Phase Current {get;private set;}
        public bool Critical=>Current is Phase.Stairs or Phase.ReturnStairs or Phase.RightGapApproach or Phase.RightGap or
            Phase.LeftGapApproach or Phase.LeftGap or Phase.L3Approach or Phase.L3Up or Phase.L3Return or Phase.L3Down or Phase.Destruction or Phase.Cup;
        public bool AtDestruction=>AtDestructionFor(observedActor);
        public bool AtDestructionFor(ActorState actor)=>Current==Phase.Destruction && movement.AtRest(actor,DestructionX,4);
        public bool AtCup=>Current==Phase.Cup && movement.AtRest(observedActor,CupX,7.2f);
        public bool Complete=>Current==Phase.Complete;
        public bool MiddleClimbUp {get;private set;}
        public bool MiddleClimbDown {get;private set;}
        public bool RightAirborne {get;private set;}
        public bool RightLanding {get;private set;}
        public bool LeftAirborne {get;private set;}
        public bool LeftLanding {get;private set;}
        public bool AscendingSlopes=>ascendingSlopes==15;
        public bool DescendingSlopes=>descendingSlopes==15;
        public bool OutwardSeam {get;private set;}
        public bool ReturnSeam {get;private set;}
        public IReadOnlyList<FactoryRouteMotionWitness> Witnesses=>witnessView;

        public FactoryContinuationRouteMotion(RoomLayout geometry,ActorState actor)
        {
            room=geometry??throw new ArgumentNullException(nameof(geometry));
            if(room.platforms==null || room.ladders==null)
                throw new InvalidOperationException("Маршрут комнаты 27: отсутствуют установленные поверхности или лестницы.");
            movement=new FactoryRouteMovement(room,"Маршрут комнаты 27");witnessView=witnesses.AsReadOnly();
            ObserveActor(actor);movement.RequireLadder(BottomX,.8f,4);movement.RequireLadder(LadderX,4,7.2f);
            for(int i=0;i<slopes.Length;i++) slopes[i]=RequireSurface(139.2f+.8f*i,.8f+.8f*i,.8f,.8f,true,1);
            RequireSurface(153.6f,3.2f,7.2f,.8f,false,0);
            RequireSurface(162.4f,3.2f,14.4f,.8f,false,0);
            RequireSurface(172.8f,6.4f,.8f,.8f,true,0);
            foreach(var point in new[]{new Vector2(BottomX,.8f),new Vector2(130.6f,.8f),new Vector2(138.2f,.8f),
                new Vector2(142.8f,4),new Vector2(154.8f,4),new Vector2(159.9f,4),new Vector2(DestructionX,4),
                new Vector2(166.2f,4),new Vector2(LadderX,4),new Vector2(LadderX,7.2f),new Vector2(CupX,7.2f)})
                movement.RequireSupportedClear(actor,point);
            if(!movement.AtRest(actor,BottomX,.8f))
                throw new InvalidOperationException($"Маршрут комнаты 27 требует фактический покой внизу L2 склада: actor={actor.id}, position={actor.Position}, velocity={actor.Velocity}, climbing={actor.climbing}.");
            Record("warehouse-bottom",actor);Current=Phase.StairsApproach;
        }
        public PlayerCommand Command(ActorState actor)
        {
            ObserveActor(actor);
            switch(Current)
            {
                case Phase.StairsApproach:case Phase.Seam:case Phase.RightGapApproach:
                    return new PlayerCommand {movement=Vector2.right};
                case Phase.Stairs:return StairCommand(actor,true);
                case Phase.RightGap:case Phase.LeftGap:
                    bool edge=!jumped;jumped=true;
                    return new PlayerCommand {movement=Current==Phase.RightGap?Vector2.right:Vector2.left,jump=edge,jumpHeld=false};
                case Phase.DestructionApproach:case Phase.Destruction:return FactoryRouteMovement.Move(actor,DestructionX);
                case Phase.L3Approach:case Phase.L3Return:return FactoryRouteMovement.Move(actor,LadderX);
                case Phase.L3Up:return new PlayerCommand {movement=Vector2.up};
                case Phase.L3Down:return new PlayerCommand {movement=Vector2.down};
                case Phase.CupApproach:case Phase.Cup:return FactoryRouteMovement.Move(actor,CupX);
                case Phase.LeftGapApproach:case Phase.ReturnSeam:
                    return new PlayerCommand {movement=Vector2.left};
                case Phase.ReturnStairs:return StairCommand(actor,false);
                case Phase.WarehouseReturn:return FactoryRouteMovement.Move(actor,BottomX);
                default:return default;
            }
        }
        public void Tick(ActorState actor,float stepSeconds=0)
        {
            if(!float.IsFinite(stepSeconds) || stepSeconds<0)
                throw new InvalidOperationException($"Маршрут комнаты 27: неверный шаг наблюдения dt={stepSeconds}, actor={actor?.id}.");
            lastStepSeconds=stepSeconds;
            ObserveActor(actor);
            switch(Current)
            {
                case Phase.StairsApproach:
                    if(actor.x>=138.2f && movement.Landed(actor,.8f)) Current=Phase.Stairs;
                    break;
                case Phase.Stairs:
                    ObserveSlopes(actor,true);
                    ObserveStairRecovery(actor,true,stepSeconds);
                    if(actor.x>=142.8f && movement.Landed(actor,4))
                    {Require(AscendingSlopes,actor,"этаж 4 достигнут без наблюдения всех четырёх наклонных опор");Record("stairs-top",actor);Current=Phase.Seam;}
                    break;
                case Phase.Seam:
                    if(actor.x>=154.8f && movement.Landed(actor,4))
                    {OutwardSeam=true;Record("seam-out",actor);Current=Phase.RightGapApproach;}
                    break;
                case Phase.RightGapApproach:
                    if(actor.x>=159.9f && movement.Landed(actor,4)) {jumped=false;Current=Phase.RightGap;}
                    break;
                case Phase.RightGap:
                    ObserveJump(actor,true);
                    if(jumped && actor.x>=162.6f && movement.Landed(actor,4))
                    {Require(RightAirborne && rightRising,actor,"правый берег достигнут без наблюдаемого прыжка");RightLanding=true;Record("right-landing",actor);Current=Phase.DestructionApproach;}
                    break;
                case Phase.DestructionApproach:
                    if(movement.AtRest(actor,DestructionX,4)) {Record("before-septum",actor);Current=Phase.Destruction;}
                    break;
                case Phase.L3Approach:
                    if(movement.AtLadderEntry(actor,LadderX,4)) Current=Phase.L3Up;
                    break;
                case Phase.L3Up:
                    if(!MiddleClimbUp && FactoryRouteMovement.MiddleClimb(actor,LadderX,4,7.2f,true))
                    {MiddleClimbUp=true;Record("middle-climb-up",actor);}
                    if(movement.At(actor,LadderX,7.2f))
                    {movement.RequireClimb(MiddleClimbUp,actor,Current.ToString());Record("ladder-top",actor);Current=Phase.CupApproach;}
                    break;
                case Phase.CupApproach:
                    if(movement.AtRest(actor,CupX,7.2f)) {Record("cup-ready",actor);Current=Phase.Cup;}
                    break;
                case Phase.L3Return:
                    if(movement.AtLadderEntry(actor,LadderX,7.2f)) Current=Phase.L3Down;
                    break;
                case Phase.L3Down:
                    if(!MiddleClimbDown && FactoryRouteMovement.MiddleClimb(actor,LadderX,4,7.2f,false))
                    {MiddleClimbDown=true;Record("middle-climb-down",actor);}
                    if(movement.At(actor,LadderX,4))
                    {movement.RequireClimb(MiddleClimbDown,actor,Current.ToString());Record("ladder-bottom",actor);Current=Phase.LeftGapApproach;}
                    break;
                case Phase.LeftGapApproach:
                    if(actor.x<=163.1f && movement.Landed(actor,4)) {jumped=false;Current=Phase.LeftGap;}
                    break;
                case Phase.LeftGap:
                    ObserveJump(actor,false);
                    if(jumped && actor.x<=160.5f && movement.Landed(actor,4))
                    {Require(LeftAirborne && leftRising,actor,"левый берег достигнут без наблюдаемого прыжка");LeftLanding=true;Record("left-landing",actor);Current=Phase.ReturnSeam;}
                    break;
                case Phase.ReturnSeam:
                    if(actor.x<=152.4f && movement.Landed(actor,4))
                    {ReturnSeam=true;Record("seam-back",actor);Current=Phase.ReturnStairs;}
                    break;
                case Phase.ReturnStairs:
                    ObserveSlopes(actor,false);
                    ObserveStairRecovery(actor,false,stepSeconds);
                    if(actor.x<=138.2f && movement.Landed(actor,.8f))
                    {Require(DescendingSlopes,actor,"нижний этаж достигнут без наблюдения всех четырёх наклонных опор");Record("stairs-bottom",actor);Current=Phase.WarehouseReturn;}
                    break;
                case Phase.WarehouseReturn:
                    if(movement.AtRest(actor,BottomX,.8f)) {Record("warehouse-return",actor);Current=Phase.Complete;}
                    break;
            }
        }
        public void ContinueAfterDestruction(ActorState actor)
        {
            ObserveActor(actor);
            Require(AtDestruction,actor,"продолжение запрошено вне фактического покоя перед перегородкой");
            foreach(var surface in RoomSurfaces.All(room))
                if(surface.sourceDoorId==SeptumId)
                    throw new InvalidOperationException($"Маршрут комнаты 27: перегородка ещё блокирует путь: id={SeptumId}, bounds=({surface.x},{surface.y},{surface.width},{surface.height}).");
            // The caller proves actual destruction/receipt. Absence from composed
            // movement geometry is an additional requirement, not a destruction receipt.
            movement.RequireSupportedClear(actor,new Vector2(166.2f,4));
            Record("after-septum",actor);Current=Phase.L3Approach;
        }
        public void BeginReturn(ActorState actor)
        {
            ObserveActor(actor);Require(AtCup,actor,"возврат запрошен до фактического покоя у cup28");
            Record("cup-return",actor);Current=Phase.L3Return;
        }
        private RoomSurface RequireSurface(float x,float y,float width,float height,bool oneWay,int slope)
        {
            RoomSurface selected=null;int count=0;
            foreach(var surface in room.platforms)
                if(Mathf.Abs(surface.x-x)<.001f && Mathf.Abs(surface.y-y)<.001f && Mathf.Abs(surface.width-width)<.001f &&
                    Mathf.Abs(surface.height-height)<.001f && surface.oneWay==oneWay && surface.slopeDirection==slope)
                {selected=surface;count++;}
            if(count!=1) throw new InvalidOperationException($"Маршрут комнаты 27: исходная опора не однозначна: room={room.id}, rect=({x},{y},{width},{height}), oneWay={oneWay}, slope={slope}, count={count}.");
            return selected;
        }
        private void ObserveSlopes(ActorState actor,bool up)
        {
            if(actor.climbing || Mathf.Abs(actor.vy)>=.001f || (up?actor.vx<=0:actor.vx>=0)) return;
            var hull=Anatomy.Movement(actor);
            for(int i=0;i<slopes.Length;i++)
            {
                int bit=1<<i;if(((up?ascendingSlopes:descendingSlopes)&bit)!=0) continue;
                var surface=slopes[i];
                if(actor.x<surface.x || actor.x>=surface.x+surface.width || actor.y<=surface.y+RoomSupport.ContactTolerance ||
                    Mathf.Abs(actor.y-RoomSupport.Top(surface,actor.x,hull.width))>RoomSupport.ContactTolerance ||
                    !ActorTerrainMotion.Supported(room,hull,actor.Position)) continue;
                if(up) ascendingSlopes|=bit;else descendingSlopes|=bit;
                Record((up?"slope-up-":"slope-down-")+i,actor,surface);
            }
        }
        private PlayerCommand StairCommand(ActorState actor,bool up)
        {
            int observed=up?ascendingSlopes:descendingSlopes;
            // Pause by ordinary direction/release inputs at each short support.
            // A long rendered frame can skip its moving observation window;
            // this keeps the actual support guard, not an interpolated witness.
            for(int step=0;step<slopes.Length;step++)
            {
                int index=up?step:slopes.Length-1-step;
                if((observed&(1<<index))!=0) continue;
                if(recoverySlope>=0 && (recoverySlope!=index || recoveryUp!=up)) ResetStairRecovery();
                if(recoverySlope==index)
                {
                    if(!recoveryRetreat) return StairDirection(up);
                    // Re-enter the moving window from its approach side. A long
                    // frame may have crossed it without a single valid observation.
                    float approach=up?slopes[index].x-.2f:slopes[index].x+slopes[index].width+.2f;
                    var back=FactoryRouteMovement.Move(actor,approach);
                    if(back.movement.x==0 && StairRest(actor)) BeginStairNudge(actor,index,up);
                    return recoveryRetreat?back:StairDirection(up);
                }
                var command=FactoryRouteMovement.Move(actor,slopes[index].x+.15f);
                if(command.movement.x==0 && StairRest(actor))
                {
                    // At the old braked waypoint there may be no moving sample.
                    // A real direction tap supplies one; only ObserveSlopes may
                    // record it after MovementController has actually moved.
                    BeginStairNudge(actor,index,up);
                    return StairDirection(up);
                }
                return command;
            }
            ResetStairRecovery();
            return new PlayerCommand {movement=up?Vector2.right:Vector2.left};
        }
        private bool StairRest(ActorState actor)=>!actor.climbing && Mathf.Abs(actor.vx)<=.1f && Mathf.Abs(actor.vy)<.001f &&
            ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
        private static PlayerCommand StairDirection(bool up)=>new() {movement=up?Vector2.right:Vector2.left};
        private void BeginStairNudge(ActorState actor,int index,bool up)
        {
            if(recoverySlope!=index || recoveryUp!=up) recoveryAttempts=0;
            recoverySlope=index;recoveryUp=up;recoveryRetreat=false;recoverySeconds=0;
            if(++recoveryAttempts>4) FailStairRecovery(actor,"исчерпаны четыре обычных попытки наблюдения опоры");
        }
        private void ObserveStairRecovery(ActorState actor,bool up,float stepSeconds)
        {
            if(recoverySlope<0) return;
            int observed=up?ascendingSlopes:descendingSlopes;
            if((observed&(1<<recoverySlope))!=0) {ResetStairRecovery();return;}
            recoverySeconds+=stepSeconds;
            var surface=slopes[recoverySlope];
            if(!recoveryRetreat && ((up?actor.x>=surface.x+surface.width:actor.x<surface.x) || recoverySeconds>=1))
            {recoveryRetreat=true;recoverySeconds=0;}
            else if(recoveryRetreat && recoverySeconds>=3)
                FailStairRecovery(actor,"обычный возврат перед опорой не достиг покоя за три секунды");
        }
        private void FailStairRecovery(ActorState actor,string reason)
            =>throw new InvalidOperationException($"Маршрут комнаты 27: {reason}; actor={actor.id}, room={room.id}, phase={Current}, slope={recoverySlope}, up={recoveryUp}, retreat={recoveryRetreat}, attempts={recoveryAttempts}, elapsed={recoverySeconds}, dt={lastStepSeconds}, position={actor.Position}, velocity={actor.Velocity}, climbing={actor.climbing}, ascendingMask={ascendingSlopes}, descendingMask={descendingSlopes}.");
        private void ResetStairRecovery()
        {recoverySlope=-1;recoveryAttempts=0;recoveryRetreat=false;recoverySeconds=0;}
        private void ObserveJump(ActorState actor,bool right)
        {
            if(!jumped || actor.climbing || actor.y<=4.0002f || ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position)) return;
            if(right)
            {
                if(actor.vy>0) rightRising=true;
                if(!RightAirborne) {RightAirborne=true;Record("right-airborne",actor);}
            }
            else
            {
                if(actor.vy>0) leftRising=true;
                if(!LeftAirborne) {LeftAirborne=true;Record("left-airborne",actor);}
            }
        }
        private void Record(string kind,ActorState actor,RoomSurface surface=null)
            =>witnesses.Add(new FactoryRouteMotionWitness(kind,Current.ToString(),actor,
                ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),surface));
        private void ObserveActor(ActorState actor)
        {
            if(actor==null || !float.IsFinite(actor.x) || !float.IsFinite(actor.y) || !float.IsFinite(actor.vx) || !float.IsFinite(actor.vy))
                throw new InvalidOperationException($"Маршрут комнаты 27: отсутствует актёр или нечисловое движение: actor={actor?.id}, phase={Current}.");
            observedActor=actor;
        }
        private void Require(bool condition,ActorState actor,string reason)
        {
            if(!condition) throw new InvalidOperationException($"Маршрут комнаты 27: {reason}; actor={actor.id}, phase={Current}, position={actor.Position}, velocity={actor.Velocity}, climbing={actor.climbing}, ascendingMask={ascendingSlopes}, descendingMask={descendingSlopes}.");
        }
    }
}
