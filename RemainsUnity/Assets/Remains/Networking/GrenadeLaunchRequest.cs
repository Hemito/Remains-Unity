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
            var origin=WeaponPose.Origin(actor);
            return new ThrowRequest {transaction=Guid.NewGuid().ToString("N"),level=actor.level,
                generation=actor.generation,lifecycle=actor.lifecycleSequence,
                originX=origin.x,originY=origin.y,aimX=aim.x,aimY=aim.y};
        }
        public static bool Matches(ActorState actor,ThrowRequest request)=>actor!=null && !actor.down &&
            !string.IsNullOrWhiteSpace(request.transaction) && actor.level==request.level &&
            actor.generation==request.generation && actor.lifecycleSequence==request.lifecycle &&
            float.IsFinite(request.originX) && float.IsFinite(request.originY) &&
            float.IsFinite(request.aimX) && float.IsFinite(request.aimY) &&
            float.IsFinite((new Vector2(request.aimX,request.aimY)-new Vector2(request.originX,request.originY)).sqrMagnitude);
    }
}
