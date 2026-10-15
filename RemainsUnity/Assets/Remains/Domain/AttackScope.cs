namespace Remains.Domain
{
    public static class AttackScope
    {
        public static bool CanAffect(AttackEvent attack,ActorState target,bool friendlyFire) =>
            Includes(attack,target) && (target.id!=attack.attacker || Remains.Content.WeaponCatalog.IsExplosive(attack.weapon)) &&
            (!NpcNaturalAttack.IsNatural(attack) || target.id==attack.naturalAttackTarget) &&
            (!target.player || !attack.sourcePlayer || target.id==attack.attacker || friendlyFire);

        // An event belongs to its origin level even after the source actor travels.
        public static bool Includes(AttackEvent attack, ActorState target) =>
            ActorPresence.IsPresent(target) && !string.IsNullOrEmpty(attack.level) && attack.level == target.level;
    }
}
