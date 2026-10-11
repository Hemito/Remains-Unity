using System.Collections.Generic;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // A short query along the actual walking direction. No hidden-player position lookup.
    public static class NpcDoorIntent
    {
        public static DoorState Select(ActorState actor,Vector2 destination,IReadOnlyList<DoorState> doors,RoomLayout room)
        {
            if(actor==null || actor.player || actor.down || actor.attackBusy || ActorHoldRules.Active(actor) ||
                !ActorTypes.Get(actor.actorType).canUseDoors || Mathf.Abs(destination.x-actor.x)<.1f) return null;
            var hull=Anatomy.Movement(actor);
            var start=actor.Position+Vector2.up*(hull.height*.5f);
            var end=start+Vector2.right*Mathf.Sign(destination.x-actor.x)*(hull.width*.5f+.6f);
            // Movement sees solid surfaces regardless of their bullet permeability.
            float nearest=float.PositiveInfinity;RoomSurface obstacle=null;
            foreach(var surface in RoomSurfaces.Near(room,SurfaceContact.QueryBounds(start,end)))
            {
                if(surface.oneWay) continue;
                var entry=SurfaceContact.Entry(surface,start,end);
                if(entry.HasValue && entry.Value<nearest) {nearest=entry.Value;obstacle=surface;}
            }
            if(obstacle==null) return null;
            foreach(var door in doors)
                if(door.level==actor.level && door.Blocks && door.CanInteract && obstacle.sourceDoorId==door.id &&
                    Mathf.Abs(destination.x-actor.x)>=Mathf.Abs(door.x+door.width*.5f-actor.x)) return door;
            return null;
        }
    }
}
