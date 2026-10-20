using System.Linq;

namespace Remains.Domain
{
    public static class PartyRules
    {
        public static bool ResolveDefeat(WorldState world,IPartyDefeatRule rule=null)
        {
            rule??=new HubPartyDefeatRule();
            var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            var statuses=players.Select(a=>new PartyMemberStatus(a)).ToArray();
            if(players.Length==0 || !rule.IsDefeated(statuses)) return false;
            // Validate every result before applying any: one bad mod result cannot half-recover a party.
            var recoveries=statuses.Select((member,index)=>rule.Recovery(member,index)).ToArray();
            foreach(var recovery in recoveries)
            {
                if(string.IsNullOrWhiteSpace(recovery.level) || float.IsNaN(recovery.health) || float.IsInfinity(recovery.health) || recovery.health<=0 ||
                    float.IsNaN(recovery.position.x) || float.IsInfinity(recovery.position.x) || float.IsNaN(recovery.position.y) || float.IsInfinity(recovery.position.y))
                    throw new System.InvalidOperationException($"Правило поражения вернуло некорректное восстановление: level={recovery.level}, health={recovery.health}, position={recovery.position}.");
            }
            foreach(string level in recoveries.Select(r=>r.level).Distinct()) RoomLayout.Load(level,world);
            for(int i=0;i<players.Length;i++)
            {
                var actor=players[i];var recovery=recoveries[i];
                actor.health=recovery.health;actor.down=false;PropImpactCooldown.Clear(actor);actor.level=recovery.level;
                actor.Position=recovery.position;actor.Velocity=UnityEngine.Vector2.zero;ActorPose.Select(actor,"idle");
                if(recovery.removePeriodicInjuries) actor.injuries.RemoveAll(injury=>injury.periodicDamage);
                actor.lifecycleSequence++;actor.revision++;
            }
            world.revision++;
            return true;
        }
    }
}
