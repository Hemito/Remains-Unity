using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Projects only non-owned live replicas. Recovery snapshots and owner messages remain untouched.
    public sealed class RemoteActorMotion
    {
        public const float PredictionSeconds=.1f,CorrectionSeconds=.05f;
        private sealed class Sample
        {
            public string owner,level;
            public int generation,lifecycle,seen;
            public uint sequence;
            public bool down,climbing;
            public string pose;
            public uint poseSequence;
            public Vector2 position,velocity,correction,received;
            public float age,phase,phaseCorrection,receivedPhase;
            public bool clockReady;
            public double clockOffset;
        }
        private readonly Dictionary<string,Sample> samples=new();
        private readonly List<string> removed=new();
        private int tick;
        private string worldId;

        // The caller must not independently advance a phase belonging to this
        // confirmed pair. Running/falling animation retains its existing clock.
        public bool ControlsPosePhase(ActorState actor)=>actor!=null && actor.climbing && !ActorSessionRules.IsPassive(actor) &&
            samples.TryGetValue(actor.id,out var sample) && sample.climbing && sample.owner==actor.owner &&
            sample.level==actor.level && sample.generation==actor.generation && sample.lifecycle==actor.lifecycleSequence &&
            sample.pose==actor.pose && sample.poseSequence==actor.poseSequence;

        public void Step(WorldState world,string localOwner,float dt,System.Func<string,bool> paused,double now)
        {
            if(worldId!=world.worldId) {Clear();worldId=world.worldId;}
            tick++;dt=Mathf.Max(0,dt);
            foreach(var actor in world.actors)
            {
                if(actor.owner==localOwner || !ActorPresence.IsPresent(actor)) {samples.Remove(actor.id);continue;}
                bool climbing=actor.climbing && !ActorSessionRules.IsPassive(actor);
                bool reset=!samples.TryGetValue(actor.id,out var sample) || sample.owner!=actor.owner || sample.level!=actor.level ||
                    sample.generation!=actor.generation || sample.lifecycle!=actor.lifecycleSequence || sample.down!=ActorSessionRules.IsPassive(actor) ||
                    !ActorMotionReplication.IsNewer(actor.motionSequence,sample.sequence) && sample.climbing!=climbing;
                if(reset)
                {
                    sample=new Sample {owner=actor.owner,level=actor.level,generation=actor.generation,lifecycle=actor.lifecycleSequence,
                        sequence=actor.motionSequence,down=ActorSessionRules.IsPassive(actor),position=actor.Position,received=actor.Position,velocity=actor.Velocity,
                        climbing=climbing,pose=actor.pose,poseSequence=actor.poseSequence,phase=actor.poseTime,receivedPhase=actor.poseTime};
                    samples[actor.id]=sample;
                    ObserveClock(actor,sample,now);
                }
                else if(ActorMotionReplication.IsNewer(actor.motionSequence,sample.sequence))
                {
                    var received=actor.Position;
                    float age=ObserveClock(actor,sample,now);
                    var projected=ActorSessionRules.IsPassive(actor) || climbing?received:received+actor.Velocity*age;
                    // Teleports must not be traversed as motion. Normal envelopes include the last observed speed.
                    float discontinuity=Mathf.Max(3,(sample.velocity.magnitude+actor.Velocity.magnitude)*PredictionSeconds*2);
                    bool teleport=Vector2.Distance(received,sample.received)>discontinuity;
                    if(climbing)
                    {
                        // A hold resets phase; a direction/rate or pose change is
                        // a different path. Never traverse that reset as an animation.
                        bool changedPath=!sample.climbing || sample.pose!=actor.pose || sample.poseSequence!=actor.poseSequence ||
                            (sample.velocity-actor.Velocity).sqrMagnitude>.000001f ||
                            (actor.poseTime-sample.receivedPhase)*actor.vy<-.000001f;
                        if(teleport || changedPath)
                        {sample.position=received;sample.phase=actor.poseTime;sample.correction=Vector2.zero;sample.phaseCorrection=0;}
                        else
                        {sample.correction=received-sample.position;sample.phaseCorrection=actor.poseTime-sample.phase;}
                    }
                    else if(teleport || sample.climbing)
                    {sample.position=projected;sample.correction=Vector2.zero;sample.phaseCorrection=0;}
                    else sample.correction=projected-sample.position;
                    sample.received=received;sample.sequence=actor.motionSequence;sample.velocity=actor.Velocity;sample.age=age;
                    sample.climbing=climbing;sample.pose=actor.pose;sample.poseSequence=actor.poseSequence;sample.receivedPhase=actor.poseTime;
                }
                sample.seen=tick;
                if(sample.clockReady && !paused(actor.level) && LevelActivity.HasConnectedPlayer(world,actor.level))
                {
                    // Bodies interpolate toward confirmed positions only: predicting their
                    // last falling velocity would carry them through an unseen landing.
                    float predict=ActorSessionRules.IsPassive(actor) || sample.climbing?0:Mathf.Min(dt,Mathf.Max(0,PredictionSeconds-sample.age));
                    sample.age+=dt;
                    float fraction=1-Mathf.Exp(-dt/CorrectionSeconds);
                    var correction=sample.correction*fraction;
                    sample.correction-=correction;
                    sample.position+=sample.velocity*predict+correction;
                    if(sample.climbing)
                    {
                        float phaseCorrection=sample.phaseCorrection*fraction;
                        sample.phaseCorrection-=phaseCorrection;sample.phase+=phaseCorrection;
                    }
                }
                actor.Position=sample.position;
                if(sample.climbing) actor.poseTime=sample.phase;
            }
            removed.Clear();foreach(var pair in samples) if(pair.Value.seen!=tick) removed.Add(pair.Key);
            foreach(string id in removed) samples.Remove(id);
        }
        private static float ObserveClock(ActorState actor,Sample sample,double now)
        {
            // A recovered/handoff pose can predate this owner's clock. Wait for its
            // first publication; a previous owner's timestamp is not comparable.
            if(actor.motionClockGeneration!=actor.generation) {sample.clockReady=false;return 0;}
            if(!double.IsFinite(actor.motionSentAt) || actor.motionSentAt<0)
            {
                sample.clockReady=false;
                Debug.LogWarning($"Некорректная отметка движения: actor={actor.id}, generation={actor.generation}, sequence={actor.motionSequence}, time={actor.motionSentAt}.");
                return 0;
            }
            double offset=now-actor.motionSentAt;
            sample.clockOffset=sample.clockReady?System.Math.Min(sample.clockOffset,offset):offset;
            sample.clockReady=true;
            // Minimum observed offset includes the unknown constant one-way delay.
            // Only excess delivery delay is compensated; no cross-PC clock equality.
            return (float)System.Math.Clamp(offset-sample.clockOffset,0,PredictionSeconds);
        }
        public void Clear()=>samples.Clear();
    }
}
