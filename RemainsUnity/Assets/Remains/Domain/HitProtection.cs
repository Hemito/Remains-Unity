using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    public interface IHitProtectionRule
    {
        float Resolve(ActorState target,AttackEvent attack,float incomingDamage);
    }
    public sealed class TypeHitProtectionRule : IHitProtectionRule
    {
        public float Resolve(ActorState target,AttackEvent attack,float incomingDamage)
        {
            float absorbed=Mathf.Min(incomingDamage,ActorTypes.Get(target.actorType).hitProtection*FactoryEnemyScaling.Protection(target.factoryEnemy));
            return incomingDamage-absorbed;
        }
    }
}
