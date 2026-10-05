using System;
using UnityEngine;

namespace Remains.Domain
{
    public static class NpcAttackRules
    {
        public const float PreparationSeconds=.8f;
        public const float CooldownSeconds=2;
        private static readonly INpcWeaponTimingRule standardTiming=new GuardWeaponTiming();
        public static void Begin(ActorState actor,ActorState target)=>TryBegin(actor,target);
        public static bool TryBegin(ActorState actor,ActorState target,INpcWeaponTimingRule timing=null)
        {
            if(actor.down || actor.attackBusy || actor.attackCooldown>0 || target==null) return false;
            if(Remains.Content.ActorTypes.Get(actor.actorType).naturalAttack!=null) return NpcNaturalAttack.Begin(actor,target);
            var action=NpcWeaponActions.Prepare(actor);if(action==null) return false;
            var weapon=Remains.Content.WeaponCatalog.Get(action.weapon);timing ??=standardTiming;
            float duration=action.kind==NpcWeaponActions.Reload?weapon.reloadSeconds:timing.Preparation(actor,weapon);
            NpcWeaponActions.ValidSeconds(duration,$"actor={actor.id}, kind={action.kind}, weapon={weapon.id}");
            actor.attackId=Guid.NewGuid().ToString("N");actor.attackTarget=target.id;
            actor.npcWeaponAction=action;actor.attackBusy=true;actor.attackRemaining=duration;actor.revision++;return true;
        }
        public static AttackEvent Complete(ActorState actor,ActorState target,RoomLayout room=null)=>Complete(actor,target,room,standardTiming);
        public static AttackEvent Complete(ActorState actor,ActorState target,RoomLayout room,INpcWeaponTimingRule timing)
        {
            if(actor.down) {Cancel(actor);return null;}
            if(!actor.attackBusy || actor.attackRemaining>0) return null;
            var action=actor.npcWeaponAction;
            if(action?.kind==NpcNaturalAttack.Kind) return NpcNaturalAttack.Complete(actor,target,room);
            if(action==null || action.kind!=NpcWeaponActions.Shot && action.kind!=NpcWeaponActions.Reload)
            {
                Debug.LogWarning($"Отменено действие NPC без поддержанной привязки: actor={actor.id}, attack={actor.attackId}, kind={action?.kind}.");
                Cancel(actor);return null;
            }
            if(!NpcWeaponActions.StillSelected(actor,action)) {Cancel(actor);return null;}
            var weapon=Remains.Content.WeaponCatalog.Get(action.weapon);timing ??=standardTiming;
            float cooldown=NpcWeaponActions.ValidSeconds(timing.Recovery(actor,weapon),$"actor={actor.id}, recovery, weapon={weapon.id}");
            AttackEvent result=null;
            if(action.kind==NpcWeaponActions.Reload) WeaponAmmunition.CompleteReload(actor,weapon);
            else if(target!=null && target.id==actor.attackTarget && ActorPresence.IsPresent(target) && target.level==actor.level &&
                (room==null || RoomVisibility.Clear(room,Remains.Gameplay.WeaponPose.RangedAnchor(actor),target.Position+Vector2.up*.55f)))
            {
                var end=target.Position+new Vector2(0,.55f);
                Remains.Gameplay.NpcWeaponAim.Apply(actor,end,room);
                var origin=Remains.Gameplay.WeaponPose.Muzzle(actor);
                if((room==null || !ShotObstacles.FirstContact(room,Remains.Gameplay.WeaponPose.RangedAnchor(actor),origin).HasValue) &&
                    WeaponAmmunition.SpendShot(actor,weapon,out _))
                    result=new AttackEvent {id=actor.attackId,attacker=actor.id,weapon=action.weapon,level=actor.level,
                    originX=origin.x,originY=origin.y,endX=end.x,endY=end.y};
            }
            actor.attackBusy=false;actor.attackRemaining=0;actor.attackCooldown=cooldown;
            actor.attackTarget=null;actor.revision++;return result;
        }
        public static void Cancel(ActorState actor)
        {
            actor.attackBusy=false;actor.attackRemaining=0;actor.attackCooldown=CooldownSeconds;
            actor.attackId=null;actor.attackTarget=null;actor.npcWeaponAction=null;actor.revision++;
        }
        public static void CopyProgress(ActorState source,ActorState destination)
        {
            if(source.attackId!=destination.attackId || source.attackBusy!=destination.attackBusy ||
                !NpcWeaponActions.SameBinding(source.npcWeaponAction,destination.npcWeaponAction)) return;
            destination.attackRemaining=source.attackRemaining;destination.attackCooldown=source.attackCooldown;
        }
    }
}
