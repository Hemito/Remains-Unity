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
            var connection=NetworkServer.localConnection??throw new InvalidOperationException("Host has no local connection.");
            if(!localServerHandlers.TryGetValue(typeof(T),out var handler)) throw new InvalidOperationException("No local authority handler for "+typeof(T).Name);
            handler(connection,message,channel);
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
            RaiseChanged();
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
