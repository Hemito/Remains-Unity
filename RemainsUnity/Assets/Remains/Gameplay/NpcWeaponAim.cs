using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public static class NpcWeaponAim
    {
        public static void Apply(ActorState actor,Vector2? knownAimPoint,RoomLayout room=null)
        {
            if(string.IsNullOrEmpty(actor.weapon) || !Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon) return;
            if(knownAimPoint.HasValue && Mathf.Abs(knownAimPoint.Value.x-actor.x)>.01f)
                actor.facing=knownAimPoint.Value.x>=actor.x?1:-1;
            var target=knownAimPoint ?? (WeaponPose.RangedAnchor(actor)+Vector2.right*(actor.facing<0?-10:10));
            WeaponPose.Aim(actor,target,room);
        }
    }
}
