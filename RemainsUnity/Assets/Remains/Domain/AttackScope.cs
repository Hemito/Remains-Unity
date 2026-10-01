namespace Remains.Domain
{
    public static class AttackScope
    {
        // An event belongs to its origin level even after the source actor travels.
        public static bool Includes(AttackEvent attack, ActorState target) =>
            target != null && !string.IsNullOrEmpty(attack.level) && attack.level == target.level;
    }
}
