using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Domain
{
    public interface IDamageRule
    {
        float Calculate(string weapon, float contactSpeed);
    }

    public sealed class PrototypeDamageRule : IDamageRule
    {
        public float Calculate(string weapon, float contactSpeed)
        {
            var definition=WeaponCatalog.Get(weapon);
            return definition.damage+Mathf.Clamp(contactSpeed*definition.speedDamage,0,definition.maxSpeedDamage);
        }
    }

    public sealed class DamageReceiver
    {
        public event System.Action<DamageResult> Applied;
        private readonly IDamageRule rules;
        private readonly IKnockdownRule knockdown;
        public DamageReceiver(IDamageRule rules,IKnockdownRule knockdown=null)
        {this.rules=rules;this.knockdown=knockdown??new ZeroHealthKnockdownRule();}

        public bool Apply(ActorState actor, AttackEvent attack, bool contact)
        {
            if (!AttackScope.Includes(attack,actor) || !contact || actor.health <= 0 || actor.resolvedAttacks.Contains(attack.id)) return false;
            float previousHealth=actor.health;
            float amount=rules.Calculate(attack.weapon,attack.speed);
            if(float.IsNaN(amount) || float.IsInfinity(amount) || amount<0)
                throw new System.InvalidOperationException($"Правило урона вернуло некорректное значение: actor={actor.id}, attack={attack.id}, damage={amount}.");
            float remaining=Mathf.Max(0,previousHealth-amount);
            float lost=previousHealth-remaining;
            float restore=ActorTypes.Get(actor.actorType).restoreHealthAfterHit;
            bool enter=restore<=0 && knockdown.ShouldEnter(new KnockdownContext(previousHealth,remaining,actor.player));
            actor.resolvedAttacks.Add(attack.id);actor.health=restore>0?restore:remaining;
            if(lost>0) actor.damageSequence++;
            actor.down |= enter;
            actor.revision++;
            if (actor.down)
            {
                ActorPose.Select(actor,"down");
                if(!actor.player)
                {
                    actor.target=null;
                    if(actor.attackBusy) NpcAttackRules.Cancel(actor);
                }
            }
            if(lost>0) Applied?.Invoke(new DamageResult {attack=attack.id,actor=actor.id,level=actor.level,
                generation=actor.generation,amount=lost,x=actor.x,y=actor.y});
            return true;
        }
    }

    public static class HitGeometry
    {
        public static float? CircleEntry(Vector2 center,float radius,Vector2 start,Vector2 end)
        {
            var delta=end-start;var offset=start-center;
            float c=offset.sqrMagnitude-radius*radius;
            if(c<=0) return 0;
            float a=delta.sqrMagnitude;
            if(a<=0) return null;
            float b=Vector2.Dot(offset,delta),discriminant=b*b-a*c;
            if(discriminant<0) return null;
            float entry=(-b-Mathf.Sqrt(discriminant))/a;
            return entry>=0 && entry<=1?entry:null;
        }

        // One geometric contract for hitscan, swept melee and regional extensions.
        public static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            var t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude) : 0;
            return Vector2.Distance(point, start + delta * t);
        }

        public static bool Contacts(ActorState target, AttackEvent attack)
        {
            return Anatomy.Contact(target,attack)!=null;
        }
    }
}
