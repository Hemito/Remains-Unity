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
        private readonly PassengerMotionRegistry passengers=new();
        private readonly FreePropMotionContext motion=new();
        private readonly FreePropPoseCadence poses=new();
        private readonly Action<PropState> publishChanged;
        private WorldState poseWorld;
        private string poseOwner;
        public FreePropSimulation(CoopSession session,SimulationGeometry geometry,InteractionAdapter interactions)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            roomForLevel=(geometry??throw new ArgumentNullException(nameof(geometry))).Get;
            publish=(interactions??throw new ArgumentNullException(nameof(interactions))).SendProp;
            publishChanged=prop=> {if(poses.ShouldPublish(prop,Time.unscaledTimeAsDouble)) publish(prop);};
            isPaused=session.Pause.IsPaused;
            session.Changed+=()=>passengers.Reconcile(session.World,session.LocalOwner);
        }
        public void Step(float delta)
        {
            // No simulation owner exists before welcome or after client shutdown.
            if(session.World==null || string.IsNullOrEmpty(session.LocalOwner)) return;
            if(!ReferenceEquals(poseWorld,session.World) || poseOwner!=session.LocalOwner)
            {poses.Reset();poseWorld=session.World;poseOwner=session.LocalOwner;}
            FreePropMotion.Step(session.World,session.LocalOwner,delta,roomForLevel,isPaused,
                cadence.TryAdvance(Time.unscaledTimeAsDouble)?publishChanged:null,passengers,motion);
        }
    }
}
