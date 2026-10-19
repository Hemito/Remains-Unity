using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class DoorDamageRequestData
    {
        public string world,door,decisionActor;
        public int decisionGeneration,decisionLifecycle,sourceGeneration,sourceLifecycle,projectileGeneration;
        public AttackEvent attack;
        public Vector2 contact;
    }
    public static class DoorDamageRequestRules
    {
        // Numerical serialization precision, not a gameplay clearance or damage tolerance.
        private const float Precision=.001f;
        public static DoorChange Apply(WorldState world,ActorState participant,DoorDamageRequestData request,bool paused,
            DoorDamageAttackLedger ledger,IDoorDamageRule rule,Func<string,WeaponDefinition> weapons,out string reason)
        {
            reason=Validate(world,participant,request,ledger,weapons,out var door,out var weapon);
            if(reason!=null) return DoorChange.Rejected;
            // Receipts are durable. A valid repeat cannot damage twice even during pause or after destruction.
            if(door.integrity.resolvedAttacks.Contains(request.attack.id)) return DoorChange.Unchanged;
            if(paused) {reason="Локация на паузе: новый урон закреплённому объекту не принят.";return DoorChange.Rejected;}
            if(!door.Blocks) {reason="Закреплённый объект больше не перекрывает контакт.";return DoorChange.Rejected;}
            if(rule==null) {reason="Не настроено правило урона закреплённым объектам.";return DoorChange.Rejected;}
            try
            {
                var change=DoorDamageOperations.Apply(door,request.attack.id,rule.Damage(door,request.attack,weapon),out reason);
                if(change==DoorChange.Changed) WorldChanges.Mark(world,door);
                return change;
            }
            catch(Exception failure) when(failure is ArgumentException || failure is InvalidOperationException)
            {reason=failure.Message;return DoorChange.Rejected;}
        }
        public static string Validate(WorldState world,ActorState participant,DoorDamageRequestData request,
            DoorDamageAttackLedger ledger,Func<string,WeaponDefinition> weapons,out DoorState door,out WeaponDefinition weapon)
        {
            door=null;weapon=null;
            if(world==null || request==null || request.world!=world.worldId) return "Контакт относится к другому миру.";
            if(participant==null || !participant.player || !participant.connected || string.IsNullOrEmpty(participant.owner) ||
                participant.id!=request.decisionActor || participant.generation!=request.decisionGeneration ||
                participant.lifecycleSequence!=request.decisionLifecycle) return "Отправитель контакта не принадлежит текущему подключению или эпохе.";
            var attack=request.attack;
            if(attack==null || string.IsNullOrWhiteSpace(attack.id) || string.IsNullOrWhiteSpace(attack.attacker) ||
                string.IsNullOrWhiteSpace(attack.weapon) || !Finite(attack.Origin) || !Finite(attack.End) ||
                !Finite(request.contact) || !float.IsFinite(attack.speed) || attack.speed<0 || !float.IsFinite(attack.radius) || attack.radius<0)
                return "Некорректный сегмент контакта закреплённого объекта.";
            door=world.doors.Find(value=>value.id==request.door);
            if(door==null || door.integrity==null) return "Закреплённый объект не поддерживает разрушаемую целостность.";
            try {door.Validate();}
            catch(InvalidOperationException failure) {return "Некорректные данные закреплённого объекта: "+failure.Message;}
            if(door.level!=attack.level || participant.level!=attack.level) return "Контакт и отправитель находятся в разных локациях.";
            if(NpcNaturalAttack.IsNatural(attack) || Remains.Content.WeaponCatalog.IsExplosive(attack.weapon)) return "Урон закреплённым объектам этой атакой пока не поддержан.";
            try {weapon=weapons?.Invoke(attack.weapon);}
            catch(Exception failure) when(failure is ArgumentException || failure is InvalidOperationException)
            {return "Не удалось определить оружие контакта: "+failure.Message;}
            if(weapon==null || weapon.id!=attack.weapon) return "Не найдено определение оружия контакта.";
            if(weapon.projectileSpeed>0)
            {
                var flight=world.projectiles.Find(value=>value.launch?.id==attack.id);
                if(flight==null || flight.owner!=participant.owner || flight.generation!=request.projectileGeneration)
                    return "Контакт снаряда сообщил прежний владелец или поколение.";
                if(!SameLaunch(flight.launch,attack) || !OnFlight(flight,attack)) return "Сегмент не принадлежит принятой траектории снаряда.";
            }
            else
            {
                var source=world.Find(attack.attacker);
                if(source==null || source.owner!=participant.owner || source.owner!=attack.sourceOwner || source.level!=attack.level ||
                    source.generation!=request.sourceGeneration || source.lifecycleSequence!=request.sourceLifecycle || source.down ||
                    request.projectileGeneration!=0 || ledger==null || !ledger.Contains(world.worldId,source,attack))
                    return "Контакт не принадлежит принятому сегменту текущего владельца атаки.";
            }
            var entry=SurfaceContact.Entry(door.Bounds,attack.Origin,attack.End,weapon.projectileSpeed>0?0:attack.radius);
            if(!entry.HasValue) return "Сегмент не касается физической области закреплённого объекта.";
            var point=Vector2.Lerp(attack.Origin,attack.End,entry.Value);
            point=new Vector2(Mathf.Clamp(point.x,door.Bounds.xMin,door.Bounds.xMax),Mathf.Clamp(point.y,door.Bounds.yMin,door.Bounds.yMax));
            if(Vector2.Distance(point,request.contact)>Precision) return "Заявленная точка не совпала с контактом физической области.";
            return null;
        }
        private static bool Finite(Vector2 point)=>float.IsFinite(point.x) && float.IsFinite(point.y);
        internal static bool SameLaunch(AttackEvent launch,AttackEvent segment)=>launch.id==segment.id && launch.attacker==segment.attacker &&
            launch.weapon==segment.weapon && launch.level==segment.level && launch.sourceOwner==segment.sourceOwner &&
            launch.sourcePlayer==segment.sourcePlayer && launch.friendlyFireDecision==segment.friendlyFireDecision &&
            FactoryEnemyScaling.Same(launch.factoryEnemy,segment.factoryEnemy);
        internal static bool OnFlight(ProjectileState flight,AttackEvent segment)
        {
            if(segment.speed!=flight.flightSpeed || segment.radius!=flight.launch.radius) return false;
            var delta=flight.launch.End-flight.launch.Origin;float range=delta.magnitude;
            if(range<=0) return false;
            var direction=delta/range;
            var start=segment.Origin-flight.launch.Origin;var end=segment.End-flight.launch.Origin;
            float from=Vector2.Dot(start,direction),to=Vector2.Dot(end,direction);
            return from>=-Precision && to>=from && to<=range+Precision &&
                (start-direction*from).magnitude<=Precision && (end-direction*to).magnitude<=Precision;
        }
    }
}
