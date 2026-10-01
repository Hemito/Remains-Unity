using System.Linq;

namespace Remains.Domain
{
    public static class PartyRules
    {
        public static bool ResolveDefeat(WorldState world)
        {
            var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            if(players.Length==0 || players.Any(a=>!a.down)) return false;
            foreach(var actor in players)
            {
                actor.health=1;actor.down=false;
                CharacterPlacement.SpawnInHub(actor,System.Array.IndexOf(players,actor));
                actor.injuries.RemoveAll(injury=>injury.periodicDamage);
                actor.lifecycleSequence++;
            }
            world.revision++;
            return true;
        }
    }
}
