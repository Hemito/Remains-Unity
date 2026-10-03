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
        }
        private readonly Dictionary<string,Sample> samples=new();
        private readonly List<string> removed=new();
        private int tick;
        private string worldId;

        public void Step(WorldState world,string localOwner,float dt,System.Func<string,bool> paused)
        {
            if(worldId!=world.worldId) {Clear();worldId=world.worldId;}
            tick++;dt=Mathf.Max(0,dt);
            foreach(var actor in world.actors)
            {
                if(actor.owner==localOwner || !ActorPresence.IsPresent(actor)) {samples.Remove(actor.id);continue;}
                bool reset=!samples.TryGetValue(actor.id,out var sample) || sample.owner!=actor.owner || sample.level!=actor.level ||
                    sample.generation!=actor.generation || sample.lifecycle!=actor.lifecycleSequence || sample.down!=actor.down;
                if(reset)
                {
                    sample=new Sample {owner=actor.owner,level=actor.level,generation=actor.generation,lifecycle=actor.lifecycleSequence,
                        sequence=actor.motionSequence,down=actor.down,position=actor.Position,received=actor.Position,velocity=actor.Velocity};
                    samples[actor.id]=sample;
                }
                else if(ActorMotionReplication.IsNewer(actor.motionSequence,sample.sequence))
                {
                    var received=actor.Position;
                    // Teleports must not be traversed as motion. Normal envelopes include the last observed speed.
                    float discontinuity=Mathf.Max(3,(sample.velocity.magnitude+actor.Velocity.magnitude)*PredictionSeconds*2);
                    if(Vector2.Distance(received,sample.received)>discontinuity) {sample.position=received;sample.correction=Vector2.zero;}
                    else sample.correction=received-sample.position;
                    sample.received=received;sample.sequence=actor.motionSequence;sample.velocity=actor.Velocity;sample.age=0;
                }
                sample.seen=tick;
                if(!paused(actor.level) && LevelActivity.HasConnectedPlayer(world,actor.level) && !actor.down)
                {
                    float predict=Mathf.Min(dt,Mathf.Max(0,PredictionSeconds-sample.age));
                    sample.age+=dt;
                    var correction=sample.correction*(1-Mathf.Exp(-dt/CorrectionSeconds));
                    sample.correction-=correction;
                    sample.position+=sample.velocity*predict+correction;
                }
                else if(actor.down) {sample.position=actor.Position;sample.correction=Vector2.zero;sample.velocity=Vector2.zero;}
                actor.Position=sample.position;
            }
            removed.Clear();foreach(var pair in samples) if(pair.Value.seen!=tick) removed.Add(pair.Key);
            foreach(string id in removed) samples.Remove(id);
        }
        public void Clear()=>samples.Clear();
    }
}
