using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class LampBreakRequestData
    {
        public string world,lamp,decisionActor;
        public int decisionGeneration,decisionLifecycle,sourceGeneration,sourceLifecycle,projectileGeneration;
        public AttackEvent attack;
    }
    public static class LampBreakRules
    {
        public static string Validate(WorldState world,ActorState participant,LampBreakRequestData request,DoorDamageAttackLedger ledger,out LampState lamp,Func<string,WeaponDefinition> weapons=null)
        {
            lamp=null;
            if(world==null||request==null||world.worldId!=request.world) return "Контакт лампы относится к другому миру.";
            if(participant==null||!participant.connected||!participant.player||participant.id!=request.decisionActor||
                participant.generation!=request.decisionGeneration||participant.lifecycleSequence!=request.decisionLifecycle)
                return "Не совпало подключение или поколение отправителя контакта лампы.";
            var attack=request.attack;
            if(attack==null||string.IsNullOrWhiteSpace(attack.id)||attack.level!=participant.level||
                !Finite(attack.Origin)||!Finite(attack.End)||!float.IsFinite(attack.radius)||attack.radius<0||
                !float.IsFinite(attack.speed)||attack.speed<0) return "Некорректный сегмент атаки лампы.";
            lamp=world.lamps.Find(value=>value.id==request.lamp);
            if(lamp==null||lamp.level!=attack.level) return "Не найдена лампа в локации атаки.";
            lamp.Validate();
            if(NpcNaturalAttack.IsNatural(attack)||Remains.Content.WeaponCatalog.IsExplosive(attack.weapon)) return "Эта атака не поддерживает разрушение ламп.";
            var weapon=(weapons??WeaponCatalog.Get)(attack.weapon);
            if(!float.IsFinite(weapon.damage)||weapon.damage<=0) return "Атака не наносит урон лампе.";
            if(weapon.projectileSpeed>0)
            {
                var flight=world.projectiles.Find(value=>value.launch?.id==attack.id);
                if(flight==null||flight.owner!=participant.owner||flight.generation!=request.projectileGeneration||
                    !DoorDamageRequestRules.SameLaunch(flight.launch,attack)||!DoorDamageRequestRules.OnFlight(flight,attack)) return "Контакт лампы не принадлежит текущему владельцу и траектории снаряда.";
            }
            else
            {
                var source=world.Find(attack.attacker);
                if(source==null||source.down||source.owner!=participant.owner||source.owner!=attack.sourceOwner||
                    source.generation!=request.sourceGeneration||source.lifecycleSequence!=request.sourceLifecycle||
                    request.projectileGeneration!=0||!ledger.Contains(world.worldId,source,attack)) return "Не подтверждён сегмент атаки лампы текущего владельца.";
            }
            if(!SurfaceContact.Entry(lamp.Bounds,attack.Origin,attack.End,weapon.projectileSpeed>0?0:attack.radius).HasValue)
                return "Сегмент не касается рисунка лампы.";
            return null;
        }
        private static bool Finite(Vector2 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y);
    }
}
