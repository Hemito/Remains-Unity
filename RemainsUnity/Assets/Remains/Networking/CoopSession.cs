using JsonUtility=Remains.Serialization.StateJson;
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
        public CharacterContentGate CharacterContent {get;}=new();
        private GameplayRuleSet rules;
        public GameplayRuleSet Rules=>rules??throw new InvalidOperationException("Правила сессии ещё не настроены.");
        public void ConfigureRules(GameplayRuleSet rules)
        {
            if(NetworkServer.active || NetworkClient.active) throw new InvalidOperationException("Нельзя менять правила во время подключения или игры.");
            this.rules=rules??throw new ArgumentNullException(nameof(rules));
        }
        public string CharacterId { get; set; }
        public event Action Changed;
        public event Action<AttackEvent> AttackReceived;
        public event Action<string> Notice;
        public event Action<NpcHitMessage> NpcHitReceived;
        public event Action<FriendlyHitMessage> FriendlyHitReceived;
        public event Action ServerReady;
        public event Action ClientReady;
        public event Action<ContentManifest> ModMismatch;
        public event Action<string,bool> HealingFinished;
        public event Action<string,long> RecoveryPrepared;
        public event Action<int,string,long> RecoveryAcknowledged;
        public event Action<int> RecoveryPeerLeft;
        private readonly Dictionary<int, string> owners = new();
        private readonly Dictionary<string, string> playerIds = new();
        private readonly PoseSequenceGate serverPoses = new();
        private readonly PoseSequenceGate clientPoses = new();
        private NpcHitDelivery npcHits;
        public bool UseLobby {get;set;}
        private ProtocolAdmission admission;
        public ProtocolAdmission Admission=>admission ??= new ProtocolAdmission(this);
        private SessionLobby lobby;
        public SessionLobby Lobby=>lobby ??= new SessionLobby(this);
        private SessionTravel travel;
        public SessionTravel Travel => travel ??= new SessionTravel(this);
        private SessionDeposits deposits;
        public SessionDeposits Deposits=>deposits ??= new SessionDeposits(this);
        private SessionProjectiles projectiles;
        public SessionProjectiles Projectiles=>projectiles ??= new SessionProjectiles(this);
        private SessionPause pause;
        public SessionPause Pause=>pause ??= new SessionPause(this);
        public void ShowNotice(string text) => Notice?.Invoke(text);
        private readonly Dictionary<int,long> backupRevisions = new();
        private readonly RecoveryReceiptGate recoveryReceipts = new();
        private readonly WorldReplication worldReplication=new();
        private readonly OwnershipService ownership = new();
        private SharedInventory inventory;
        private readonly HashSet<string> recoveredActors=new();

        public override void OnStartServer()
        {
            npcHits=gameObject.AddComponent<NpcHitDelivery>();
            npcHits.Initialize(this,owner=>
            {
                if(string.IsNullOrEmpty(owner)) return null;
                var recipient=owners.FirstOrDefault(pair=>pair.Value==owner);
                if(recipient.Value!=owner) return null;
                return NetworkServer.connections.TryGetValue(recipient.Key,out var connection)?connection:null;
            });
            serverPoses.Clear();
            backupRevisions.Clear();recoveryReceipts.Clear();owners.Clear();playerIds.Clear();
            Travel.RegisterServer();
            Pause.RegisterServer();Deposits.RegisterServer();Projectiles.RegisterServer();
            Admission.RegisterServer();
            ServerWorld=null;
            Lobby.RegisterServer(UseLobby);
            if(!UseLobby) StartSelectedWorld(null,false);
            NetworkServer.RegisterHandler<JoinMessage>(Join);
            NetworkServer.RegisterHandler<ActorMessage>(UpdateActor);
            NetworkServer.RegisterHandler<PoseMessage>(UpdatePose);
            NetworkServer.RegisterHandler<AttackMessage>(RelayAttack);
            NetworkServer.RegisterHandler<NpcHitMessage>(RelayNpcHit);
            NetworkServer.RegisterHandler<FriendlyHitMessage>(RelayFriendlyHit);
            NetworkServer.RegisterHandler<ClaimMessage>(Claim);
            NetworkServer.RegisterHandler<ReviveMessage>(Revive);
            NetworkServer.RegisterHandler<BackupAckMessage>((connection, ack) =>
            {
                if(ServerWorld==null) return;
                if (ack.world != ServerWorld.worldId) Debug.LogWarning($"Подтверждение чужого мира: клиент={connection.connectionId}, мир={ack.world}, ожидается={ServerWorld.worldId}.");
                else if(!recoveryReceipts.Matches(ack.snapshot,ack.revision))
                    Debug.LogWarning($"Неизвестное подтверждение снимка: клиент={connection.connectionId}, снимок={ack.snapshot}, ревизия={ack.revision}.");
                else
                {
                    backupRevisions[connection.connectionId]=Math.Max(backupRevisions.GetValueOrDefault(connection.connectionId),ack.revision);
                    RecoveryAcknowledged?.Invoke(connection.connectionId,ack.snapshot,ack.revision);
                    CompleteBackedUpGrants();
                }
            });
            ServerReady?.Invoke();
        }

        internal void StartSelectedWorld(RecoverySnapshot snapshot,bool createNew,bool acceptContentMismatch=false)
        {
            var source=createNew?null:snapshot??Saves.SelectedSnapshot();
            if(source!=null)
            {
                var content=source.CreateLiveWorld().weaponContent;
                if(!acceptContentMismatch && ContentComparison.Compare(content,WeaponCatalog.ManifestCopy()).Count>0)
                    throw new InvalidOperationException("Состав контента сохранения отличается или неизвестен. Выберите мир в лобби и подтвердите сравнение перед загрузкой.");
                if(content!=null && !content.IsSupported)
                    throw new InvalidOperationException($"Формат манифеста не поддерживается: schema={content.schema}, scope={content.scope}. Исходный мир сохранён.");
            }
            if(snapshot!=null) Saves.SelectContinuation(snapshot);
            ServerWorld = createNew?CreateWorld():Saves.LoadWorld() ?? CreateWorld();
            npcHits?.Reset();
            if(HubTrainingContent.NeedsInstall(ServerWorld) || FactoryRaiderContent.NeedsInstall(ServerWorld))
            {
                if(source!=null) Debug.Log("Сохранена копия мира перед обновлением базового контента: "+Saves.PreserveRecoverySource());
                HubTrainingContent.Install(ServerWorld);FactoryRaiderContent.Install(ServerWorld);
            }
            FriendlyFireDecision.Validate(ServerWorld.friendlyFireDecision);
            WorldHistory.BeginContinuation(ServerWorld);
            GrenadeRules.BeginSession(ServerWorld);
            recoveredActors.Clear();
            foreach(var actor in ServerWorld.actors)
                if(SavedActorPlacement.Repair(actor,RoomLayout.Load(actor.level),"загрузка мира "+ServerWorld.worldId)) recoveredActors.Add(actor.id);
            if(recoveredActors.Count>0)
            {
                string backup=Saves.PreserveRecoverySource();
                Debug.LogWarning($"Исходный сейв сохранён перед восстановлением позиций: world={ServerWorld.worldId}, actors={recoveredActors.Count}, backup={backup}.");
                ServerWorld.revision++;Saves.SaveWorld(ServerWorld);
            }
            ownership.BeginSession(ServerWorld);
            inventory = new SharedInventory(ServerWorld,Rules.Loot);
            InvokeRepeating(nameof(CheckpointWorld),3,3);
        }

        internal void DiscardUnstartedWorld()
        {
            CancelInvoke(nameof(CheckpointWorld));ServerWorld=null;inventory=null;recoveredActors.Clear();
        }

        internal void AdmitLobbyPlayer(NetworkConnectionToClient connection,JoinMessage message)=>Join(connection,message);

        public override void OnStartClient()
        {
            CharacterContent.Reset();
            clientPoses.Clear();
            Admission.RegisterClient();
            Lobby.RegisterClient();
            Travel.RegisterClient();
            Pause.RegisterClient();Deposits.RegisterClient();
            worldReplication.Reset();
            NetworkClient.RegisterHandler<WorldPartMessage>(message=>
            {
                var received=worldReplication.Receive(message);
                if(received==null) return;
                if(received.owner!=null) Welcome(new WelcomeMessage {owner=received.owner,actor=received.actor,worldJson=received.snapshot.Json});
                else ReceiveWorld(new WorldMessage {json=received.snapshot.Json});
            });
            NetworkClient.RegisterHandler<ActorMessage>(message =>
            {
                var actor = JsonUtility.FromJson<ActorState>(message.json);
                MergeActor(actor);
            });
            Projectiles.RegisterClient();
            NetworkClient.RegisterHandler<PoseMessage>(ReceivePose);
            NetworkClient.RegisterHandler<AttackMessage>(message => AttackReceived?.Invoke(JsonUtility.FromJson<AttackEvent>(message.json)));
            NetworkClient.RegisterHandler<NpcHitMessage>(message => NpcHitReceived?.Invoke(message));
            NetworkClient.RegisterHandler<FriendlyHitMessage>(message=>FriendlyHitReceived?.Invoke(message));
            NetworkClient.RegisterHandler<NoticeMessage>(message => Notice?.Invoke(message.text));
            NetworkClient.RegisterHandler<ModMismatchMessage>(message=>ModMismatch?.Invoke(JsonUtility.FromJson<ContentManifest>(System.Text.Encoding.UTF8.GetString(message.manifest))));
            NetworkClient.RegisterHandler<HealingReplyMessage>(message=>{HealingFinished?.Invoke(message.transaction,message.success);Notice?.Invoke(message.reason);});
            ClientReady?.Invoke();
        }

        public override void OnClientConnect()
        {Admission.Begin();}

        public override void OnServerConnect(NetworkConnectionToClient connection)
        {base.OnServerConnect(connection);Admission.Connected(connection);}

        internal void ProtocolAccepted()
        {
            base.OnClientConnect();
            SendJoin(false);
        }

        public void SendJoin(bool acceptMismatch,bool acceptCharacter=false)
        {
            if(!Admission.ClientApproved) {ShowNotice("Сначала необходимо согласовать сетевой протокол.");return;}
            try
            {
                if(!CharacterContent.TryPrepare(Saves,CharacterId,acceptCharacter,out var character)) return;
                Lobby.PublishOffers();
                NetworkClient.Send(new JoinMessage { character = CharacterId, displayName = CharacterId,
                    characterJson=character==null?null:JsonUtility.ToJson(character),
                    modFingerprint=WeaponCatalog.ModFingerprint,acceptMismatch=acceptMismatch });
            }
            catch(Exception failure)
            {
                Debug.LogError("Не удалось подготовить профиль для подключения: "+failure);
                Notice?.Invoke("Не удалось подготовить профиль: "+failure.Message);
                if(NetworkServer.active) StopHost();else StopClient();
            }
        }

        private static WorldState CreateWorld()
        {
            var world = new WorldState();
            world.actors.Add(new ActorState { id = "npc:training-guard", actorType="raider", owner = "host", x = 8, health = 140 });
            var container=new ContainerState();world.containers.Add(container);
            container.items.Add(SharedInventory.Create("caps", 20));
            container.items.Add(SharedInventory.Create("heal", 3));
            container.items.Add(SharedInventory.Create("grenade", 3));
            world.props.Add(PropCatalog.Create("prop:crate-1","mcrate1","factory:0",new Vector2(-3,.8f)));
            return world;
        }

        private void Join(NetworkConnectionToClient connection, JoinMessage message)
        {
            if(!Admission.IsApproved(connection.connectionId))
            {Admission.Reject(connection,"Клиент не выполнил согласование сетевого протокола. Обновите сборку.");return;}
            if (string.IsNullOrWhiteSpace(message.character) || owners.ContainsKey(connection.connectionId))
            {
                connection.Disconnect();
                return;
            }
            if(message.modFingerprint!=WeaponCatalog.ModFingerprint && !message.acceptMismatch)
            {
                connection.Send(new ModMismatchMessage {manifest=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(WeaponCatalog.ManifestCopy()))});
                return;
            }
            if(Lobby.HoldJoin(connection,message)) return;
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
                    WeaponAmmunition.Initialize(actor,true);
                }
                actor.id="player:"+message.character;actor.character=message.character;actor.player=true;
                CharacterPlacement.SpawnInHub(actor,ServerWorld.actors.Count(a=>a.player));
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
            WorldReplication.Send(connection,PrepareRecovery(),owner,actor.id);
            if(recoveredActors.Contains(actor.id)) connection.Send(new NoticeMessage {text="Недопустимая позиция из старого сохранения восстановлена на свободной опоре. Здоровье и предметы сохранены; исходный сейв скопирован в Recovery."});
            BroadcastWorld();
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            Admission.Disconnected(connection.connectionId);
            Lobby.Disconnected(connection.connectionId);
            if (owners.Remove(connection.connectionId, out string owner))
            {
                backupRevisions.Remove(connection.connectionId);
                RecoveryPeerLeft?.Invoke(connection.connectionId);
                var actor = ServerWorld.Find(playerIds[owner]);
                ActorSessionRules.Disconnect(actor);
                if(actor.level=="hub") GrenadeRules.ForgetHubActor(ServerWorld,actor.id);
                playerIds.Remove(owner);
                ownership.Reassign(ServerWorld, owners.Values.ToArray());
                ServerWorld.revision++;
                BroadcastWorld();
                CompleteBackedUpGrants();
            }
            base.OnServerDisconnect(connection);
        }

        private void Welcome(WelcomeMessage message)
        {
            LocalOwner = message.owner;
            LocalActorId = message.actor;
            World = PersistCopy(message.worldJson);
            SaveLocalCharacter();
            Changed?.Invoke();
        }

        private void ReceiveWorld(WorldMessage message)
        {
            var incoming = PersistCopy(message.json);
            if (World != null && World.worldId == incoming.worldId)
            {
                foreach(var prop in WorldProps.All(incoming))
                    PropReplication.PreserveNewerPose(WorldProps.Find(World,prop.id),prop,LocalOwner);
                // A full backup must not rewind locally-owned pose or an unacknowledged health change.
                foreach (var actor in incoming.actors)
                {
                    var local = World.Find(actor.id);
                    if (local == null || local.owner != LocalOwner || local.generation != actor.generation ||
                        local.lifecycleSequence!=actor.lifecycleSequence || local.level!=actor.level) continue;
                    ActorPose.CopyMotion(local,actor);
                    NpcAttackRules.CopyProgress(local,actor);
                    if (local.revision > actor.revision)
                    {
                        actor.extensions=local.extensions;
                        actor.attackId=local.attackId;actor.attackTarget=local.attackTarget;actor.attackBusy=local.attackBusy;
                        actor.attackRemaining=local.attackRemaining;actor.attackCooldown=local.attackCooldown;
                        actor.health = local.health; actor.down = local.down; actor.revision = local.revision;
                        actor.mana=local.mana;actor.damageSequence=local.damageSequence;actor.resolvedExplosions=local.resolvedExplosions;
                        actor.resolvedAttacks=local.resolvedAttacks;
                        actor.magazines=local.magazines;
                        if(!InventoryJournal.Merge(actor,local,out var inventoryCopy,out var uses))
                            throw new InvalidOperationException($"Не удалось согласовать расход инвентаря: actor={actor.id}, revision={actor.revision}.");
                        actor.inventory=inventoryCopy;actor.inventoryUses=uses;
                    }
                }
            }
            World = incoming;
            SaveLocalCharacter();
            Changed?.Invoke();
        }

        private WorldState PersistCopy(string json)
        {
            var snapshot=new RecoverySnapshot(json);
            // The host already persisted this payload before sending it. Its local client
            // must not overwrite newer canonical state while processing queued messages.
            if(!NetworkServer.active) Saves.SaveRecovery(snapshot);
            if(NetworkClient.isConnected) NetworkClient.Send(new BackupAckMessage {
                world=snapshot.WorldId,revision=snapshot.Revision,snapshot=snapshot.Id });
            return snapshot.CreateLiveWorld();
        }

        public void SaveLocalCharacter()
        {
            if(LocalActor!=null) Saves.SaveCharacter(LocalActor);
        }

        private RecoverySnapshot PrepareRecovery()
        {
            var snapshot=Saves.SaveWorld(ServerWorld);
            recoveryReceipts.Register(snapshot);
            RecoveryPrepared?.Invoke(snapshot.Id,snapshot.Revision);
            return snapshot;
        }

        private void MergeActor(ActorState actor)
        {
            if (World == null) return;
            var current = World.Find(actor.id);
            if (current != null && ActorVersion.IsOlder(actor,current)) return;
            if(current!=null && current.owner==LocalOwner && current.generation==actor.generation &&
                current.lifecycleSequence==actor.lifecycleSequence && current.level==actor.level)
            {
                ActorPose.CopyMotion(current,actor);
                NpcAttackRules.CopyProgress(current,actor);
            }
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
            if(!InventoryJournal.Merge(actor,incoming,out var inventoryCopy,out var uses))
            {
                Debug.LogWarning($"Отклонён несогласованный расход инвентаря: actor={actor.id}, revision={incoming.revision}.");
                connection.Send(new NoticeMessage {text="Расход предметов не подтверждён: состояние инвентаря не совпало. Переподключитесь к сессии."});
                return;
            }
            incoming.inventory=inventoryCopy;incoming.inventoryUses=uses;
            incoming.owner = actor.owner;
            incoming.connected = actor.connected;
            if(incoming.level!=actor.level)
            {
                Debug.LogWarning($"Отклонена смена локации через состояние: actor={actor.id}, source={actor.level}, destination={incoming.level}. Требуется запрос перехода.");
                connection.Send(new NoticeMessage { text="Смена локации требует подтверждённого перехода." });
                return;
            }
            ServerWorld.actors[ServerWorld.actors.IndexOf(actor)] = incoming;
            if(!actor.player && actor.attackBusy && !incoming.attackBusy)
            {CommitWorld();return;}
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
                actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence ||
                !serverPoses.Accept(message.actor,message.generation,message.lifecycleSequence,message.sequence)) return;
            ApplyPose(actor, message);
            NetworkServer.SendToAll(message, Channels.Unreliable);
        }

        private void ReceivePose(PoseMessage message)
        {
            var actor = World?.Find(message.actor);
            if (actor == null || actor.owner == LocalOwner || actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence) return;
            if (!clientPoses.Accept(message.actor,message.generation,message.lifecycleSequence,message.sequence)) return;
            ApplyPose(actor, message);
        }

        private static void ApplyPose(ActorState actor, PoseMessage message)
        {
            actor.x = message.x; actor.y = message.y; actor.vx = message.vx; actor.vy = message.vy;
            actor.facing = message.facing; actor.pose = message.pose;
            actor.poseTime=message.poseTime;
            actor.climbing=message.climbing;
            actor.jumpSupportRemaining=message.jumpSupportRemaining;
            actor.crouched=message.crouched;
            actor.weapon=message.weapon;actor.weaponAngle=message.weaponAngle;
            actor.weaponTipX=message.weaponTipX;actor.weaponTipY=message.weaponTipY;
            actor.weaponReach=message.weaponReach;
            actor.weaponFlipY=message.weaponFlipY;
            if(actor.attackId==message.attackId && actor.attackBusy==message.attackBusy)
            {
                actor.attackRemaining=message.attackRemaining;actor.attackCooldown=message.attackCooldown;
            }
        }

        public void PublishPose(ActorState actor, uint sequence) => NetworkClient.Send(new PoseMessage
        {
            actor = actor.id, generation = actor.generation,lifecycleSequence=actor.lifecycleSequence, sequence = sequence,
            x = actor.x, y = actor.y, vx = actor.vx, vy = actor.vy, facing = actor.facing, pose = actor.pose,poseTime=actor.poseTime,climbing=actor.climbing,crouched=actor.crouched,jumpSupportRemaining=actor.jumpSupportRemaining,
            weapon=actor.weapon,weaponAngle=actor.weaponAngle,weaponTipX=actor.weaponTipX,weaponTipY=actor.weaponTipY,weaponReach=actor.weaponReach,weaponFlipY=actor.weaponFlipY,
            attackId=actor.attackId,attackBusy=actor.attackBusy,attackRemaining=actor.attackRemaining,attackCooldown=actor.attackCooldown
        }, Channels.Unreliable);

        public void Fire(AttackEvent attack) => NetworkClient.Send(new AttackMessage { json = JsonUtility.ToJson(attack) });
        private void RelayAttack(NetworkConnectionToClient connection, AttackMessage message)
        {
            var attack = JsonUtility.FromJson<AttackEvent>(message.json);
            if(attack.weapon=="grenade") {Debug.LogWarning($"Взрыв без сохраняемой сущности отклонён: {attack.id}.");return;}
            if (!owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.Find(attack.attacker)?.owner != owner) return;
            if(string.IsNullOrEmpty(attack.level) || (attack.weapon!="grenade" && attack.level!=ServerWorld.Find(attack.attacker).level))
            { Debug.LogWarning($"Атака без корректной локации отклонена: id={attack.id}, actor={attack.attacker}, level={attack.level}.");return; }
            attack.sourceOwner=owner;attack.sourcePlayer=ServerWorld.Find(attack.attacker).player;
            attack.friendlyFireDecision=ServerWorld.friendlyFireDecision;
            float projectileSpeed=WeaponCatalog.Get(attack.weapon).projectileSpeed;
            if(projectileSpeed>0 && !Projectiles.Launch(attack)) return;
            NetworkServer.SendToAll(new AttackMessage {json=JsonUtility.ToJson(attack)});
        }

        public void ReportNpcHit(NpcHitMessage message) => NetworkClient.Send(message);
        public void ReportFriendlyHit(FriendlyHitMessage message)=>NetworkClient.Send(message);
        private void RelayFriendlyHit(NetworkConnectionToClient connection,FriendlyHitMessage message)
        {
            var attack=JsonUtility.FromJson<AttackEvent>(message.attackJson);
            var target=ServerWorld.Find(message.target);
            var source=ServerWorld.Find(attack.attacker);
            if(target==null || target.generation!=message.generation || target.lifecycleSequence!=message.lifecycle ||
                !owners.TryGetValue(connection.connectionId,out var owner) ||
                !(WeaponCatalog.Get(attack.weapon).projectileSpeed>0?Projectiles.IsDecisionOwner(owner,attack):source!=null && source.player && source.owner==owner && attack.sourceOwner==owner) ||
                !FriendlyFireDecision.UsesAttacker(attack,target) || !AttackScope.CanAffect(attack,target,ServerWorld.friendlyFire)) return;
            foreach(var pair in owners)
                if(pair.Value==target.owner && NetworkServer.connections.TryGetValue(pair.Key,out var destination))
                {destination.Send(message);return;}
        }
        private void RelayNpcHit(NetworkConnectionToClient connection, NpcHitMessage message)
        {
            var attack = JsonUtility.FromJson<AttackEvent>(message.attackJson);
            var target = ServerWorld.Find(message.target);
            if (target == null || target.player || string.IsNullOrEmpty(attack?.id) || !AttackScope.Includes(attack,target) ||
                !owners.TryGetValue(connection.connectionId, out string owner) || !(WeaponCatalog.Get(attack.weapon).projectileSpeed>0?Projectiles.IsDecisionOwner(owner,attack):ServerWorld.Find(attack.attacker)?.owner==owner)) return;
            npcHits.Accept(message,attack);
        }

        public void RequestClaim(string container,string item,int quantity=0) => NetworkClient.Send(new ClaimMessage { transaction = Guid.NewGuid().ToString("N"),container=container, item = item,quantity=quantity });
        public string RequestRevive(string target)=>RequestHealing(target,false);
        public void RequestSelfHeal()=>RequestHealing(LocalActorId,true);
        private string RequestHealing(string targetId,bool selfHeal)
        {
            var healer=LocalActor;var target=World?.Find(targetId);
            if(healer==null || target==null) return null;
            string transaction=Guid.NewGuid().ToString("N");
            NetworkClient.Send(new ReviveMessage {target=targetId,transaction=transaction,selfHeal=selfHeal,
                healerGeneration=healer.generation,healerLifecycle=healer.lifecycleSequence,damageSequence=healer.damageSequence,
                targetGeneration=target.generation,targetLifecycle=target.lifecycleSequence});
            return transaction;
        }
        public void ReturnToFactory() => Travel.RequestFactory();

        private void Revive(NetworkConnectionToClient connection, ReviveMessage message)
        {
            if (!owners.TryGetValue(connection.connectionId, out string owner)) return;
            if(ServerWorld.completedTransactions.Contains(message.transaction))
            {connection.Send(new HealingReplyMessage {transaction=message.transaction,success=true,reason="Лечение уже подтверждено."});return;}
            var healer = ServerWorld.Find(playerIds[owner]);
            if(Pause.IsServerPaused(healer.level))
            {connection.Send(new HealingReplyMessage {transaction=message.transaction,success=false,reason="Лечение отменено: уровень остановлен. Предмет не потрачен."});return;}
            var target = ServerWorld.Find(message.target);
            bool current=target!=null && healer.generation==message.healerGeneration && healer.lifecycleSequence==message.healerLifecycle &&
                healer.damageSequence==message.damageSequence && target.generation==message.targetGeneration && target.lifecycleSequence==message.targetLifecycle;
            if(!current || !HealingRules.Apply(healer,target,message.selfHeal))
            {connection.Send(new HealingReplyMessage {transaction=message.transaction,success=false,reason="Лечение отменено: изменилось состояние, расстояние или нет лечебного предмета."});return;}
            ServerWorld.completedTransactions.Add(message.transaction);
            ServerWorld.revision++;
            BroadcastWorld();
            connection.Send(new HealingReplyMessage {transaction=message.transaction,success=true,
                reason=message.selfHeal?"Лечебный предмет использован.":"Союзник поднят. Лечебный предмет использован."});
        }

        private void Claim(NetworkConnectionToClient connection, ClaimMessage message)
        {
            if (!owners.TryGetValue(connection.connectionId, out string owner)) return;
            var actor = ServerWorld.Find(playerIds[owner]);
            bool success = inventory.PrepareClaim(message.transaction,message.container, message.item, actor,out string reason,message.quantity);
            connection.Send(new NoticeMessage { text = success ? "Подбор подтверждается копиями мира…" : reason });
            if (success) { BroadcastWorld(); CompleteBackedUpGrants(); }
        }

        internal bool HasInventoryBackups(long revision)=>!owners.Keys.Any(id=>!backupRevisions.TryGetValue(id,out long rev) || rev<revision);
        internal void CompleteInventoryTransactions()=>CompleteBackedUpGrants();
        private void CompleteBackedUpGrants()
        {
            bool changed=false;
            var recipients=new HashSet<string>();
            foreach(var grant in ServerWorld.pendingGrants.ToArray())
            {
                // Every currently connected peer must durably write the reservation first.
                if(!HasInventoryBackups(grant.preparedRevision)) continue;
                inventory.CommitGrant(grant);
                recipients.Add(grant.recipient);
                changed=true;
            }
            changed|=Deposits.CompleteBackedUp();
            if(changed) { BroadcastWorld(); }
            foreach(var connection in NetworkServer.connections.Values)
            {
                var actor=ConnectedActor(connection);
                if(actor!=null && recipients.Contains(actor.id) && !ServerWorld.pendingGrants.Any(grant=>grant.recipient==actor.id))
                    connection.Send(new NoticeMessage {text="Подбор завершён: предметы в личном инвентаре."});
            }
        }

        private void BroadcastWorld()
        {
            Pause.Publish();
            var snapshot=PrepareRecovery();
            foreach(int id in owners.Keys)
                if(NetworkServer.connections.TryGetValue(id,out var connection)) WorldReplication.Send(connection,snapshot);
        }
        public ActorState ConnectedActor(NetworkConnectionToClient connection) =>
            owners.TryGetValue(connection.connectionId,out var owner) && playerIds.TryGetValue(owner,out var id)?ServerWorld.Find(id):null;
        public void CommitWorld()
        {
            ownership.Reassign(ServerWorld,owners.Values.ToArray());
            ServerWorld.revision++;
            BroadcastWorld();
        }

        private void CheckpointWorld()
        {
            if (!NetworkServer.active || ServerWorld == null) return;
            ownership.Reassign(ServerWorld, owners.Values.ToArray());
            if(PartyRules.ResolveDefeat(ServerWorld,Rules.PartyDefeat))
                NetworkServer.SendToAll(new NoticeMessage {text=Rules.PartyDefeat.Notice});
            ServerWorld.revision++;
            BroadcastWorld();
        }

        public override void OnStopServer()
        {
            if(npcHits!=null) {Destroy(npcHits);npcHits=null;}
            Pause.ResetServer();
            CancelInvoke(nameof(CheckpointWorld));
            if (ServerWorld != null) Saves.SaveWorld(ServerWorld);
        }
        public override void OnStopClient()
        {
            CharacterContent.Reset();ModMismatch?.Invoke(null);
            Admission.EndClient();
            Travel.ResetClient();Lobby.ResetClient();worldReplication.Reset();
            SaveLocalCharacter();
            World=null;LocalActorId=null;LocalOwner=null;
            Pause.ResetClient();
            Changed?.Invoke();
        }
        public void SetFriendlyFire(bool enabled)
        {
            if(!NetworkServer.active) return;
            ServerWorld.friendlyFire=enabled;CommitWorld();
        }
        public void SetFriendlyFireDecision(string policy)
        {
            if(!NetworkServer.active || ServerWorld==null) return;
            FriendlyFireDecision.Validate(policy);ServerWorld.friendlyFireDecision=policy;CommitWorld();
        }
    }
}
