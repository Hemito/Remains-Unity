using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // A client's report that an attack it simulates touched a host-owned world object (a lamp, a trap or a charge).
    [Serializable] public class AttackContactRequest
    {
        public string world,decisionActor;
        public int decisionGeneration,decisionLifecycle,sourceGeneration,sourceLifecycle,projectileGeneration;
        public AttackEvent attack;
    }

    // One check of such a report, shared by lamps and hazards: the reporting player is current, and the attack is the
    // current owner's — an accepted melee or instant segment (the system's ledger) or that owner's projectile flight.
    // The object lookup and the contact geometry stay with each system.
    public static class AttackContactRules
    {
        public static string Validate(WorldState world,ActorState participant,AttackContactRequest request,DoorDamageAttackLedger ledger,string what,
            Func<string,WeaponDefinition> weapons,out WeaponDefinition weapon)
        {
            weapon=null;
            if(world==null || request==null || world.worldId!=request.world) return $"Контакт {what} относится к другому миру.";
            if(participant==null || !participant.connected || !participant.player || participant.id!=request.decisionActor ||
                participant.generation!=request.decisionGeneration || participant.lifecycleSequence!=request.decisionLifecycle)
                return $"Не совпало подключение или поколение отправителя контакта {what}.";
            var attack=request.attack;
            if(attack==null || string.IsNullOrWhiteSpace(attack.id) || attack.level!=participant.level ||
                !Finite(attack.Origin) || !Finite(attack.End) || !float.IsFinite(attack.radius) || attack.radius<0 ||
                !float.IsFinite(attack.speed) || attack.speed<0) return $"Некорректный сегмент атаки {what}.";
            if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.IsExplosive(attack.weapon)) return $"Эта атака не поддерживает контакт {what}.";
            weapon=(weapons??WeaponCatalog.Get)(attack.weapon);
            if(!float.IsFinite(weapon.damage) || weapon.damage<=0) return $"Атака не наносит урон: {what}.";
            if(weapon.projectileSpeed>0)
            {
                var flight=world.projectiles.Find(value=>value.launch?.id==attack.id);
                if(flight==null || flight.owner!=participant.owner || flight.generation!=request.projectileGeneration ||
                    !DoorDamageRequestRules.SameLaunch(flight.launch,attack) || !DoorDamageRequestRules.OnFlight(flight,attack))
                    return $"Контакт {what} не принадлежит текущему владельцу и траектории снаряда.";
            }
            else
            {
                var source=world.Find(attack.attacker);
                if(source==null || source.down || source.owner!=participant.owner || source.owner!=attack.sourceOwner ||
                    source.generation!=request.sourceGeneration || source.lifecycleSequence!=request.sourceLifecycle ||
                    request.projectileGeneration!=0 || !ledger.Contains(world.worldId,source,attack))
                    return $"Не подтверждён сегмент атаки {what} текущего владельца.";
            }
            return null;
        }
        private static bool Finite(Vector2 p)=>float.IsFinite(p.x) && float.IsFinite(p.y);
    }
}
