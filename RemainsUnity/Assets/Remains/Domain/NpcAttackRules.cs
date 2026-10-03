using System;
using UnityEngine;

namespace Remains.Domain
{
    public static class NpcAttackRules
    {
        public const float PreparationSeconds=.8f;
        public const float CooldownSeconds=2;
        public static void Begin(ActorState actor,ActorState target)
        {
            actor.attackId=Guid.NewGuid().ToString("N");actor.attackTarget=target.id;
            actor.attackBusy=true;actor.attackRemaining=PreparationSeconds;actor.revision++;
        }
        public static AttackEvent Complete(ActorState actor,ActorState target,RoomLayout room=null)
        {
            AttackEvent result=null;
            if(target!=null && target.id==actor.attackTarget && ActorPresence.IsPresent(target) && target.level==actor.level)
            {
                var end=target.Position+new Vector2(0,.55f);
                Remains.Gameplay.WeaponPose.Aim(actor,end,room);
                var origin=Remains.Gameplay.WeaponPose.Muzzle(actor);
                if(room==null || !ShotObstacles.FirstContact(room,Remains.Gameplay.WeaponPose.RangedAnchor(actor),origin).HasValue)
                    result=new AttackEvent {id=actor.attackId,attacker=actor.id,weapon="npc-shot",level=actor.level,
                    originX=origin.x,originY=origin.y,endX=end.x,endY=end.y};
            }
            actor.attackBusy=false;actor.attackRemaining=0;actor.attackCooldown=CooldownSeconds;
            actor.attackTarget=null;actor.revision++;return result;
        }
        public static void Cancel(ActorState actor)
        {
            actor.attackBusy=false;actor.attackRemaining=0;actor.attackCooldown=CooldownSeconds;
            actor.attackId=null;actor.attackTarget=null;actor.revision++;
        }
        public static void CopyProgress(ActorState source,ActorState destination)
        {
            if(source.attackId!=destination.attackId || source.attackBusy!=destination.attackBusy) return;
            destination.attackRemaining=source.attackRemaining;destination.attackCooldown=source.attackCooldown;
        }
    }
}
