using System;
using System.Collections.Generic;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Networking
{
    public struct PropImpactSampleMessage : NetworkMessage {public string json;}
    public struct PropImpactSampleAccepted : NetworkMessage {public string json;}
    public struct PropImpactRequest : NetworkMessage {public string prop,sample,target;public int generation,lifecycle;public float targetVx,targetVy;}
    public struct PropImpactAccepted : NetworkMessage {public string json;}

    // The host journals source samples and accepted contact decisions. It does not re-test victim geometry.
    // NPC contact belongs to the Box owner; player contact belongs to that player's owner.
    public sealed class SessionPropImpacts
    {
        private readonly CoopSession session;
        private readonly Dictionary<string,PropImpactSample> accepted=new(StringComparer.Ordinal);
        public PropImpactSpatialIndex Spatial {get;}=new();
        private readonly Dictionary<string,Dictionary<string,PropImpactContact>> pending=new(StringComparer.Ordinal),localPending=new(StringComparer.Ordinal);
        public PropImpactTargetIndex Targets {get;}=new();
        private WorldState serverGraph;
        private string clientWorld;
        private readonly Dictionary<string,Queue<PropImpactSample>> expirations=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Queue<PropImpactSample>> serverExpirations=new(StringComparer.Ordinal);
        private readonly Dictionary<string,bool> running=new(StringComparer.Ordinal);
        private readonly List<string> activeLevels=new();
        private readonly PropImpactSourceInbox newSources=new();
        private double nextRetry;
        private Func<ActorState,PropImpactContact,bool> apply;
        public IReadOnlyDictionary<string,PropImpactSample> Samples=>accepted;
        public SessionPropImpacts(CoopSession session)
        {
            this.session=session;session.Changed+=()=>
            {
                if(session.World==null || session.LastChange.Any(WorldChangeKind.Actors|WorldChangeKind.Identity))
                {Targets.Refresh(session.World,session.LastChangedParts);newSources.DropInactive(Targets.HasLevel);}
            };
        }
        private static void Put(Dictionary<string,Dictionary<string,PropImpactContact>> groups,PropImpactContact contact)
        {
            if(!groups.TryGetValue(contact.source.level,out var level)) groups.Add(contact.source.level,level=new Dictionary<string,PropImpactContact>(StringComparer.Ordinal));
            level[contact.id]=contact;
        }
        public void Configure(Func<ActorState,PropImpactContact,bool> receiver)=>apply=receiver??throw new ArgumentNullException(nameof(receiver));
        public void RegisterServer()
        {
            serverGraph=null;pending.Clear();serverExpirations.Clear();nextRetry=0;
            NetworkServer.RegisterHandler<PropImpactSampleMessage>(MessageTiming.Server<PropImpactSampleMessage>(Sample));
            NetworkServer.RegisterHandler<PropImpactRequest>(MessageTiming.Server<PropImpactRequest>(Contact));
        }
        public void RegisterClient()
        {
            accepted.Clear();Spatial.Clear();expirations.Clear();newSources.Clear();localPending.Clear();clientWorld=null;
            NetworkClient.RegisterHandler<PropImpactSampleAccepted>(MessageTiming.Client<PropImpactSampleAccepted>(message=>
            {
                var sample=StateJson.FromJson<PropImpactSample>(message.json);
                if(session.World==null || !PropImpactRules.Valid(sample) || sample.world!=session.World.worldId) return;
                ResetWorld();
                if(!accepted.TryGetValue(sample.prop,out var old) || sample.generation>old.generation ||
                    sample.generation==old.generation && PropReplication.IsNewer(sample.sequence,old.sequence))
                {
                    accepted[sample.prop]=sample;Spatial.Add(sample);
                    if(!expirations.TryGetValue(sample.level,out var queue)) expirations.Add(sample.level,queue=new Queue<PropImpactSample>());
                    queue.Enqueue(sample);
                    // Geometry is decided once at accepted-source delivery, never against an old retained
                    // point on a later frame. The two-second index is delivery/history retention only.
                    newSources.Accept(sample,Targets.HasLevel(sample.level));
                    var prop=WorldProps.Find(session.World,sample.prop);
                    if(prop!=null && prop.generation==sample.generation)
                    {
                        prop.impacts??=new PropImpactJournal();
                        if(prop.impacts.sampleGeneration!=sample.generation || PropReplication.IsNewer(sample.sequence,prop.impacts.sampleSequence))
                        {prop.impacts.sampleGeneration=sample.generation;prop.impacts.sampleSequence=sample.sequence;}
                    }
                }
            }));
            NetworkClient.RegisterHandler<PropImpactAccepted>(MessageTiming.Client<PropImpactAccepted>(message=>
            {
                var contact=StateJson.FromJson<PropImpactContact>(message.json);
                if(session.World==null || contact?.source?.world!=session.World.worldId) return;
                ResetWorld();
                var prop=WorldProps.Find(session.World,contact.source.prop);
                PropImpactRules.AdoptImpulse(prop,contact);
                Put(localPending,contact);
            }));
        }
        private void ResetWorld()
        {
            if(clientWorld==session.World?.worldId) return;
            accepted.Clear();Spatial.Clear();expirations.Clear();newSources.Clear();localPending.Clear();clientWorld=session.World?.worldId;
            if(session.World==null) return;
            // Recovery/join reads the durable accepted journals once; ordinary snapshots do not re-scan them.
            foreach(var prop in WorldProps.All(session.World)) if(prop.impacts!=null)
                foreach(var contact in prop.impacts.contacts) Put(localPending,contact);
        }
        public void Publish(PropImpactSample sample)=>NetworkClient.Send(new PropImpactSampleMessage {json=StateJson.ToJson(sample)});
        public void Report(PropImpactSample sample,ActorState target)=>NetworkClient.Send(new PropImpactRequest
        {prop=sample.prop,sample=sample.id,target=target.id,generation=target.generation,lifecycle=target.lifecycleSequence,targetVx=target.vx,targetVy=target.vy});
        public void Expire(string level,double now)
        {
            if(!expirations.TryGetValue(level,out var queue)) return;
            while(queue.Count>0 && queue.Peek().at<now-PropImpactRules.SampleHistorySeconds)
            {
                var sample=queue.Dequeue();
                if(accepted.TryGetValue(sample.prop,out var current) && current.id==sample.id)
                {accepted.Remove(sample.prop);Spatial.Remove(sample.prop,sample.id);}
            }
        }
        public void DrainSources(string level,List<PropImpactSample> output)
            =>newSources.Drain(level,output);
        private void Sample(NetworkConnectionToClient connection,PropImpactSampleMessage message)
        {
            var actor=session.ConnectedActor(connection);var world=session.ServerWorld;
            if(world==null || actor==null) return;
            var incoming=StateJson.FromJson<PropImpactSample>(message.json);
            var prop=incoming==null?null:WorldProps.Find(world,incoming.prop);
            if(prop==null || !PropPoseScope.CanPublish(world,actor,prop,session.Pause.IsServerPaused(prop.level)) ||
                !session.LevelTime.HasLocal(prop.level)) return;
            bool duplicate=prop.impacts?.samples.Exists(value=>value.id==incoming.id)==true;
            if(!PropImpactRules.AcceptSample(world,prop,actor.owner,incoming,session.LevelTime.ReadServer(prop.level),out var sample))
            {Debug.LogWarning($"Отклонён образец Box: id={incoming?.id}, prop={prop.id}, owner={actor.owner}, generation={prop.generation}/{incoming?.generation}.");return;}
            WorldChanges.Mark(world,prop);
            if(duplicate) {connection.Send(new PropImpactSampleAccepted {json=StateJson.ToJson(sample)});return;}
            if(!serverExpirations.TryGetValue(sample.level,out var expiry)) serverExpirations.Add(sample.level,expiry=new Queue<PropImpactSample>());
            expiry.Enqueue(sample);
            NetworkServer.SendToAll(new PropImpactSampleAccepted {json=StateJson.ToJson(sample)});
        }
        private void Contact(NetworkConnectionToClient connection,PropImpactRequest request)
        {
            var reporter=session.ConnectedActor(connection);var world=session.ServerWorld;
            if(world==null || reporter==null) return;
            var prop=WorldProps.Find(world,request.prop);var target=world.Find(request.target);
            if(prop?.impacts==null || target==null || target.generation!=request.generation || target.lifecycleSequence!=request.lifecycle || target.level!=prop.level ||
                session.Pause.IsServerPaused(prop.level) || !LevelActivity.HasConnectedPlayer(world,prop.level)) return;
            var sample=prop.impacts.samples.Find(value=>value.id==request.sample);
            if(sample==null || !PropImpactRules.CanReport(sample,target,reporter.owner))
            {Debug.LogWarning($"Отклонён контакт Box без принятого источника/права решения: prop={prop.id}, sample={request.sample}, target={target.id}, reporter={reporter.owner}.");return;}
            string id=sample.id+":"+target.id+":"+target.lifecycleSequence;
            var previous=prop.impacts.contacts.Find(value=>value.id==id);
            if(previous!=null) {connection.Send(new PropImpactAccepted {json=StateJson.ToJson(previous)});return;}
            if(!PropImpactCatalog.ResolveBody(target,out var profile))
            {Debug.LogWarning($"Нет профиля физического контакта Box: target={target.id}, type={target.actorType}, prop={prop.id}; контакт пропущен.");return;}
            var contact=PropImpactRules.Prepare(world,prop,sample,target,profile,new Vector2(request.targetVx,request.targetVy),session.LevelTime.ReadServer(prop.level));
            if(contact==null) return;
            PropImpactRules.Commit(world,prop,target,contact);Put(pending,contact);
            NetworkServer.SendToAll(new PropImpactAccepted {json=StateJson.ToJson(contact)});
            session.CommitWorldAtFrameEnd();
        }
        private readonly List<string> completed=new();
        public void Step()
        {
            activeLevels.Clear();activeLevels.AddRange(Targets.Levels);
            if(NetworkServer.active && session.ServerWorld!=null)
            {
                if(serverGraph!=session.ServerWorld)
                {
                    serverGraph=session.ServerWorld;pending.Clear();serverExpirations.Clear();
                    foreach(var prop in WorldProps.All(serverGraph)) if(prop.impacts!=null)
                    {
                        foreach(var contact in prop.impacts.contacts) Put(pending,contact);
                        foreach(var sample in prop.impacts.samples)
                        {
                            if(!serverExpirations.TryGetValue(sample.level,out var expiry)) serverExpirations.Add(sample.level,expiry=new Queue<PropImpactSample>());
                            expiry.Enqueue(sample);
                        }
                    }
                    foreach(var queue in serverExpirations.Values)
                    {
                        var sorted=queue.ToArray();Array.Sort(sorted,(a,b)=>a.at.CompareTo(b.at));queue.Clear();
                        foreach(var sample in sorted) queue.Enqueue(sample);
                    }
                }
                if(NetworkTime.time>=nextRetry)
                {
                    nextRetry=NetworkTime.time+.15;completed.Clear();
                    running.Clear();
                    foreach(string level in activeLevels)
                    {
                        if(!Running(serverGraph,level,true)) continue;
                        ExpireServer(level);
                        if(!pending.TryGetValue(level,out var levelPending)) continue;
                        completed.Clear();
                        foreach(var pair in levelPending)
                        {
                        var contact=pair.Value;
                        if(!Running(serverGraph,contact.source.level,true)) continue;
                        var target=serverGraph.Find(contact.target);
                        if(target==null || target.lifecycleSequence!=contact.lifecycle || target.level!=contact.source.level ||
                            target.health<=0 || target.resolvedAttacks.Contains(contact.id)) {completed.Add(pair.Key);continue;}
                        NetworkServer.SendToAll(new PropImpactAccepted {json=StateJson.ToJson(contact)});
                        }
                        foreach(string id in completed)
                        {
                            var finished=levelPending[id];var prop=WorldProps.Find(serverGraph,finished.source.prop);
                            if(prop?.impacts!=null && prop.impacts.contacts.RemoveAll(value=>value.id==id)>0) WorldChanges.Mark(serverGraph,prop);
                            levelPending.Remove(id);
                        }
                    }
                }
            }
            if(session.World==null || apply==null) return;
            ResetWorld();completed.Clear();running.Clear();
            foreach(string level in activeLevels)
            {
                if(!localPending.TryGetValue(level,out var levelPending) || !Running(session.World,level,false)) continue;
                completed.Clear();
                foreach(var pair in levelPending)
                {
                var contact=pair.Value;
                if(!Running(session.World,contact.source.level,false)) continue;
                var target=session.World.Find(contact.target);
                if(target==null || target.lifecycleSequence!=contact.lifecycle || target.level!=contact.source.level || target.health<=0 ||
                    target.resolvedAttacks.Contains(contact.id)) {completed.Add(pair.Key);continue;}
                if(target.owner!=session.LocalOwner) continue;
                if(apply(target,contact)) session.PublishActor(target);
                completed.Add(pair.Key);
                }
                foreach(string id in completed) levelPending.Remove(id);
            }
        }
        private bool Running(WorldState world,string level,bool server)
        {
            if(!running.TryGetValue(level,out bool value))
                running[level]=value=!(server?session.Pause.IsServerPaused(level):session.Pause.IsPaused(level)) && LevelActivity.HasConnectedPlayer(world,level);
            return value;
        }
        private void ExpireServer(string level)
        {
            if(!serverExpirations.TryGetValue(level,out var queue) || !session.LevelTime.HasLocal(level)) return;
            double before=session.LevelTime.ReadServer(level)-PropImpactRules.SampleHistorySeconds;
            while(queue.Count>0 && queue.Peek().at<before)
            {
                var sample=queue.Dequeue();
                var prop=WorldProps.Find(serverGraph,sample.prop);
                if(prop?.impacts!=null && prop.impacts.samples.RemoveAll(value=>value.id==sample.id)>0) WorldChanges.Mark(serverGraph,prop);
            }
        }
    }
}
