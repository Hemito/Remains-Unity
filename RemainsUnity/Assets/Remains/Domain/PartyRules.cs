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
                actor.level="hub";actor.health=1;actor.down=false;actor.pose="idle";
                actor.x=System.Array.IndexOf(players,actor)*2;actor.y=0;actor.vx=actor.vy=0;
                actor.injuries.RemoveAll(injury=>injury.periodicDamage);
                actor.lifecycleSequence++;
            }
            world.revision++;
            return true;
        }
    }
}
