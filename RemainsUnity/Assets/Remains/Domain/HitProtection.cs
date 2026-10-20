using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    public interface IHitProtectionRule
    {
        float Resolve(ActorState target,AttackEvent attack,float incomingDamage);
    }
    public interface IPhysicalHitProtectionRule {float ResolvePhysical(ActorState target,float incomingDamage);}
    public sealed class TypeHitProtectionRule : IHitProtectionRule,IPhysicalHitProtectionRule
    {
        public float Resolve(ActorState target,AttackEvent attack,float incomingDamage)
            =>ResolvePhysical(target,incomingDamage);
        public float ResolvePhysical(ActorState target,float incomingDamage)
        {
            float absorbed=Mathf.Min(incomingDamage,ActorTypes.Get(target.actorType).hitProtection*FactoryEnemyScaling.Protection(target.factoryEnemy));
            return incomingDamage-absorbed;
        }
    }
}
