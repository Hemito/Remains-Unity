using System.IO;

namespace Remains.Domain
{
    public static class FriendlyFireDecision
    {
        public const string Victim="victim",Attacker="attacker";
        public static void Validate(string policy)
        {if(policy!=Victim && policy!=Attacker) throw new InvalidDataException($"Неизвестное правило прямого дружественного огня: {policy}.");}
        public static bool UsesAttacker(AttackEvent attack,ActorState target)
            =>attack.weapon!="grenade" && attack.sourcePlayer && target.player && target.id!=attack.attacker && attack.friendlyFireDecision==Attacker;
    }
}
