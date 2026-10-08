using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Probe intent sequence only. MovementController remains the sole mover.
    internal sealed class WarehouseLowerRouteMotion
    {
        internal enum Phase { GapApproach,Gap,Entry,Arrived,L1Approach,L1Down,L2Approach,L2Down,L2Up,L1Return,L1Up,ReturnEntry,Complete,
            OfficeGapApproach,OfficeGap,OfficeReturn,OfficeComplete }
        private readonly FactoryRouteMovement movement;
        private bool jumped;
        private readonly HashSet<Phase> climbed=new();
        internal Phase Current {get;private set;}
        internal bool Critical=>Current is Phase.Gap or Phase.OfficeGap or Phase.L1Down or Phase.L2Down or Phase.L2Up or Phase.L1Up;
        internal bool Arrived=>Current==Phase.Arrived;
        internal bool Complete=>Current==Phase.Complete;
        internal bool OfficeReturned=>Current==Phase.OfficeComplete;
        internal WarehouseLowerRouteMotion(RoomLayout geometry,ActorState actor)
        {
            movement=new FactoryRouteMovement(geometry,"Нижний маршрут склада");
            movement.RequireLadder(122.98f,4,7.2f);movement.RequireLadder(125.38f,.8f,4);
            foreach(var p in new[]{new Vector2(90.62f,7.2f),new Vector2(101.7f,7.2f),new Vector2(104.7f,7.2f),
                new Vector2(117.8f,7.2f),new Vector2(122.98f,7.2f),new Vector2(122.98f,4),new Vector2(125.38f,4),new Vector2(125.38f,.8f)})
                movement.RequireSupportedClear(actor,p);
            if(!At(actor,90.62f,7.2f)) throw new InvalidOperationException("Нижний маршрут склада начинается на верхушке лестницы офиса (90,62;7,2).");
        }
        internal bool Landed(ActorState actor,float y)=>movement.Landed(actor,y);
        internal bool At(ActorState actor,float x,float y)=>movement.At(actor,x,y);
        private bool AtRest(ActorState actor,float x,float y)=>movement.AtRest(actor,x,y);
        // A normal S/W press catches the ladder within its entry corridor; do not
        // insist on parking on the narrow cap with a second horizontal tap.
        private bool AtLadderEntry(ActorState actor,float x,float y)=>movement.AtLadderEntry(actor,x,y);
        private void ObserveClimb(ActorState actor,float x,float bottom,float top,bool up)
        {
            if(FactoryRouteMovement.MiddleClimb(actor,x,bottom,top,up)) climbed.Add(Current);
        }
        private void RequireClimb(ActorState actor)
        {
            movement.RequireClimb(climbed.Contains(Current),actor,Current.ToString());
        }
        private static PlayerCommand Move(ActorState actor,float x)
        {
            return FactoryRouteMovement.Move(actor,x);
        }
        internal void BeginLoop(ActorState actor)
        {
            if(!Arrived || !AtRest(actor,117.8f,7.2f)) throw new InvalidOperationException("Петля склада требует фактический покой на входной опоре.");
            Current=Phase.L1Approach;
        }
        internal void BeginOfficeReturn(ActorState actor)
        {
            if(!Complete || !AtRest(actor,117.8f,7.2f))
                throw new InvalidOperationException("Обратный маршрут офиса требует подтверждённый покой на входе склада (117,8;7,2).");
            jumped=false;Current=Phase.OfficeGapApproach;
        }
        internal PlayerCommand Command(ActorState actor)
        {
            switch(Current)
            {
                // Continuous approach preserves walking speed at the jump edge.
                case Phase.GapApproach:return new PlayerCommand {movement=Vector2.right};
                case Phase.OfficeGapApproach:return new PlayerCommand {movement=Vector2.left};
                case Phase.Gap:case Phase.OfficeGap:
                    bool edge=!jumped;jumped=true;
                    return new PlayerCommand {movement=Current==Phase.Gap?Vector2.right:Vector2.left,jump=edge,jumpHeld=false};
                case Phase.Entry:case Phase.ReturnEntry:return Move(actor,117.8f);
                case Phase.L1Approach:case Phase.L1Return:return Move(actor,122.98f);
                case Phase.L2Approach:return Move(actor,125.38f);
                case Phase.L1Down:case Phase.L2Down:return new PlayerCommand {movement=Vector2.down};
                case Phase.L1Up:case Phase.L2Up:return new PlayerCommand {movement=Vector2.up};
                case Phase.OfficeReturn:return Move(actor,90.62f);
                default:return default;
            }
        }
        internal void Tick(ActorState actor)
        {
            switch(Current)
            {
                case Phase.GapApproach:if(actor.x>=101.7f && Landed(actor,7.2f)) Current=Phase.Gap;break;
                case Phase.Gap:if(jumped && actor.x>=104.5f && Landed(actor,7.2f)) Current=Phase.Entry;break;
                case Phase.Entry:if(AtRest(actor,117.8f,7.2f)) Current=Phase.Arrived;break;
                case Phase.L1Approach:if(AtLadderEntry(actor,122.98f,7.2f)) Current=Phase.L1Down;break;
                case Phase.L1Down:
                    ObserveClimb(actor,122.98f,4,7.2f,false);
                    if(At(actor,122.98f,4)) {RequireClimb(actor);Current=Phase.L2Approach;}break;
                case Phase.L2Approach:if(AtLadderEntry(actor,125.38f,4)) Current=Phase.L2Down;break;
                case Phase.L2Down:
                    ObserveClimb(actor,125.38f,.8f,4,false);
                    if(At(actor,125.38f,.8f)) {RequireClimb(actor);Current=Phase.L2Up;}break;
                case Phase.L2Up:
                    ObserveClimb(actor,125.38f,.8f,4,true);
                    if(At(actor,125.38f,4)) {RequireClimb(actor);Current=Phase.L1Return;}break;
                case Phase.L1Return:if(AtLadderEntry(actor,122.98f,4)) Current=Phase.L1Up;break;
                case Phase.L1Up:
                    ObserveClimb(actor,122.98f,4,7.2f,true);
                    if(At(actor,122.98f,7.2f)) {RequireClimb(actor);Current=Phase.ReturnEntry;}break;
                case Phase.ReturnEntry:if(AtRest(actor,117.8f,7.2f)) Current=Phase.Complete;break;
                case Phase.OfficeGapApproach:if(actor.x<=104.7f && Landed(actor,7.2f)) Current=Phase.OfficeGap;break;
                case Phase.OfficeGap:if(jumped && actor.x<=102.2f && Landed(actor,7.2f)) Current=Phase.OfficeReturn;break;
                case Phase.OfficeReturn:if(AtRest(actor,90.62f,7.2f) && Mathf.Abs(actor.vx)<.001f) Current=Phase.OfficeComplete;break;
            }
        }
    }
}
