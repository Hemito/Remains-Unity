using System;
using System.Collections.Generic;
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
        private readonly ReplicationCadence cadence=new(.05);
        private readonly PassengerMotionRegistry passengers=new();
        private readonly FreePropMotionContext motion=new(trackSupport:true);
        private readonly FreePropPoseCadence poses=new();
        private readonly PropImpactController impacts;
        private readonly Action<PropState> publishChanged;
        private readonly Action<PropState,bool> publishPose;
        private readonly Action<PropState,Vector2> notifyMotion;
        private readonly Action<PropState> retainFinalPose;
        private readonly Action<string> publishDue;
        private readonly HashSet<string> finalPoses=new(StringComparer.Ordinal);
        private double publishTime;
        private string poseWorld;
        private string poseOwner;
        public FreePropSimulation(CoopSession session,SimulationGeometry geometry,InteractionAdapter interactions,PlatformSimulation platforms=null)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            Remains.Content.PropImpactCatalog.Load();impacts=new PropImpactController(session);
            roomForLevel=(geometry??throw new ArgumentNullException(nameof(geometry))).Get;
            publishPose=(interactions??throw new ArgumentNullException(nameof(interactions))).SendProp;
            notifyMotion=interactions.NotifyPropMotion;
            publishChanged=prop=> {if(poses.ShouldPublish(prop,publishTime,out bool changed)) publishPose(prop,changed);};
            retainFinalPose=prop=>finalPoses.Add(prop.id);
            publishDue=PublishCurrentPose;
            isPaused=session.Pause.IsPaused;
            session.Changed+=()=>
            {
                passengers.Reconcile(session.World,session.LocalOwner);
                if(session.LastChange.Any(WorldChangeKind.Props|WorldChangeKind.Containers)) motion.InvalidateSupport();
            };
            interactions.PropMoved+=motion.ExternalMotion;
            session.PropImpacts.ImpulseAdopted+=motion.WakeImpulse;
            geometry.OcclusionChanged+=(level,room,revision)=>motion.WakeLevel(level);
            if(platforms!=null) platforms.TopologyChanged+=room=>
            {
                foreach(string level in geometry.LoadedLevels)
                    if(ReferenceEquals(geometry.Find(level),room)) motion.WakeLevel(level);
            };
        }
        public void Step(float delta,IReadOnlyList<ActorState> connectedPlayers=null)
        {
            // No simulation owner exists before welcome or after client shutdown.
            if(session.World==null || string.IsNullOrEmpty(session.LocalOwner)) return;
            // A new graph of the same world (every received snapshot) keeps the sent poses; another world or owner resets.
            if(poseWorld!=session.World.worldId || poseOwner!=session.LocalOwner)
            {poses.Reset();finalPoses.Clear();poseWorld=session.World.worldId;poseOwner=session.LocalOwner;}
            publishTime=Time.unscaledTimeAsDouble;
            bool publish=cadence.TryAdvance(publishTime);
            FreePropMotion.Step(session.World,session.LocalOwner,delta,roomForLevel,isPaused,
                publish?publishChanged:null,passengers,motion,notifyMotion,retainFinalPose,connectedPlayers);
            if(publish)
            {
                using var section=Remains.Diagnostics.FrameHitchLog.Section("Тела: повторы поз");
                foreach(string id in finalPoses) PublishCurrentPose(id);
                finalPoses.Clear();
                poses.PublishDue(publishTime,publishDue);
            }
            using(Remains.Diagnostics.FrameHitchLog.Section("Тела: удары ящиков")) impacts.Step(motion);
        }
        private void PublishCurrentPose(string id)
        {
            var prop=WorldProps.Find(session.World,id);
            if(prop==null || prop.owner!=session.LocalOwner || !prop.movable || !prop.hasPhysicalBody) return;
            if(prop.held || !motion.Running(prop.level)) {poses.Defer(id,publishTime);return;}
            publishChanged(prop);
        }
    }
}
