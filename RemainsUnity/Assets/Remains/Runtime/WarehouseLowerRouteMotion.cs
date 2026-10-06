using System;
using System.Linq;
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
        private readonly RoomLayout room;
        private bool jumped;
        internal Phase Current {get;private set;}
        internal bool Critical=>Current is Phase.Gap or Phase.OfficeGap or Phase.L1Down or Phase.L2Down or Phase.L2Up or Phase.L1Up;
        internal bool Arrived=>Current==Phase.Arrived;
        internal bool Complete=>Current==Phase.Complete;
        internal bool OfficeReturned=>Current==Phase.OfficeComplete;
        internal WarehouseLowerRouteMotion(RoomLayout geometry,ActorState actor)
        {
            room=geometry??throw new ArgumentNullException(nameof(geometry));
            RequireLadder(122.98f,4,7.2f);RequireLadder(125.38f,.8f,4);
            foreach(var p in new[]{new Vector2(90.62f,7.2f),new Vector2(101.7f,7.2f),new Vector2(104.7f,7.2f),
                new Vector2(117.8f,7.2f),new Vector2(122.98f,7.2f),new Vector2(122.98f,4),new Vector2(125.38f,4),new Vector2(125.38f,.8f)})
                if(!ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),p) ||
                    !ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),p))
                    throw new InvalidOperationException($"Нижний маршрут склада: нет объявленной свободной опоры: room={room.id}, point={p}.");
            if(!At(actor,90.62f,7.2f)) throw new InvalidOperationException("Нижний маршрут склада начинается на верхушке лестницы офиса (90,62;7,2).");
        }
        private void RequireLadder(float x,float bottom,float top)
        {
            if(room.ladders.Count(l=>Mathf.Abs(l.x+l.movementOffset-x)<.001f &&
                Mathf.Abs(l.bottom-bottom)<.001f && Mathf.Abs(l.top-top)<.001f)!=1)
                throw new InvalidOperationException($"Нижний маршрут склада: лестница не однозначна: room={room.id}, actorX={x}, bottom={bottom}, top={top}.");
        }
        internal bool Landed(ActorState actor,float y)=>Mathf.Abs(actor.y-y)<.0002f && Mathf.Abs(actor.vy)<.001f &&
            !actor.climbing && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
        internal bool At(ActorState actor,float x,float y)=>Mathf.Abs(actor.x-x)<.08f && Landed(actor,y);
        private bool AtRest(ActorState actor,float x,float y)=>At(actor,x,y) && Mathf.Abs(actor.vx)<.1f;
        private static PlayerCommand Move(ActorState actor,float x)
        {
            float distance=x-actor.x;
            // Release before the target, observe actual braking, then use short
            // ordinary direction taps. No velocity/position replacement or timer.
            if(Mathf.Abs(distance)<=.4f && Mathf.Abs(actor.vx)>.1f) return default;
            return new PlayerCommand {movement=Vector2.right*(Mathf.Abs(distance)>.06f?Mathf.Sign(distance):0)};
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
                case Phase.L1Approach:if(AtRest(actor,122.98f,7.2f)) Current=Phase.L1Down;break;
                case Phase.L1Down:if(At(actor,122.98f,4)) Current=Phase.L2Approach;break;
                case Phase.L2Approach:if(AtRest(actor,125.38f,4)) Current=Phase.L2Down;break;
                case Phase.L2Down:if(At(actor,125.38f,.8f)) Current=Phase.L2Up;break;
                case Phase.L2Up:if(At(actor,125.38f,4)) Current=Phase.L1Return;break;
                case Phase.L1Return:if(AtRest(actor,122.98f,4)) Current=Phase.L1Up;break;
                case Phase.L1Up:if(At(actor,122.98f,7.2f)) Current=Phase.ReturnEntry;break;
                case Phase.ReturnEntry:if(AtRest(actor,117.8f,7.2f)) Current=Phase.Complete;break;
                case Phase.OfficeGapApproach:if(actor.x<=104.7f && Landed(actor,7.2f)) Current=Phase.OfficeGap;break;
                case Phase.OfficeGap:if(jumped && actor.x<=102.2f && Landed(actor,7.2f)) Current=Phase.OfficeReturn;break;
                case Phase.OfficeReturn:if(AtRest(actor,90.62f,7.2f) && Mathf.Abs(actor.vx)<.001f) Current=Phase.OfficeComplete;break;
            }
        }
    }
}
