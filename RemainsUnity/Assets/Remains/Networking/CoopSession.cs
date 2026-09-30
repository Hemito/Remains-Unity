using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;
using Remains.Content;

namespace Remains.Networking
{
    // Transport adapter and session coordinator. Domain rules live in separate services.
    public sealed class CoopSession : NetworkManager
    {
        public WorldState World { get; private set; }
        public WorldState ServerWorld { get; private set; }
        public string LocalOwner { get; private set; }
        public string LocalActorId { get; private set; }
        public ActorState LocalActor => World?.Find(LocalActorId);
        public SaveStore Saves { get; set; }
        public string CharacterId { get; set; }
        public event Action Changed;
        public event Action<AttackEvent> AttackReceived;
        public event Action<string> Notice;
        public event Action<NpcHitMessage> NpcHitReceived;
        public event Action ServerReady;
        public event Action ClientReady;
        public event Action<string> ModMismatch;
        private readonly Dictionary<int, string> owners = new();
        private readonly Dictionary<string, string> playerIds = new();
        private readonly Dictionary<string, uint> poseSequences = new();
        private readonly Dictionary<int,long> backupRevisions = new();
        private readonly OwnershipService ownership = new();
        private SharedInventory inventory;

        public override void OnStartServer()
        {
            ServerWorld = Saves.LoadWorld() ?? CreateWorld();
            foreach (var actor in ServerWorld.actors.Where(a => a.player)) actor.connected = false;
            inventory = new SharedInventory(ServerWorld);
            NetworkServer.RegisterHandler<JoinMessage>(Join);
            NetworkServer.RegisterHandler<ActorMessage>(UpdateActor);
            NetworkServer.RegisterHandler<PoseMessage>(UpdatePose);
            NetworkServer.RegisterHandler<AttackMessage>(RelayAttack);
            NetworkServer.RegisterHandler<NpcHitMessage>(RelayNpcHit);
            NetworkServer.RegisterHandler<ClaimMessage>(Claim);
            NetworkServer.RegisterHandler<ReviveMessage>(Revive);
            NetworkServer.RegisterHandler<BackupAckMessage>((connection, ack) =>
            {
                if (ack.world != ServerWorld.worldId) Debug.LogWarning($"Подтверждение чужого мира: клиент={connection.connectionId}, мир={ack.world}, ожидается={ServerWorld.worldId}.");
                else { backupRevisions[connection.connectionId]=ack.revision; CompleteBackedUpGrants(); }
            });
            InvokeRepeating(nameof(CheckpointWorld), 3, 3);
            ServerReady?.Invoke();
        }

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<WelcomeMessage>(Welcome);
            NetworkClient.RegisterHandler<WorldMessage>(ReceiveWorld);
            NetworkClient.RegisterHandler<ActorMessage>(message =>
            {
                var actor = JsonUtility.FromJson<ActorState>(message.json);
                MergeActor(actor);
            });
            NetworkClient.RegisterHandler<PoseMessage>(ReceivePose);
            NetworkClient.RegisterHandler<AttackMessage>(message => AttackReceived?.Invoke(JsonUtility.FromJson<AttackEvent>(message.json)));
            NetworkClient.RegisterHandler<NpcHitMessage>(message => NpcHitReceived?.Invoke(message));
            NetworkClient.RegisterHandler<NoticeMessage>(message => Notice?.Invoke(message.text));
            NetworkClient.RegisterHandler<ModMismatchMessage>(message=>ModMismatch?.Invoke(message.description));
            ClientReady?.Invoke();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            SendJoin(false);
        }

        public void SendJoin(bool acceptMismatch)
        {
            var character=Saves.LoadCharacter();
            NetworkClient.Send(new JoinMessage { character = CharacterId, displayName = CharacterId,
                characterJson=character==null?null:JsonUtility.ToJson(character),
                modFingerprint=WeaponCatalog.ModFingerprint,acceptMismatch=acceptMismatch });
        }

        private static WorldState CreateWorld()
        {
            var world = new WorldState();
            world.actors.Add(new ActorState { id = "npc:training-guard", owner = "host", x = 8, health = 140 });
            world.container.Add(SharedInventory.Create("caps", 20));
            world.container.Add(SharedInventory.Create("heal", 3));
            world.container.Add(SharedInventory.Create("grenade", 3));
            world.props.Add(new PropState { id="prop:crate-1",owner="host",x=-3,y=0.5f });
            return world;
        }

        private void Join(NetworkConnectionToClient connection, JoinMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.character) || owners.ContainsKey(connection.connectionId))
            {
                connection.Disconnect();
                return;
            }
            if(message.modFingerprint!=WeaponCatalog.ModFingerprint && !message.acceptMismatch)
            {
                connection.Send(new ModMismatchMessage {description=WeaponCatalog.ModDescription});
                return;
            }
            var actor = ServerWorld.actors.Find(a => a.character == message.character);
            if (actor?.connected == true)
            {
                connection.Send(new NoticeMessage { text = "Этот персонаж уже подключён." });
                connection.Disconnect();
                return;
            }
            string owner = "connection:" + connection.connectionId;
            owners[connection.connectionId] = owner;
            if (actor == null)
            {
                actor=string.IsNullOrEmpty(message.characterJson)?null:JsonUtility.FromJson<ActorState>(message.characterJson);
                if(actor!=null && actor.character!=message.character)
                    throw new InvalidOperationException($"Персонаж в пакете не соответствует ID: {actor.character}, ожидается {message.character}.");
                if(actor==null)
                {
                    actor=new ActorState();
                    actor.inventory.Add(SharedInventory.Create("heal", 5));
                    actor.inventory.Add(SharedInventory.Create("grenade", 5));
                }
                actor.id="player:"+message.character;actor.character=message.character;actor.player=true;
                actor.x=ServerWorld.actors.Count(a=>a.player)*2;actor.y=0;actor.level="factory:0";
                actor.hair=ServerWorld.actors.Count(a=>a.player);
                ServerWorld.actors.Add(actor);
            }
            actor.owner = owner;
            actor.connected = true;
            actor.generation++;
            actor.lifecycleSequence++;
            actor.revision++;
            playerIds[owner] = actor.id;
            ownership.Reassign(ServerWorld, owners.Values.ToArray());
            ServerWorld.revision++;
            connection.Send(new WelcomeMessage { owner = owner, actor = actor.id, worldJson = JsonUtility.ToJson(ServerWorld) });
            BroadcastWorld();
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            if (owners.Remove(connection.connectionId, out string owner))
            {
                backupRevisions.Remove(connection.connectionId);
                var actor = ServerWorld.Find(playerIds[owner]);
                if (actor.level == "hub") ServerWorld.actors.Remove(actor);
                else
                {
                    actor.connected = false;
                    actor.down = true;
                    actor.lifecycleSequence++;
                    actor.pose = "down";
                    actor.revision++;
                }
                playerIds.Remove(owner);
                ownership.Reassign(ServerWorld, owners.Values.ToArray());
                ServerWorld.revision++;
                Saves.SaveWorld(ServerWorld);
                BroadcastWorld();
                CompleteBackedUpGrants();
            }
            base.OnServerDisconnect(connection);
        }

        private void Welcome(WelcomeMessage message)
        {
            LocalOwner = message.owner;
            LocalActorId = message.actor;
            World = JsonUtility.FromJson<WorldState>(message.worldJson);
            PersistCopy();
            Changed?.Invoke();
        }

        private void ReceiveWorld(WorldMessage message)
        {
            var incoming = JsonUtility.FromJson<WorldState>(message.json);
            if (World != null && World.worldId == incoming.worldId)
            {
                // A full backup must not rewind locally-owned pose or an unacknowledged health change.
                foreach (var actor in incoming.actors)
                {
                    var local = World.Find(actor.id);
                    if (local == null || local.owner != LocalOwner || local.generation != actor.generation ||
                        local.lifecycleSequence!=actor.lifecycleSequence || local.level!=actor.level) continue;
                    actor.Position = local.Position;
                    actor.Velocity = local.Velocity;
                    actor.pose = local.pose;
                    actor.poseTime=local.poseTime;
                    if (local.revision > actor.revision)
                    {
                        actor.health = local.health; actor.down = local.down; actor.revision = local.revision;
                        actor.mana=local.mana;
                    }
                }
            }
            World = incoming;
            PersistCopy();
            Changed?.Invoke();
        }

        private void PersistCopy()
        {
            Saves.SaveWorld(World);
            if (LocalActor != null) Saves.SaveCharacter(LocalActor);
            if (NetworkClient.isConnected) NetworkClient.Send(new BackupAckMessage { world = World.worldId, revision = World.revision });
        }

        private void MergeActor(ActorState actor)
        {
            if (World == null) return;
            var current = World.Find(actor.id);
            if (current != null && ActorVersion.IsOlder(actor,current)) return;
            if (current != null) World.actors.Remove(current);
            World.actors.Add(actor);
            Changed?.Invoke();
        }

        private void UpdateActor(NetworkConnectionToClient connection, ActorMessage message)
        {
            var incoming = JsonUtility.FromJson<ActorState>(message.json);
            var actor = ServerWorld.Find(incoming.id);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner ||
                !ActorVersion.CanPublishOwnerState(incoming,actor)) return;
            // Shared inventory is host-coordinated; movement/combat/stat decisions remain client-owned.
            incoming.inventory = actor.inventory;
            incoming.owner = actor.owner;
            incoming.connected = actor.connected;
            if(incoming.level!=actor.level) incoming.lifecycleSequence++;
            ServerWorld.actors[ServerWorld.actors.IndexOf(actor)] = incoming;
            ServerWorld.revision++;
            NetworkServer.SendToAll(new ActorMessage { json = JsonUtility.ToJson(incoming) });
        }

        public void PublishActor(ActorState actor)
        {
            Changed?.Invoke();
            if (NetworkClient.isConnected) NetworkClient.Send(new ActorMessage { json = JsonUtility.ToJson(actor) });
        }

        private void UpdatePose(NetworkConnectionToClient connection, PoseMessage message)
        {
            var actor = ServerWorld.Find(message.actor);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner ||
                actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence || !AcceptSequence(message)) return;
            ApplyPose(actor, message);
            NetworkServer.SendToAll(message, Channels.Unreliable);
        }

        private bool AcceptSequence(PoseMessage message)
        {
            string key = message.actor + ":" + message.generation;
            if (poseSequences.TryGetValue(key, out uint last) && message.sequence <= last) return false;
            poseSequences[key] = message.sequence;
            return true;
        }

        private void ReceivePose(PoseMessage message)
        {
            var actor = World?.Find(message.actor);
            if (actor == null || actor.owner == LocalOwner || actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence) return;
            // Separate client sequence map: in host mode server already accepted this message.
            ApplyPose(actor, message);
        }

        private static void ApplyPose(ActorState actor, PoseMessage message)
        {
            actor.x = message.x; actor.y = message.y; actor.vx = message.vx; actor.vy = message.vy;
            actor.facing = message.facing; actor.pose = message.pose;
            actor.poseTime=message.poseTime;
            actor.weapon=message.weapon;actor.weaponAngle=message.weaponAngle;
            actor.weaponTipX=message.weaponTipX;actor.weaponTipY=message.weaponTipY;
            actor.weaponReach=message.weaponReach;
        }

        public void PublishPose(ActorState actor, uint sequence) => NetworkClient.Send(new PoseMessage
        {
            actor = actor.id, generation = actor.generation,lifecycleSequence=actor.lifecycleSequence, sequence = sequence,
            x = actor.x, y = actor.y, vx = actor.vx, vy = actor.vy, facing = actor.facing, pose = actor.pose,poseTime=actor.poseTime,
            weapon=actor.weapon,weaponAngle=actor.weaponAngle,weaponTipX=actor.weaponTipX,weaponTipY=actor.weaponTipY,weaponReach=actor.weaponReach
        }, Channels.Unreliable);

        public void Fire(AttackEvent attack) => NetworkClient.Send(new AttackMessage { json = JsonUtility.ToJson(attack) });
        private void RelayAttack(NetworkConnectionToClient connection, AttackMessage message)
        {
            var attack = JsonUtility.FromJson<AttackEvent>(message.json);
            if (!owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.Find(attack.attacker)?.owner != owner) return;
            NetworkServer.SendToAll(message);
        }

        public void ReportNpcHit(NpcHitMessage message) => NetworkClient.Send(message);
        private void RelayNpcHit(NetworkConnectionToClient connection, NpcHitMessage message)
        {
            var attack = JsonUtility.FromJson<AttackEvent>(message.attackJson);
            var target = ServerWorld.Find(message.target);
            if (target == null || target.generation != message.generation ||
                !owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.Find(attack.attacker)?.owner != owner) return;
            var recipient = owners.FirstOrDefault(pair => pair.Value == target.owner);
            if (NetworkServer.connections.TryGetValue(recipient.Key, out var destination)) destination.Send(message);
        }

        public void RequestClaim(string item) => NetworkClient.Send(new ClaimMessage { transaction = Guid.NewGuid().ToString("N"), item = item });
        public void RequestRevive(string target) => NetworkClient.Send(new ReviveMessage { target = target, transaction = Guid.NewGuid().ToString("N") });
        public void RequestSelfHeal()=>NetworkClient.Send(new ReviveMessage {target=LocalActorId,transaction=Guid.NewGuid().ToString("N"),selfHeal=true});
        public void ReturnToFactory()
        {
            if(LocalActor==null || LocalActor.down || LocalActor.level!="hub") return;
            LocalActor.level="factory:0";LocalActor.Position=Vector2.zero;LocalActor.Velocity=Vector2.zero;LocalActor.revision++;
            PublishActor(LocalActor);
        }

        private void Revive(NetworkConnectionToClient connection, ReviveMessage message)
        {
            if (!owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.completedTransactions.Contains(message.transaction)) return;
            var healer = ServerWorld.Find(playerIds[owner]);
            var target = ServerWorld.Find(message.target);
            if(!HealingRules.Apply(healer,target,message.selfHeal)) return;
            ServerWorld.completedTransactions.Add(message.transaction);
            ServerWorld.revision++;
            BroadcastWorld();
            NetworkServer.SendToAll(new ActorMessage { json = JsonUtility.ToJson(target) });
            connection.Send(new NoticeMessage { text = message.selfHeal?"Лечебный предмет использован.":"Союзник поднят. Лечебный предмет использован." });
            Saves.SaveWorld(ServerWorld);
        }
        private void Claim(NetworkConnectionToClient connection, ClaimMessage message)
        {
            if (!owners.TryGetValue(connection.connectionId, out string owner)) return;
            var actor = ServerWorld.Find(playerIds[owner]);
            bool success = inventory.PrepareClaim(message.transaction, message.item, actor);
            connection.Send(new NoticeMessage { text = success ? "Подбор подтверждается копиями мира…" : "Этот предмет уже забрали." });
            if (success) { Saves.SaveWorld(ServerWorld); BroadcastWorld(); CompleteBackedUpGrants(); }
        }

        private void CompleteBackedUpGrants()
        {
            bool changed=false;
            foreach(var grant in ServerWorld.pendingGrants.ToArray())
            {
                // Every currently connected peer must durably write the reservation first.
                if(owners.Keys.Any(id=>!backupRevisions.TryGetValue(id,out long rev) || rev<grant.preparedRevision)) continue;
                inventory.CommitGrant(grant);
                changed=true;
            }
            if(changed) { Saves.SaveWorld(ServerWorld); BroadcastWorld(); }
        }

        private void BroadcastWorld() => NetworkServer.SendToAll(new WorldMessage { json = JsonUtility.ToJson(ServerWorld) });
        public ActorState ConnectedActor(NetworkConnectionToClient connection) =>
            owners.TryGetValue(connection.connectionId,out var owner) && playerIds.TryGetValue(owner,out var id)?ServerWorld.Find(id):null;
        public void CommitWorld()
        {
            ServerWorld.revision++;
            Saves.SaveWorld(ServerWorld);
            BroadcastWorld();
        }

        private void CheckpointWorld()
        {
            if (!NetworkServer.active || ServerWorld == null) return;
            ownership.Reassign(ServerWorld, owners.Values.ToArray());
            if(PartyRules.ResolveDefeat(ServerWorld))
                NetworkServer.SendToAll(new NoticeMessage {text="Вся группа без сознания. Возвращение в хаб с 1 HP."});
            ServerWorld.revision++;
            Saves.SaveWorld(ServerWorld);
            BroadcastWorld();
        }

        public override void OnStopServer()
        {
            CancelInvoke(nameof(CheckpointWorld));
            if (ServerWorld != null) Saves.SaveWorld(ServerWorld);
        }
        public override void OnStopClient()
        {
            if(World!=null) {Saves.SaveWorld(World);if(LocalActor!=null) Saves.SaveCharacter(LocalActor);}
            World=null;LocalActorId=null;LocalOwner=null;
            Changed?.Invoke();
        }
        public void SetFriendlyFire(bool enabled)
        {
            if(!NetworkServer.active) return;
            ServerWorld.friendlyFire=enabled;CommitWorld();
        }
    }
}
