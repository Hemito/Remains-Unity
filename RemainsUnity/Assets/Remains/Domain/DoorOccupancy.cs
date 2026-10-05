using System;
using UnityEngine;

namespace Remains.Domain
{
    public static class DoorOccupancy
    {
        // Disconnected bodies and held objects still occupy physical space.
        public static bool IsOccupied(WorldState world,DoorState door)
        {
            door.Validate();
            foreach(var actor in world.actors)
            {
                if(actor.level!=door.level) continue;
                var hull=Anatomy.Movement(actor);
                if(Intersects(door,new Rect(actor.x-hull.width*.5f,actor.y,hull.width,hull.height),actor.id)) return true;
            }
            foreach(var prop in world.props) if(Occupied(prop)) return true;
            foreach(var container in world.containers) if(Occupied(container)) return true;
            return false;
            bool Occupied(PropState prop)=>prop.hasPhysicalBody && prop.level==door.level &&
                Intersects(door,new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height),prop.id);
        }
        private static bool Intersects(DoorState door,Rect bounds,string id)
        {
            if(!float.IsFinite(bounds.xMin) || !float.IsFinite(bounds.yMin) || !float.IsFinite(bounds.xMax) ||
                !float.IsFinite(bounds.yMax) || bounds.width<=0 || bounds.height<=0)
                throw new InvalidOperationException($"Невозможно проверить проём: дверь={door.id}, объект={id}, границы={bounds}.");
            return door.Bounds.Overlaps(bounds);
        }
    }
}
