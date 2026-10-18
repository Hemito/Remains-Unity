using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // The animation/pose writers share one active-zone decision. Refresh once per simulation frame;
    // queries inspect connected players only, not the entire actor list for each actor.
    public sealed class ActorActivity
    {
        private readonly List<ActorState> players=new();
        private readonly HashSet<string> levels=new(StringComparer.Ordinal);
        private readonly HashSet<string> pendingPoses=new(StringComparer.Ordinal);
        private string worldId;
        public void Refresh(WorldState world)
        {
            if(worldId!=world.worldId) {pendingPoses.Clear();worldId=world.worldId;}
            players.Clear();levels.Clear();
            foreach(var actor in world.actors)
                if(actor.player && actor.connected) {players.Add(actor);levels.Add(actor.level);}
        }
        public bool Active(ActorState actor)=>ActorPresence.IsPresent(actor) && levels.Contains(actor.level) &&
            (actor.player || ActorSessionRules.IsPassive(actor) || ActorHoldRules.Active(actor) || NpcActivity.Awake(players,actor));
        // Remember activity until the next transport cadence, even when the actor/player crosses the room boundary
        // in this frame. The final real pose is sent once; its sleeping successors do not advance transport clocks.
        public void ObserveSimulation(ActorState actor)=>pendingPoses.Add(actor.id);
        public bool TakePosePublication(ActorState actor,out bool final)
        {
            bool pending=pendingPoses.Remove(actor.id);
            bool active=Active(actor);final=pending && !active;
            return pending || active;
        }
    }
}
