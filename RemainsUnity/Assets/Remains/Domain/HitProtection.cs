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
    // A level contact of a given damage type that is not a weapon attack (spikes).
    public interface ITypedContactProtectionRule {float ResolveContact(ActorState target,string damageType,float incomingDamage,string contactId);}
    public sealed class TypeHitProtectionRule : IHitProtectionRule,IPhysicalHitProtectionRule,IPhysicalContactProtectionRule,ITypedContactProtectionRule
    {
        private readonly System.Func<ActorState,DamageTargetContact> targetContact;
        public TypeHitProtectionRule(System.Func<ActorState,DamageTargetContact> targetContact=null)
        {this.targetContact=targetContact;}
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
        public float ResolveContact(ActorState target,string damageType,float incomingDamage,string contactId)
        {DamageTypes.Validate(damageType);return ResolveTyped(target,damageType,incomingDamage,contactId);}
        private float ResolveTyped(ActorState target,string type,float incomingDamage,string attackId)
        {
            if(type==DamageTypes.Electric)
            {
                if(targetContact==null) throw new System.InvalidOperationException(
                    $"Для электрического урона не подключён контакт цели: actor={target.id}, level={target.level}, attack={attackId}.");
                incomingDamage*=targetContact(target).ElectricMultiplier;
            }
            float intrinsic=ActorTypes.Get(target.actorType).hitProtection*FactoryEnemyScaling.Protection(target.factoryEnemy);
            return EquippedArmorProtection.Resolve(EquippedArmorProtection.Profile(target),type,incomingDamage,intrinsic,attackId,target.id);
        }
    }
}
