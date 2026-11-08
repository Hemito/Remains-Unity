using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using Remains.Content;
using Remains.Persistence;
using System.Linq;
using UnityEngine;

namespace Remains.Networking
{
    public sealed partial class CoopSession
    {
        private readonly Dictionary<Type,Action<NetworkConnectionToClient,object,int>> localServerHandlers=new();
        private readonly Queue<(Type type,object message,int channel)> localHostRequests=new();
        internal void RegisterServerHandler<T>(Action<NetworkConnectionToClient,T> handler,bool requireAuthentication=true) where T:struct,NetworkMessage
            =>RegisterServerHandler<T>((connection,message,channel)=>handler(connection,message),requireAuthentication);
        internal void RegisterServerHandler<T>(Action<NetworkConnectionToClient,T,int> handler,bool requireAuthentication=true) where T:struct,NetworkMessage
        {
            NetworkServer.RegisterHandler(handler,requireAuthentication);
            localServerHandlers[typeof(T)]=(connection,message,channel)=>
            {
                if(requireAuthentication && !connection.isAuthenticated) throw new InvalidOperationException("Unauthenticated local host request.");
                handler(connection,(T)message,channel);
            };
        }
        // Local authority uses exactly the registered server validation, without serialization or a transport echo.
        public void SendToServer<T>(T message,int channel=Channels.Reliable) where T:struct,NetworkMessage
        {
            if(!SharedHostWorld || LocalActorId==null)
            {NetworkClient.Send(message,channel);return;}
            if(!localServerHandlers.ContainsKey(typeof(T))) throw new InvalidOperationException("No local authority handler for "+typeof(T).Name);
            localHostRequests.Enqueue((typeof(T),message,channel));
        }
        // Process after gameplay Update loops. A travel/grenade command may change level topology or remove a flight;
        // applying it inside its caller's enumeration would invalidate that simulation loop.
        private void FlushLocalHostRequests()
        {
            int count=localHostRequests.Count;
            for(int i=0;i<count;i++)
            {
                var request=localHostRequests.Dequeue();
                if(!SharedHostWorld || LocalActorId==null || NetworkServer.localConnection==null) continue;
                localServerHandlers[request.type](NetworkServer.localConnection,request.message,request.channel);
            }
        }
        private void ResetHostWorldBuffers()
        {
            localHostRequests.Clear();hostChangePending=hostUnknownParts=false;hostChangeKind=WorldChangeKind.None;
            hostChangeParts.Clear();hostActorLives.Clear();
        }
        internal bool IsSharedHostConnection(NetworkConnectionToClient connection)=>SharedHostWorld && ReferenceEquals(connection,NetworkServer.localConnection);
        internal void BroadcastState<T>(T message,int channel=Channels.Reliable) where T:struct,NetworkMessage
        {
            foreach(var connection in NetworkServer.connections.Values)
                if(!IsSharedHostConnection(connection)) connection.Send(message,channel);
        }
        private bool? hostSelfTicks;
        public bool HostSelfTicks=>hostSelfTicks??Debug.isDebugBuild;
        public bool SharedHostWorld=>NetworkServer.active && !HostSelfTicks;
        public void ConfigureHostSelfTicks(bool? enabled)
        {
            if(NetworkServer.active || NetworkClient.active) throw new InvalidOperationException("Host mode cannot change during a session.");
            hostSelfTicks=enabled??Debug.isDebugBuild;
        }
        private bool hostChangePending,hostUnknownParts;
        private WorldChangeKind hostChangeKind;
        private readonly HashSet<string> hostChangeParts=new(StringComparer.Ordinal);
        private void QueueSharedHostChanged(WorldChangeKind kind,IReadOnlyCollection<string> parts)
        {
            hostChangePending=true;hostChangeKind|=kind;
            if(parts==null) hostUnknownParts=true;
            else foreach(string part in parts) hostChangeParts.Add(part);
        }
        // Direct local authority can commit while a physics system enumerates its flights. Reconcile presentation
        // after all Update simulation loops, retaining the exact union of composed keys rather than re-entering them.
        internal void FlushSharedHostChanged()
        {
            if(!hostChangePending) return;
            var kind=hostChangeKind;IReadOnlyCollection<string> parts=hostUnknownParts?null:hostChangeParts.ToArray();
            hostChangePending=hostUnknownParts=false;hostChangeKind=WorldChangeKind.None;hostChangeParts.Clear();
            if(SharedHostWorld && World!=null && LocalActorId!=null) DeliverChanged(kind,parts);
        }
        private readonly Dictionary<string,(int life,bool alive)> hostActorLives=new(StringComparer.Ordinal);
        private void ObserveHostLives()
        {
            hostActorLives.Clear();
            if(ServerWorld!=null) foreach(var actor in ServerWorld.actors)
                hostActorLives[actor.id]=(actor.lifecycleSequence,!actor.down && actor.health>0);
        }
        private void WelcomeSharedHost(RecoverySnapshot snapshot,string owner,string actorId)
        {
            var welcomed=ServerWorld??throw new InvalidOperationException("Host has no authoritative world.");
            BeginJoinSelection();joinSelection.RequireCurrent(CharacterId,Saves);
            joinSelection.RequireWelcome(welcomed,actorId,owner);
            FactoryContentSources.Prepare(welcomed,welcomed.actors.Where(a=>a.player && a.connected).Select(a=>a.level).Distinct());
            EntryPreparation.Take(welcomed,actorId);
            LocalOwner=owner;LocalActorId=actorId;localActorSlot=-1;
            foreach(var actor in welcomed.actors) CosmeticEquipment.ReconcileInventory(actor);
            WeaponRecoilReplication.NeutralizeWorld(welcomed,Pause.ServerRevision);
            foreach(var actor in welcomed.actors) WorldChanges.Mark(welcomed,actor);
            World=welcomed;
            ObserveHostLives();
            if(LocalActor.level!="hub") Saves.PreserveCharacterSource();
            QueueRecoveryReceipt(snapshot);
            Debug.Log("Подключён хост: один мир, character="+LocalActor.character+", actor="+LocalActorId);
            DeliverChanged();
            if(!GameplayFailurePending) EntryPreparation.CompleteAdmission();
        }
        private void PublishSharedHostTick(WorldTick tick)
        {
            if(!ReferenceEquals(World,ServerWorld)) throw new InvalidOperationException("Shared host graph was replaced.");
            FactoryContentSources.Prepare(World,World.actors.Where(a=>a.player && a.connected).Select(a=>a.level).Distinct());
            if(WorldTickReceiver.Changes(tick).Any(WorldChangeKind.Actors))
            {
                var ids=new HashSet<string>(World.actors.Select(a=>a.id),StringComparer.Ordinal);
                foreach(string id in hostActorLives.Keys.Where(id=>!ids.Contains(id)).ToArray()) hostActorLives.Remove(id);
                foreach(var actor in World.actors)
                    if(!hostActorLives.TryGetValue(actor.id,out var previous) || previous.life!=actor.lifecycleSequence || actor.owner!=LocalOwner)
                        hostActorLives[actor.id]=(actor.lifecycleSequence,!actor.down && actor.health>0);
            }
            RaiseChanged(WorldTickReceiver.Changes(tick),WorldTickReceiver.ChangedParts(tick));
        }
        private void PublishSharedHostActor(ActorState actor,WorldChangeKind changes)
        {
            if(actor==null || !ReferenceEquals(World,ServerWorld) || !ReferenceEquals(World.Find(actor.id),actor) || actor.owner!=LocalOwner)
                throw new InvalidOperationException("Local host may publish only its current authoritative actor.");
            WeaponRecoilReplication.Normalize(actor,Pause.ServerRevision,Pause.IsServerPaused(actor.level),"хост / один мир");
            CosmeticEquipment.Validate(actor,CosmeticCatalog.Current);
            bool wasAlive=hostActorLives.TryGetValue(actor.id,out var previous) && previous.life==actor.lifecycleSequence && previous.alive;
            var loot=CreatureDeathLoot.PrepareSharedHost(World,actor,wasAlive);
            var before=new ActorState {id=actor.id,player=actor.player,health=wasAlive?1:0,down=!wasAlive,lifecycleSequence=actor.lifecycleSequence};
            var blast=SessionDeathBlasts.Blast(before,actor);
            loot?.ApplyAccepted(World);
            hostActorLives[actor.id]=(actor.lifecycleSequence,!actor.down && actor.health>0);
            actor.motionSequence=unchecked(actor.motionSequence+1);StampMotion(actor);
            WorldChanges.Mark(World,actor);World.revision++;
            if(blast!=null) DeathBlasts.Detonate(blast);
            else if(wasAlive && (actor.down || actor.health<=0)) BroadcastWorld();
            else BroadcastState(serverActors.Next(actor));
            RaiseChanged(changes,changes==WorldChangeKind.Actors?new[]{"actors:"+actor.id}:null);
        }
    }
}
