using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class PropImpactController
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,(int generation,uint sequence,double next)> publishers=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        private PropImpactTargetIndex Targets=>session.PropImpacts.Targets;
        private readonly List<PropImpactSample> candidates=new();
        private readonly List<ActorState> bodies=new();
        private string world,owner;
        public PropImpactController(CoopSession session)
        {
            this.session=session;session.Changed+=()=>
            {
                if(session.World==null) {publishers.Clear();return;}
                if(!session.LastChange.Any(WorldChangeKind.Props|WorldChangeKind.Containers)) return;
                removed.Clear();
                foreach(var pair in publishers) if(WorldProps.Find(session.World,pair.Key)==null) removed.Add(pair.Key);
                foreach(string key in removed) publishers.Remove(key);
            };
        }
        public void Step(FreePropMotionContext context)
        {
            var state=session.World;
            if(state==null || string.IsNullOrEmpty(session.LocalOwner)) return;
            if(world!=state.worldId || owner!=session.LocalOwner)
            {world=state.worldId;owner=session.LocalOwner;publishers.Clear();}
            Targets.SynchronizeActive();
            foreach(string level in Targets.Levels) if(context.Running(level) && session.LevelTime.HasLocal(level))
                session.PropImpacts.Expire(level,session.LevelTime.ReadLocal(level));
            // Only sources stepped by the existing free-body dispatcher can publish a sample.
            foreach(var captured in context.ImpactSources)
            {
                var prop=captured.prop;
                if(prop.owner!=owner || !context.Running(prop.level) || prop.held || !PropImpactRules.IsBox(prop) ||
                    !PropImpactRules.Dangerous(captured.velocity) || !session.LevelTime.HasLocal(prop.level)) continue;
                var area=new Rect(captured.position.x-prop.width*.5f,captured.position.y-prop.anchorHeight,prop.width,prop.height);
                if(!Targets.IntersectsActiveZone(prop.level,area)) continue;
                double now=session.LevelTime.ReadLocal(prop.level);
                publishers.TryGetValue(prop.id,out var sent);
                if(sent.generation==prop.generation && now<sent.next) continue;
                uint sequence=PropImpactRules.NextSampleSequence(prop,sent.generation,sent.sequence);
                if(sequence==0) throw new InvalidOperationException("Исчерпан номер образца Box: "+prop.id);
                var sample=new PropImpactSample {world=world,prop=prop.id,owner=owner,level=prop.level,generation=prop.generation,sequence=sequence,
                    x=captured.position.x,y=captured.position.y,vx=captured.velocity.x,vy=captured.velocity.y};
                sample.id=PropImpactRules.SampleId(world,prop.id,prop.generation,sequence);
                session.PropImpacts.Publish(sample);publishers[prop.id]=(prop.generation,sequence,now+1d/30);
            }
            foreach(string level in Targets.Levels)
            {
                if(!context.Running(level) || !session.LevelTime.HasLocal(level)) continue;
                session.PropImpacts.DrainSources(level,candidates);
                foreach(var sample in candidates)
                {
                    var prop=context.Support.Find(sample.prop);
                    if(prop==null || prop.held || !PropImpactRules.IsBox(prop) || sample.generation!=prop.generation ||
                        session.LevelTime.ReadLocal(level)-sample.at>PropImpactRules.SampleHistorySeconds) continue;
                    Targets.Query(level,sample.Bounds,bodies);
                    foreach(var actor in bodies)
                        if(actor.health>0 && PropImpactRules.CanReport(sample,actor,owner) &&
                            !PropImpactCooldown.Active(actor,session.LevelTime.ReadLocal(level)) && PropImpactRules.Contact(sample,actor))
                            session.PropImpacts.Report(sample,actor);
                }
            }
        }
    }
}
