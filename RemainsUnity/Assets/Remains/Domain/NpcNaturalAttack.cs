using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class NaturalAttackDefinition
    {
        [InspectorName("Тип урона (стабильный ID)")] public string damageType;
        [Min(.01f),InspectorName("Скорость сближения, м/с")] public float approachSpeed;
        [Min(.01f),InspectorName("Дистанция начала, м")] public float beginDistance;
        [Min(0),InspectorName("Допустимая разность высоты, м")] public float verticalDistance;
        [Min(0),InspectorName("Подготовка, с")] public float preparationSeconds;
        [Min(0),InspectorName("Восстановление, с")] public float recoverySeconds;
        [Min(0),InspectorName("Урон")] public float damage;
        [InspectorName("Начало контакта X от тела")] public float originX;
        [Min(0),InspectorName("Высота контакта от опоры")] public float originY;
        [Min(.01f),InspectorName("Длина контакта")] public float reach;
        [Min(.01f),InspectorName("Радиус контакта")] public float radius;
        public void Validate(string type)
        {
            try {DamageTypes.Validate(damageType);}
            catch(InvalidOperationException error)
            {throw new InvalidOperationException($"Некорректный тип урона естественной атаки: actorType={type}, damageType={damageType}. "+error.Message,error);}
            foreach(float value in new[]{approachSpeed,beginDistance,verticalDistance,preparationSeconds,recoverySeconds,damage,originX,originY,reach,radius})
                if(!float.IsFinite(value)) throw new InvalidOperationException("Нечисловые параметры естественной атаки: type="+type);
            if(approachSpeed<=0 || beginDistance<=0 || verticalDistance<0 || preparationSeconds<0 || recoverySeconds<0 || damage<0 ||
                originY<0 || reach<=0 || radius<=0)
                throw new InvalidOperationException("Некорректная естественная атака: type="+type);
        }
    }
    public interface INaturalAttackDamageRule {float Calculate(string actorType);}
    public sealed class TypeNaturalAttackDamageRule : INaturalAttackDamageRule
    {public float Calculate(string actorType)=>NpcNaturalAttack.Definition(actorType).damage;}

    // Contact actions share the existing durable attack lifecycle, never an inventory weapon.
    public static class NpcNaturalAttack
    {
        public const string Kind="natural-contact";
        public static bool IsNatural(AttackEvent attack)=>attack?.naturalAttackType!=null;
        // A creature's only attack, or a raider's body hit beside its weapon (ActorTypes keeps the two apart).
        public static NaturalAttackDefinition Definition(string type)
        {
            var definition=ActorTypes.Get(type);var result=definition.naturalAttack??definition.raider?.bodyHit;
            if(result==null) throw new InvalidOperationException("Отсутствуют данные естественной атаки: type="+type);
            return result; // ActorTypes.Register validates content once before simulation.
        }
        // udarUnit: a touch deals 0.8–1.2 of its damage times the attacker's factor (attKorp param2, 1 or 0.5).
        public const float MinVariation=-.6f,MaxVariation=.2f;
        // The float rounding of a roll next to 1 may step past the bound by an ulp; the bound itself is the original's.
        public static float Variation(float factor,System.Random random)=>Mathf.Clamp(factor*(float)(random.NextDouble()*.4+.8)-1,MinVariation,MaxVariation);
        public static Vector2 Origin(ActorState actor)
        {var data=Definition(actor.actorType);return actor.BodyPosition+new Vector2(data.originX*actor.facing,data.originY);}
        public static bool InRange(ActorState actor,ActorState target)
        {
            if(target==null || !ActorPresence.IsPresent(target) || target.level!=actor.level) return false;
            var data=Definition(actor.actorType);
            return Math.Abs(target.x-actor.x)<=data.beginDistance && Math.Abs(target.y-actor.y)<=data.verticalDistance;
        }
        public static float Approach(ActorState actor,Vector2? destination,float walkSpeed)
        {
            if(!destination.HasValue) return 0;
            var data=Definition(actor.actorType);float difference=destination.Value.x-actor.x;
            return Math.Abs(difference)>data.beginDistance*.7f?Math.Sign(difference)*Math.Min(1,data.approachSpeed/walkSpeed):0;
        }
        public static bool Begin(ActorState actor,ActorState target)
        {
            if(target!=null && string.IsNullOrWhiteSpace(target.id))
                throw new InvalidOperationException("Естественная атака получила цель без стабильного ID: actor="+actor.id);
            if(target?.id==actor.id) return false;
            if(actor.down || actor.attackBusy || actor.attackCooldown>0 || !InRange(actor,target)) return false;
            var data=Definition(actor.actorType);
            actor.facing=target.x>=actor.x?1:-1;
            actor.attackId=Guid.NewGuid().ToString("N");actor.attackTarget=target.id;
            actor.npcWeaponAction=new NpcWeaponActionState {kind=Kind,naturalAttackType=actor.actorType};
            actor.attackBusy=true;actor.attackRemaining=data.preparationSeconds;actor.revision++;return true;
        }
        public static AttackEvent Complete(ActorState actor,ActorState target,RoomLayout room)
        {
            if(actor.npcWeaponAction?.naturalAttackType!=actor.actorType)
                throw new InvalidOperationException($"Изменился тип естественной атаки: actor={actor.id}, actionType={actor.npcWeaponAction?.naturalAttackType}, type={actor.actorType}.");
            // A missed target does not extend the shape or teleport the attacker to it.
            var result=target!=null && target.id==actor.attackTarget && ActorPresence.IsPresent(target) && target.level==actor.level
                ?Event(actor.attackId,actor,target,0):null;
            actor.attackBusy=false;actor.attackRemaining=0;actor.attackTarget=null;
            actor.attackCooldown=Definition(actor.actorType).recoverySeconds;actor.revision++;return result;
        }
        // A touch outside the attack lifecycle (a raider's attKorp beside its weapon): the owner finds the target in
        // reach, the victim confirms the contact as for any natural attack.
        public static AttackEvent Touch(ActorState actor,ActorState target,float variation)
        {
            if(target==null || target.id==actor.id || actor.down || !InRange(actor,target)) return null;
            if(!float.IsFinite(variation) || variation<MinVariation || variation>MaxVariation)
                throw new InvalidOperationException($"Некорректное отклонение урона касания: actor={actor.id}, variation={variation}.");
            actor.facing=target.x>=actor.x?1:-1;
            return Event(Guid.NewGuid().ToString("N"),actor,target,variation);
        }
        private static AttackEvent Event(string id,ActorState actor,ActorState target,float variation)
        {
            var data=Definition(actor.actorType);var start=Origin(actor);var end=start+Vector2.right*(data.reach*actor.facing);
            return new AttackEvent {id=id,attacker=actor.id,level=actor.level,weapon="",naturalAttackType=actor.actorType,naturalAttackTarget=target.id,
                naturalAttackVariation=variation,factoryEnemy=FactoryEnemyScaling.Capture(actor.factoryEnemy),
                originX=start.x,originY=start.y,endX=end.x,endY=end.y,radius=data.radius};
        }
        public static void ValidateEvent(AttackEvent attack,ActorState source=null)
        {
            if(source!=null) FactoryEnemyScaling.ValidateCaptured(attack,source);
            var data=Definition(attack.naturalAttackType);
            if(string.IsNullOrWhiteSpace(attack.id) || string.IsNullOrWhiteSpace(attack.attacker) || string.IsNullOrWhiteSpace(attack.level) ||
                string.IsNullOrWhiteSpace(attack.naturalAttackTarget) || attack.naturalAttackTarget==attack.attacker ||
                !string.IsNullOrEmpty(attack.weapon) || !float.IsFinite(attack.originX) || !float.IsFinite(attack.originY) ||
                !float.IsFinite(attack.endX) || !float.IsFinite(attack.endY) || !float.IsFinite(attack.radius) ||
                Math.Abs(attack.radius-data.radius)>.0001f || Math.Abs(attack.endY-attack.originY)>.0001f ||
                Math.Abs(Math.Abs(attack.endX-attack.originX)-data.reach)>.0001f ||
                !float.IsFinite(attack.naturalAttackVariation) || attack.naturalAttackVariation<MinVariation || attack.naturalAttackVariation>MaxVariation ||
                source!=null && (source.player || source.actorType!=attack.naturalAttackType || source.id!=attack.attacker || source.level!=attack.level))
                throw new InvalidOperationException($"Некорректный естественный контакт: attack={attack.id}, actor={attack.attacker}, type={attack.naturalAttackType}, sourceType={source?.actorType}.");
        }
        public static System.Collections.Generic.List<AttackTargetSnapshot> Contacts(WorldState world,AttackEvent attack,RoomLayout room)
        {
            ValidateEvent(attack);
            float limit=ShotObstacles.FirstContact(room,attack.Origin,attack.End,out _,attack.radius)??1;
            var target=world.Find(attack.naturalAttackTarget);
            if(target==null) return new System.Collections.Generic.List<AttackTargetSnapshot>();
            // A targeted bite samples its victim. Nearby creatures and their corpses are not shields.
            return AttackContacts.Capture(new[]{target},attack,world.friendlyFire,true,limit);
        }
    }
}
