using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Input-intent helpers only. MovementController owns all displacement.
    internal sealed class FactoryRouteMovement
    {
        private readonly RoomLayout room;
        private readonly string context;
        internal FactoryRouteMovement(RoomLayout geometry,string diagnosticContext)
        {
            room=geometry??throw new ArgumentNullException(nameof(geometry));
            context=diagnosticContext;
        }
        internal bool Landed(ActorState actor,float y)=>Mathf.Abs(actor.y-y)<.0002f && Mathf.Abs(actor.vy)<.001f &&
            !actor.climbing && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
        internal bool At(ActorState actor,float x,float y)=>Mathf.Abs(actor.x-x)<.08f && Landed(actor,y);
        internal bool AtRest(ActorState actor,float x,float y)=>At(actor,x,y) && Mathf.Abs(actor.vx)<.1f;
        internal bool AtLadderEntry(ActorState actor,float x,float y)=>Mathf.Abs(actor.x-x)<=.3f && Landed(actor,y);
        internal void RequireLadder(float x,float bottom,float top)
        {
            int count=0;
            foreach(var ladder in room.ladders)
                if(Mathf.Abs(ladder.x+ladder.movementOffset-x)<.001f &&
                    Mathf.Abs(ladder.bottom-bottom)<.001f && Mathf.Abs(ladder.top-top)<.001f) count++;
            if(count!=1) throw new InvalidOperationException($"{context}: лестница не однозначна: room={room.id}, actorX={x}, bottom={bottom}, top={top}.");
        }
        internal static bool MiddleClimb(ActorState actor,float x,float bottom,float top,bool up)
        {
            float margin=(top-bottom)*.25f;
            return actor.climbing && Mathf.Abs(actor.x-x)<.01f && actor.y>bottom+margin && actor.y<top-margin &&
                (up?actor.vy>0:actor.vy<0);
        }
        internal void RequireClimb(bool observed,ActorState actor,string phase)
        {
            if(!observed) throw new InvalidOperationException($"{context}: площадка достигнута без наблюдаемого лазания; actor={actor.id}, phase={phase}, position={actor.Position}, velocity={actor.Velocity}.");
        }
        internal void RequireSupportedClear(ActorState actor,Vector2 point)
        {
            var hull=Anatomy.Movement(actor);
            if(!ActorTerrainMotion.IsClear(room,hull,point) || !ActorTerrainMotion.Supported(room,hull,point))
                throw new InvalidOperationException($"{context}: нет объявленной свободной опоры: room={room.id}, point={point}.");
        }
        internal static PlayerCommand Move(ActorState actor,float x)
        {
            float distance=x-actor.x;
            // Release, observe actual braking, then ordinary direction taps.
            if(Mathf.Abs(distance)<=.4f && Mathf.Abs(actor.vx)>.1f) return default;
            return new PlayerCommand {movement=Vector2.right*(Mathf.Abs(distance)>.06f?Mathf.Sign(distance):0)};
        }
    }
}
