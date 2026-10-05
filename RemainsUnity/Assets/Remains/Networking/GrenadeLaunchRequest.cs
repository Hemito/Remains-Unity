using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Networking
{
    public static class GrenadeLaunchRequest
    {
        public static ThrowRequest Capture(ActorState actor,Vector2 aim,RoomLayout room=null)
        {
            var selected=WeaponInventory.Selected(actor);
            bool held=!HeldItemSelection.HasSelection(actor) && selected?.definition=="grenade";
            var stack=held?selected:actor.inventory.Find(item=>item.definition=="grenade" && InventoryAvailability.Count(actor,item)>0);
            HeldLaunch launch;
            if(held) launch=HeldItemPose.Capture(actor,aim);
            else if(!HeldItemPose.TryQuickLaunch(actor,aim,room,out launch)) return default;
            var origin=launch.origin;var velocity=launch.velocity;
            return new ThrowRequest {transaction=Guid.NewGuid().ToString("N"),level=actor.level,item=stack?.id,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,
                originX=origin.x,originY=origin.y,aimX=aim.x,aimY=aim.y,vx=velocity.x,vy=velocity.y,
                angle=launch.angle,mirrored=launch.mirrored,
                angularVelocity=GrenadeLaunch.AngularVelocity(velocity,actor.facing,Remains.Content.WeaponCatalog.Get("grenade"))};
        }
        public static bool Matches(ActorState actor,ThrowRequest request)=>actor!=null && !actor.down &&
            !string.IsNullOrWhiteSpace(request.transaction) && !string.IsNullOrWhiteSpace(request.item) && actor.level==request.level &&
            actor.generation==request.generation && actor.lifecycleSequence==request.lifecycle &&
            float.IsFinite(request.originX) && float.IsFinite(request.originY) &&
            float.IsFinite(request.aimX) && float.IsFinite(request.aimY) &&
            float.IsFinite(request.vx) && float.IsFinite(request.vy) &&
            float.IsFinite(request.angle) && float.IsFinite(request.angularVelocity) &&
            float.IsFinite(new Vector2(request.vx,request.vy).sqrMagnitude) &&
            float.IsFinite((new Vector2(request.aimX,request.aimY)-new Vector2(request.originX,request.originY)).sqrMagnitude);
    }
}
