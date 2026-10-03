using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Networking
{
    public static class GrenadeLaunchRequest
    {
        public static ThrowRequest Capture(ActorState actor,Vector2 aim)
        {
            var selected=WeaponInventory.Selected(actor);
            bool held=selected?.definition=="grenade";
            var stack=held?selected:actor.inventory.Find(item=>item.definition=="grenade" && InventoryAvailability.Count(actor,item)>0);
            var origin=held?WeaponPose.Mount(actor):WeaponPose.Origin(actor);
            var velocity=GrenadeLaunch.Velocity(origin,aim,actor.y,Remains.Content.WeaponCatalog.Get("grenade"));
            return new ThrowRequest {transaction=Guid.NewGuid().ToString("N"),level=actor.level,item=stack?.id,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,
                originX=origin.x,originY=origin.y,aimX=aim.x,aimY=aim.y,vx=velocity.x,vy=velocity.y};
        }
        public static bool Matches(ActorState actor,ThrowRequest request)=>actor!=null && !actor.down &&
            !string.IsNullOrWhiteSpace(request.transaction) && !string.IsNullOrWhiteSpace(request.item) && actor.level==request.level &&
            actor.generation==request.generation && actor.lifecycleSequence==request.lifecycle &&
            float.IsFinite(request.originX) && float.IsFinite(request.originY) &&
            float.IsFinite(request.aimX) && float.IsFinite(request.aimY) &&
            float.IsFinite(request.vx) && float.IsFinite(request.vy) &&
            float.IsFinite(new Vector2(request.vx,request.vy).sqrMagnitude) &&
            float.IsFinite((new Vector2(request.aimX,request.aimY)-new Vector2(request.originX,request.originY)).sqrMagnitude);
    }
}
