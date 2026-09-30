using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

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
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            NetworkClient.Send(new JoinMessage { character = CharacterId, displayName = CharacterId });
        }

        private static WorldState CreateWorld()
        {
            var world = new WorldState();
            world.actors.Add(new ActorState { id = "npc:training-guard", owner = "host", x = 8, health = 140 });
            world.container.Add(SharedInventory.Create("caps", 20));
            world.container.Add(SharedInventory.Create("heal", 3));
            world.container.Add(SharedInventory.Create("grenade", 3));
            return world;
        }

        private void Join(NetworkConnectionToClient connection, JoinMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.character) || owners.ContainsKey(connection.connectionId))
            {
                connection.Disconnect();
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
                actor = new ActorState { id = "player:" + message.character, character = message.character,
                    player = true, x = ServerWorld.actors.Count(a => a.player) * 2, hair = ServerWorld.actors.Count(a => a.player) };
                actor.inventory.Add(SharedInventory.Create("heal", 5));
                actor.inventory.Add(SharedInventory.Create("grenade", 5));
                ServerWorld.actors.Add(actor);
            }
            actor.owner = owner;
            actor.connected = true;
            actor.generation++;
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
                    if (local == null || local.owner != LocalOwner || local.generation != actor.generation) continue;
                    actor.Position = local.Position;
                    actor.Velocity = local.Velocity;
                    actor.pose = local.pose;
                    if (local.revision > actor.revision)
                    {
                        actor.health = local.health; actor.down = local.down; actor.revision = local.revision;
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
            if (current != null && (actor.generation < current.generation ||
                actor.generation == current.generation && actor.revision < current.revision)) return;
            if (current != null) World.actors.Remove(current);
            World.actors.Add(actor);
            Changed?.Invoke();
        }

        private void UpdateActor(NetworkConnectionToClient connection, ActorMessage message)
        {
            var incoming = JsonUtility.FromJson<ActorState>(message.json);
            var actor = ServerWorld.Find(incoming.id);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner ||
                actor.generation != incoming.generation || incoming.revision <= actor.revision) return;
            // Shared inventory is host-coordinated; movement/combat/stat decisions remain client-owned.
            incoming.inventory = actor.inventory;
            incoming.owner = actor.owner;
            incoming.connected = actor.connected;
            ServerWorld.actors[ServerWorld.actors.IndexOf(actor)] = incoming;
            ServerWorld.revision++;
            NetworkServer.SendToAll(new ActorMessage { json = JsonUtility.ToJson(incoming) });
        }

        public void PublishActor(ActorState actor)
        {
            if (NetworkClient.isConnected) NetworkClient.Send(new ActorMessage { json = JsonUtility.ToJson(actor) });
        }

        private void UpdatePose(NetworkConnectionToClient connection, PoseMessage message)
        {
            var actor = ServerWorld.Find(message.actor);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner ||
                actor.generation != message.generation || !AcceptSequence(message)) return;
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
            if (actor == null || actor.owner == LocalOwner || actor.generation != message.generation) return;
            // Separate client sequence map: in host mode server already accepted this message.
            ApplyPose(actor, message);
        }

        private static void ApplyPose(ActorState actor, PoseMessage message)
        {
            actor.x = message.x; actor.y = message.y; actor.vx = message.vx; actor.vy = message.vy;
            actor.facing = message.facing; actor.pose = message.pose;
        }

        public void PublishPose(ActorState actor, uint sequence) => NetworkClient.Send(new PoseMessage
        {
            actor = actor.id, generation = actor.generation, sequence = sequence,
            x = actor.x, y = actor.y, vx = actor.vx, vy = actor.vy, facing = actor.facing, pose = actor.pose
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

        private void Revive(NetworkConnectionToClient connection, ReviveMessage message)
        {
            if (!owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.completedTransactions.Contains(message.transaction)) return;
            var healer = ServerWorld.Find(playerIds[owner]);
            var target = ServerWorld.Find(message.target);
            if (healer.down || target == null || !target.down || healer.level != target.level ||
                Vector2.Distance(healer.Position, target.Position) > 2 || !SharedInventory.Consume(healer, "heal")) return;
            target.down = false;
            target.health = 35;
            target.pose = "idle";
            healer.revision++; target.revision++;
            ServerWorld.completedTransactions.Add(message.transaction);
            ServerWorld.revision++;
            BroadcastWorld();
            NetworkServer.SendToAll(new ActorMessage { json = JsonUtility.ToJson(target) });
            connection.Send(new NoticeMessage { text = "Союзник поднят. Лечебный предмет использован." });
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

        private void CheckpointWorld()
        {
            if (!NetworkServer.active || ServerWorld == null) return;
            ownership.Reassign(ServerWorld, owners.Values.ToArray());
            ServerWorld.revision++;
            Saves.SaveWorld(ServerWorld);
            BroadcastWorld();
        }

        public override void OnStopServer()
        {
            CancelInvoke(nameof(CheckpointWorld));
            if (ServerWorld != null) Saves.SaveWorld(ServerWorld);
        }
    }
}
