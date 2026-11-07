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
        private readonly IHitProtectionRule protection;
        private readonly INaturalAttackDamageRule natural;
        public DamageReceiver(IDamageRule rules,IKnockdownRule knockdown=null,IHitProtectionRule protection=null)
            :this(rules,knockdown,protection,new TypeNaturalAttackDamageRule()) {}
        public DamageReceiver(IDamageRule rules,IKnockdownRule knockdown,IHitProtectionRule protection,INaturalAttackDamageRule natural)
        {this.rules=rules;this.knockdown=knockdown??new ZeroHealthKnockdownRule();this.protection=protection??new TypeHitProtectionRule();this.natural=natural??throw new System.ArgumentNullException(nameof(natural));}

        public bool Apply(ActorState actor, AttackEvent attack, bool contact)
            =>Apply(actor,attack,contact,null);

        internal bool Apply(ActorState actor,AttackEvent attack,bool contact,float? blastDistance)
        {
            if (!AttackScope.Includes(attack,actor) || !contact || actor.health <= 0 || actor.resolvedAttacks.Contains(attack.id)) return false;
            // Cosmetic receipt keeps the body at contact time, even when applying
            // damage immediately replaces the living pose with knockdown.
            var bodyPosition=actor.BodyPosition;
            float previousHealth=actor.health;
            bool previouslyDown=actor.down;
            bool naturalContact=NpcNaturalAttack.IsNatural(attack);
            if(naturalContact) NpcNaturalAttack.ValidateEvent(attack);
            if(naturalContact && actor.id!=attack.naturalAttackTarget) return false;
            float amount=naturalContact?natural.Calculate(attack.naturalAttackType)*(1+attack.naturalAttackVariation):rules.Calculate(attack.weapon,attack.speed);
            if(Remains.Content.WeaponCatalog.IsExplosive(attack.weapon))
                amount=ExplosionRules.Damage(amount,Remains.Content.WeaponCatalog.Get(attack.weapon).range,
                    blastDistance??Anatomy.BlastDistance(actor,attack.End),attack.id,actor.id);
            amount*=FactoryEnemyScaling.AttackFactor(attack);
            if(float.IsNaN(amount) || float.IsInfinity(amount) || amount<0)
                throw new System.InvalidOperationException($"Правило урона вернуло некорректное значение: actor={actor.id}, attack={attack.id}, damage={amount}.");
            amount=protection.Resolve(actor,attack,amount);
            return ApplyAmount(actor,attack.id,amount,naturalContact:naturalContact);
        }
        public bool ApplyPhysical(ActorState actor,PropImpactContact contact)
        {
            PropImpactRules.Validate(contact);
            if(actor==null || contact==null || actor.id!=contact.target || actor.level!=contact.source.level ||
                actor.lifecycleSequence!=contact.lifecycle || actor.health<=0 || actor.resolvedAttacks.Contains(contact.id)) return false;
            if(protection is not IPhysicalHitProtectionRule physical)
                throw new System.InvalidOperationException("Правило защиты не поддерживает физический контакт Box: "+actor.id);
            float amount=protection is IPhysicalContactProtectionRule typed?typed.ResolvePhysicalContact(actor,contact):physical.ResolvePhysical(actor,contact.damage);
            // Capture body placement before momentum changes and possible knockdown, just as ordinary damage does.
            bool applied=ApplyAmount(actor,contact.id,amount);
            if(applied)
            {
                if(!contact.targetFixed) {actor.vx=contact.targetVx;actor.vy=contact.targetVy;actor.climbing=false;}
                PropImpactCooldown.Record(actor,contact.source.level,contact.until);
                // Unit.damage calls alarma; a hanging bat wakes before its control can zero the received momentum.
                if(!actor.down && ActorTypes.Get(actor.actorType).behavior==NpcCreatureBehaviours.Bat && actor.creature?.mode=="hang")
                    actor.creature.mode="return";
            }
            return applied;
        }
        // A level contact (spikes): the amount before protection is the contact rule's; the victim's typed protection
        // applies as to any attack of that damage type.
        public bool ApplyContact(ActorState actor,string contactId,string damageType,float amount)
        {
            if(actor==null || string.IsNullOrWhiteSpace(contactId) || actor.health<=0 || actor.resolvedAttacks.Contains(contactId)) return false;
            if(!float.IsFinite(amount) || amount<0)
                throw new System.InvalidOperationException($"Некорректный урон контакта: actor={actor.id}, contact={contactId}, damage={amount}.");
            if(protection is not ITypedContactProtectionRule typed)
                throw new System.InvalidOperationException("Правило защиты не поддерживает типизированный контакт: "+actor.id);
            return ApplyAmount(actor,contactId,typed.ResolveContact(actor,damageType,amount,contactId));
        }
        // A creature destroying itself (UnitMsp.control → die() next to its target): its remaining health, no protection;
        // the death then takes the ordinary accepted path (death voice, its deathExplosion).
        public bool SelfDestruct(ActorState actor,string id)
        {
            if(actor==null || actor.player || actor.down || actor.health<=0 || string.IsNullOrWhiteSpace(id) || actor.resolvedAttacks.Contains(id)) return false;
            return ApplyAmount(actor,id,actor.health);
        }
        // A player's health above its radiation limit (PlayerRadiation.HealthLimit) drops to it, as the loss of a hit
        // (knockdown at 0) but with no attack to record: the owner calls it as the radiation grows.
        public bool LimitHealth(ActorState actor)
        {
            if(actor==null || !actor.player || actor.down || actor.health<=0) return false;
            float limit=PlayerRadiation.HealthLimit(actor);
            return actor.health>limit && ApplyAmount(actor,"radiation:"+actor.id+":"+(actor.damageSequence+1),actor.health-limit,record:false);
        }
        private bool ApplyAmount(ActorState actor,string attackId,float amount,bool record=true,bool naturalContact=false)
        {
            var bodyPosition=actor.BodyPosition;float previousHealth=actor.health;bool previouslyDown=actor.down;
            if(!float.IsFinite(amount) || amount<0)
                throw new System.InvalidOperationException($"Правило защиты вернуло некорректный урон: actor={actor.id}, attack={attackId}, damage={amount}.");
            float remaining=Mathf.Max(0,previousHealth-amount);
            float lost=previousHealth-remaining;
            float restore=ActorTypes.Get(actor.actorType).restoreHealthAfterHit;
            bool enter=restore<=0 && knockdown.ShouldEnter(new KnockdownContext(previousHealth,remaining,actor.player));
            if(record) actor.resolvedAttacks.Add(attackId);
            actor.health=restore>0?restore:remaining;
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
            if(lost>0) Applied?.Invoke(new DamageResult {attack=attackId,actor=actor.id,level=actor.level,
                generation=actor.generation,actorType=actor.actorType,lifecycleSequence=actor.lifecycleSequence,
                damageSequence=actor.damageSequence,
                confirmedNpcDeath=!actor.player && !previouslyDown && previousHealth>0 && actor.health==0 && actor.down,
                naturalContact=naturalContact,amount=lost,x=bodyPosition.x,y=bodyPosition.y});
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
