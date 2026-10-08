using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Conservative probe input selection. Does not replace production hit/FF rules.
    internal static class FactoryRouteShotSafety
    {
        internal static bool Clear(WorldState world,ActorState actor,RoomLayout room,Vector2 aim,string intendedDoor=null)
        {
            var muzzle=WeaponPose.Muzzle(actor);var delta=aim-muzzle;
            if(delta.sqrMagnitude<.0001f || Vector2.Dot(WeaponPose.Direction(actor),delta.normalized)<.9999f) return false;
            bool contact=ShotObstacles.FirstContact(room,muzzle,aim,out var surface).HasValue;
            if(intendedDoor==null?contact:!contact || surface.sourceDoorId!=intendedDoor) return false;
            foreach(var other in world.actors)
                if(other.player && other.id!=actor.id && other.level==actor.level &&
                    SurfaceContact.Entry(InventorySource.BodyBounds(other),muzzle,aim,.2f).HasValue) return false;
            return true;
        }
    }
}
