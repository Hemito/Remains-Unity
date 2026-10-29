using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Gameplay
{
    // Spikes (SpikeRules) for the actors this peer simulates: their owner has the motion that decides a hit, so the owner
    // applies the damage to its own actor and publishes it, as a box contact (SessionPropImpacts). Runs after every local
    // motion of the frame. Cost: one pass over the actors with cheap filters; only a falling or rising actor near spikes
    // looks them up (sorted by column).
    public sealed class SpikeController
    {
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly CombatController combat;
        private readonly ConnectedPlayers connected=new();
        private readonly List<RoomSpike> near=new();
        public int Hits {get;private set;}
        public SpikeController(CoopSession session,SimulationGeometry geometry,CombatController combat)
        {this.session=session;this.geometry=geometry;this.combat=combat;}

        public void Step()
        {
            var world=session.World;
            if(world==null) return;
            connected.Refresh(world);
            foreach(var actor in world.actors)
            {
                // Neither falling fast enough nor rising: no spike can hurt it this frame.
                if(actor.vy<=0 && actor.vy>=-SpikeRules.FallSpeed) continue;
                if(actor.owner!=session.LocalOwner || !connected.OnLevel(actor.level) || session.Pause.IsPaused(actor.level)) continue;
                // The level is not simulated here yet (geometry or its clock still arriving with the level).
                var room=geometry.Find(actor.level);
                if(room==null || room.spikes.Length==0 || !session.LevelTime.HasLocal(actor.level) || !SpikeRules.Affects(actor)) continue;
                SpikeRules.Near(room,actor.x,near);
                if(near.Count==0) continue;
                double now=session.LevelTime.ReadLocal(actor.level);
                if(PropImpactCooldown.Active(actor,now)) continue;
                foreach(var spike in near)
                {
                    var definition=SpikeCatalog.Get(spike.definition);
                    float amount=SpikeRules.Damage(spike,definition,actor);
                    if(amount<=0) continue;
                    if(combat.ApplyContact(actor,"spikes:"+Guid.NewGuid().ToString("N"),definition.damageType,amount))
                    {
                        PropImpactCooldown.Record(actor,actor.level,now+SpikeRules.Cooldown(actor));
                        session.PublishActor(actor);Hits++;
                    }
                    break;   // the cooldown of the first hit protects from the neighbouring spikes too
                }
            }
        }
    }
}
