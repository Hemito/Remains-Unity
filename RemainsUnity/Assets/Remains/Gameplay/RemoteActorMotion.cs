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
            public bool down;
            public Vector2 position,velocity,correction,received;
            public float age;
            public bool clockReady;
            public double clockOffset;
        }
        private readonly Dictionary<string,Sample> samples=new();
        private readonly List<string> removed=new();
        private int tick;
        private string worldId;

        public void Step(WorldState world,string localOwner,float dt,System.Func<string,bool> paused,double now)
        {
            if(worldId!=world.worldId) {Clear();worldId=world.worldId;}
            tick++;dt=Mathf.Max(0,dt);
            foreach(var actor in world.actors)
            {
                if(actor.owner==localOwner || !ActorPresence.IsPresent(actor)) {samples.Remove(actor.id);continue;}
                bool reset=!samples.TryGetValue(actor.id,out var sample) || sample.owner!=actor.owner || sample.level!=actor.level ||
                    sample.generation!=actor.generation || sample.lifecycle!=actor.lifecycleSequence || sample.down!=ActorSessionRules.IsPassive(actor);
                if(reset)
                {
                    sample=new Sample {owner=actor.owner,level=actor.level,generation=actor.generation,lifecycle=actor.lifecycleSequence,
                        sequence=actor.motionSequence,down=ActorSessionRules.IsPassive(actor),position=actor.Position,received=actor.Position,velocity=actor.Velocity};
                    samples[actor.id]=sample;
                    ObserveClock(actor,sample,now);
                }
                else if(ActorMotionReplication.IsNewer(actor.motionSequence,sample.sequence))
                {
                    var received=actor.Position;
                    float age=ObserveClock(actor,sample,now);
                    var projected=ActorSessionRules.IsPassive(actor)?received:received+actor.Velocity*age;
                    // Teleports must not be traversed as motion. Normal envelopes include the last observed speed.
                    float discontinuity=Mathf.Max(3,(sample.velocity.magnitude+actor.Velocity.magnitude)*PredictionSeconds*2);
                    if(Vector2.Distance(received,sample.received)>discontinuity) {sample.position=projected;sample.correction=Vector2.zero;}
                    else sample.correction=projected-sample.position;
                    sample.received=received;sample.sequence=actor.motionSequence;sample.velocity=actor.Velocity;sample.age=age;
                }
                sample.seen=tick;
                if(sample.clockReady && !paused(actor.level) && LevelActivity.HasConnectedPlayer(world,actor.level))
                {
                    // Bodies interpolate toward confirmed positions only: predicting their
                    // last falling velocity would carry them through an unseen landing.
                    float predict=ActorSessionRules.IsPassive(actor)?0:Mathf.Min(dt,Mathf.Max(0,PredictionSeconds-sample.age));
                    sample.age+=dt;
                    var correction=sample.correction*(1-Mathf.Exp(-dt/CorrectionSeconds));
                    sample.correction-=correction;
                    sample.position+=sample.velocity*predict+correction;
                }
                actor.Position=sample.position;
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
