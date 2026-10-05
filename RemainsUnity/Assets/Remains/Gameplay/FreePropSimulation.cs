using System;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    // Scheduling/replication adapter; FreePropMotion owns the physical dispatch rule.
    public sealed class FreePropSimulation
    {
        private readonly CoopSession session;
        private readonly Func<string,RoomLayout> roomForLevel;
        private readonly Func<string,bool> isPaused;
        private readonly Action<PropState> publish;
        private readonly ReplicationCadence cadence=new(.05);
        public FreePropSimulation(CoopSession session,SimulationGeometry geometry,InteractionAdapter interactions)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            roomForLevel=(geometry??throw new ArgumentNullException(nameof(geometry))).Get;
            publish=(interactions??throw new ArgumentNullException(nameof(interactions))).SendProp;
            isPaused=session.Pause.IsPaused;
        }
        public void Step(float delta)
        {
            // No simulation owner exists before welcome or after client shutdown.
            if(session.World==null || string.IsNullOrEmpty(session.LocalOwner)) return;
            FreePropMotion.Step(session.World,session.LocalOwner,delta,roomForLevel,isPaused,
                cadence.TryAdvance(Time.unscaledTimeAsDouble)?publish:null);
        }
    }
}
