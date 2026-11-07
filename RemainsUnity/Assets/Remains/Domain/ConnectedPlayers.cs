using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Connected players of a world and their levels, collected in one pass. A per-entity loop refreshes it once and
    // asks it instead of LevelActivity.HasConnectedPlayer / NpcActivity.Awake over the whole actor list, so the cost of
    // a step does not grow quadratically with the level's NPCs (TD-36). The players keep live positions; refresh
    // again after a player connects, disconnects or changes level.
    public sealed class ConnectedPlayers
    {
        private readonly List<ActorState> players=new(),all=new();
        private readonly HashSet<string> levels=new(StringComparer.Ordinal);
        public IReadOnlyList<ActorState> Players=>players;
        // Every player actor, connected or not: a disconnected player's body outside the hub stays present
        // (ActorPresence) and still sets off level mines and traps.
        public IReadOnlyList<ActorState> AllPlayers=>all;
        public void Refresh(WorldState world)
        {
            RefreshActors(world?.actors);
        }
        public void RefreshActors(IReadOnlyList<ActorState> actors)
        {
            players.Clear();all.Clear();levels.Clear();
            if(actors==null) return;
            for(int i=0;i<actors.Count;i++)
            {
                var actor=actors[i];
                if(!actor.player) continue;
                all.Add(actor);
                if(actor.connected) {players.Add(actor);levels.Add(actor.level);}
            }
        }
        // LevelActivity.HasConnectedPlayer for the refreshed world. Unconscious connected players count.
        public bool OnLevel(string level)=>levels.Contains(level);
    }
}
