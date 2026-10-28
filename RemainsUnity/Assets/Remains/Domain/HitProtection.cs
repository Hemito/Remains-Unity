using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    public interface IHitProtectionRule
    {
        float Resolve(ActorState target,AttackEvent attack,float incomingDamage);
    }
    public interface IPhysicalHitProtectionRule {float ResolvePhysical(ActorState target,float incomingDamage);}
    public interface IPhysicalContactProtectionRule {float ResolvePhysicalContact(ActorState target,PropImpactContact contact);}
    public sealed class TypeHitProtectionRule : IHitProtectionRule,IPhysicalHitProtectionRule,IPhysicalContactProtectionRule
    {
        public float Resolve(ActorState target,AttackEvent attack,float incomingDamage)
            =>ResolveTyped(target,DamageTypes.Of(attack),incomingDamage,attack.id);
        public float ResolvePhysical(ActorState target,float incomingDamage)
        {
            // Compatibility for callers that request the legacy intrinsic protection only. Gameplay box contacts
            // supply their real durable contact ID through IPhysicalContactProtectionRule below.
            float absorbed=Mathf.Min(incomingDamage,ActorTypes.Get(target.actorType).hitProtection*FactoryEnemyScaling.Protection(target.factoryEnemy));
            return incomingDamage-absorbed;
        }
        public float ResolvePhysicalContact(ActorState target,PropImpactContact contact)
            =>ResolveTyped(target,DamageTypes.Physical,contact.damage,contact.id);
        private static float ResolveTyped(ActorState target,string type,float incomingDamage,string attackId)
        {
            float intrinsic=ActorTypes.Get(target.actorType).hitProtection*FactoryEnemyScaling.Protection(target.factoryEnemy);
            return EquippedArmorProtection.Resolve(EquippedArmorProtection.Profile(target),type,incomingDamage,intrinsic,attackId,target.id);
        }
    }
}
