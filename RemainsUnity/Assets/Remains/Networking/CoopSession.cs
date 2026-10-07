using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;
using Remains.Content;
using Remains.Diagnostics;

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
        private readonly CharacterPlayTime playTime=new(()=>Time.unscaledTimeAsDouble);
        public SaveStore Saves { get; set; }
        public CharacterContentGate CharacterContent {get;}=new();
        private GameplayRuleSet rules;
        public GameplayRuleSet Rules=>rules??throw new InvalidOperationException("Правила сессии ещё не настроены.");
        public event Action<Exception> GameplayPreparationFailed;
        public bool GameplayFailurePending {get;private set;}
        public void ConfigureRules(GameplayRuleSet rules)
        {
            if(NetworkServer.active || NetworkClient.active) throw new InvalidOperationException("Нельзя менять правила во время подключения или игры.");
            this.rules=rules??throw new ArgumentNullException(nameof(rules));
            GameplayFailurePending=false;
            if(recoil==null) recoil=new SessionWeaponRecoil(this);else recoil.Configure();
            ownership=new OwnershipService(rules.ProximityOwnership);
        }
        public string CharacterId { get; set; }
        private CharacterJoinSelection joinSelection;
        private readonly OrderedSaveWrites saveWrites=new();
        private bool savingFailed;
        private Coroutine stopNotification;
        public bool CharacterSelectionLocked=>joinSelection!=null || NetworkClient.active || NetworkServer.active;
        public void BeginJoinSelection()
        {
            CancelStopNotification();
            if(joinSelection!=null) {joinSelection.RequireCurrent(CharacterId,Saves);return;}
            joinSelection=new CharacterJoinSelection(CharacterId,Saves);
            Debug.Log($"Персонаж зафиксирован для подключения: character={CharacterId}, profile={Saves.CharacterDirectoryPath}.");
        }
        public void CancelJoinSelection()
        {if(!NetworkClient.active && !NetworkServer.active) {joinSelection=null;Lobby.CancelConfiguredStartup();}}
        public void PumpSaveWrites()=>saveWrites.Pump();
        public void FlushSaveWrites()
        {
            try {saveWrites.Flush();}
            catch(Exception error) {Debug.LogError("Очередь сохранений не завершена: "+error);ShowNotice("Не удалось завершить запись сохранения. Подробности — в журнале игры.");}
        }
        private void SaveWriteFailed(Exception error)
        {
            Debug.LogError("Не удалось записать состояние сессии: "+error);
            ShowNotice("Ошибка записи сохранения. Сессия остановлена; подробности — в журнале игры.");
            if(savingFailed) return;
            savingFailed=true;
            if(NetworkServer.active) StopHost();else if(NetworkClient.active) StopClient();
        }
        public event Action Changed;
        public event Action SessionStopped;
        public event Action<string,string,float,float> RemotePoseCorrection;
        private void ObservePoseCorrection(string source,ActorState current,string nextPose,float nextTime)
        {
            if(current!=null && current.owner!=LocalOwner && current.pose=="run" && nextPose=="run" && nextTime+.0001f<current.poseTime)
                RemotePoseCorrection?.Invoke(source,current.id,current.poseTime,nextTime);
        }
        public event Action<AttackEvent> AttackReceived;
        public event Action<ActorState> PoseReceived;
        public event Action<string> Notice;
        public event Action<NpcHitMessage> NpcHitReceived;
        public event Action<string,NpcHitMessage,ActorState> NpcHitTrace;
        internal void TraceNpcHit(string stage,NpcHitMessage message,bool server=false)
            =>NpcHitTrace?.Invoke(stage,message,(server?ServerWorld:World)?.Find(message.target));
        public event Action<FriendlyHitMessage> FriendlyHitReceived;
        public event Action ServerReady;
        public event Action ClientReady;
        public event Action<ContentManifest> ModMismatch;
        public event Action<string,bool> HealingFinished;
        public event Action<string> SelfHealingRequested;
        private readonly SelfHealingReconciliation selfHealing=new(message=>Debug.LogWarning(message));
        public event Action<string,bool> ClaimPrepared;
        public event Action<string,long> RecoveryPrepared;
        public event Action<RecoverySnapshot> RecoveryPersisted;
        public event Action<CharacterPlayTime.CapturedCheckpoint> PlayTimePersisted;
        public event Action<int,string,long> RecoveryAcknowledged;
        public event Action<int> RecoveryPeerLeft;
        private readonly Dictionary<int, string> owners = new();
        private readonly Dictionary<string, string> playerIds = new();
        private readonly PoseSequenceGate serverPoses = new();
        private readonly PoseSequenceGate clientPoses = new();
        private NpcHitDelivery npcHits;
        public bool UseLobby {get;set;}
        public void ConfigureHostStartup(SessionStartupIntent intent)
        {
            Lobby.ConfigureStartup(intent);
            UseLobby=true;
        }
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
        private SessionWeaponRecoil recoil;
        public SessionWeaponRecoil Recoil=>recoil??=new SessionWeaponRecoil(this);
        private SessionReloadAudio reloadAudio;
        public SessionReloadAudio ReloadAudio=>reloadAudio ??= new SessionReloadAudio(this);
        private SessionDoorDamage doorDamage;
        public SessionDoorDamage DoorDamage=>doorDamage ??= new SessionDoorDamage(this);
        private SessionDoors doors;
        public SessionDoors Doors=>doors ??= new SessionDoors(this);
        private SessionContainerInspection containerInspection;
        public SessionContainerInspection ContainerInspection=>containerInspection ??= new SessionContainerInspection(this);
        private SessionActorHolds actorHolds;
        public SessionActorHolds ActorHolds=>actorHolds ??= new SessionActorHolds(this);
        private SessionPause pause;
        public SessionPause Pause=>pause ??= new SessionPause(this);
        private SessionLevelTime levelTime;
        public SessionLevelTime LevelTime=>levelTime ??= new SessionLevelTime(this);
        private SessionMechanisms mechanisms;
        public SessionMechanisms Mechanisms=>mechanisms ??= new SessionMechanisms(this);
        public void ShowNotice(string text) => Notice?.Invoke(text);
        private readonly Dictionary<int,long> backupRevisions = new();
        private long canonicalPersistedRevision=-1;
        private readonly RecoveryReceiptGate recoveryReceipts = new();
        private readonly WorldReplication worldReplication=new();
        private OwnershipService ownership = new();
        private SharedInventory inventory;
        private readonly HashSet<string> recoveredActors=new();

        public override void OnStartServer()
        {
            CancelStopNotification();
            savingFailed=false;
            BeginJoinSelection();
            canonicalPersistedRevision=-1;
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
            ContainerInspection.RegisterServer();
            Mechanisms.RegisterServer();
            DoorDamage.RegisterServer();Doors.RegisterServer();ActorHolds.RegisterServer();Pause.RegisterServer();Deposits.RegisterServer();Projectiles.RegisterServer();ReloadAudio.RegisterServer();
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
                    throw new InvalidOperationException("Состав контента сохранения отличается или неизвестен. Подтвердите сравнение перед загрузкой выбранного мира.");
                if(content!=null && !content.IsSupported)
                    throw new InvalidOperationException($"Формат манифеста не поддерживается: schema={content.schema}, scope={content.scope}. Исходный мир сохранён.");
            }
            if(snapshot!=null) Saves.SelectContinuation(snapshot);
            ServerWorld = createNew?CreateWorld():Saves.LoadWorld() ?? CreateWorld();
            DoorDamage.ResetServer();
            ContainerAppearancePersistence.Prepare(ServerWorld,source==null?()=>{}:
                ()=>Debug.Log("Исходный мир сохранён перед миграцией вида контейнеров: "+Saves.PreserveRecoverySource()));
            ActorControlMigration.Prepare(ServerWorld,Rules.Knockdown,Saves.PreserveRecoverySource);
            MechanismPersistence.Prepare(ServerWorld,source==null?()=>{}:
                ()=>Debug.Log("Исходный мир сохранён перед добавлением механизмов: "+Saves.PreserveRecoverySource()));
            npcHits?.Reset();
            var contentLevels=ServerWorld.actors.Select(a=>a.level).Concat(ServerWorld.containers.Select(c=>c.level))
                .Concat(ServerWorld.props.Select(p=>p.level)).Concat(ServerWorld.doors.Select(d=>d.level)).Append("hub").Distinct().ToArray();
            var roomContent=contentLevels.Select(level=>RoomContentBatch.Read(RoomAuthoring.Load(level,ServerWorld),level)).ToArray();
            if(HubTrainingContent.NeedsInstall(ServerWorld) || FactoryRaiderContent.NeedsInstall(ServerWorld) ||
                roomContent.Any(batch=>RoomContentInstaller.NeedsInstall(ServerWorld,batch)))
            {
                if(source!=null) Debug.Log("Сохранена копия мира перед обновлением базового контента: "+Saves.PreserveRecoverySource());
                HubTrainingContent.Install(ServerWorld);FactoryRaiderContent.Install(ServerWorld);
                foreach(var batch in roomContent) RoomContentInstaller.Apply(ServerWorld,batch);
            }
            FriendlyFireDecision.Validate(ServerWorld.friendlyFireDecision);
            NpcWeaponInventoryMigration.Apply(ServerWorld,source==null?null:()=>Debug.Log("Сохранена копия мира перед преобразованием оружия NPC: "+Saves.PreserveRecoverySource()));
            NpcWeaponActions.ValidateWorld(ServerWorld);
            WorldHistory.BeginContinuation(ServerWorld);
            LevelTimeMigration.Prepare(ServerWorld,contentLevels.Concat(ServerWorld.rooms.Select(room=>room.level)),source==null?null:
                ()=>Debug.Log("Исходный мир сохранён перед установкой часов уровней: "+Saves.PreserveRecoverySource()));
            LevelTime.BeginServer();
            Mechanisms.ReconcileServer();
            GrenadeRules.BeginSession(ServerWorld);
            recoveredActors.Clear();
            foreach(var actor in ServerWorld.actors)
            {
                var room=RoomLayout.Load(actor.level,ServerWorld);
                Remains.Gameplay.PlatformSimulation.RestoreSupport(ServerWorld,room,actor.level,LevelTime.ReadServer(actor.level));
                if(SavedActorPlacement.Repair(actor,room,"загрузка мира "+ServerWorld.worldId)) recoveredActors.Add(actor.id);
            }
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
            DoorDamage.ResetServer();CancelInvoke(nameof(CheckpointWorld));LevelTime.ResetServer();Mechanisms.ResetServer();ServerWorld=null;inventory=null;recoveredActors.Clear();
        }

        internal void AdmitLobbyPlayer(NetworkConnectionToClient connection,JoinMessage message)=>Join(connection,message);

        public override void OnStartClient()
        {
            GameplayFailurePending=false;
            CancelStopNotification();
            savingFailed=false;
            BeginJoinSelection();
            CharacterContent.Reset();
            clientPoses.Clear();
            Admission.RegisterClient();
            Lobby.RegisterClient();
            Travel.RegisterClient();
            ContainerInspection.RegisterClient();
            Mechanisms.RegisterClient();
            DoorDamage.RegisterClient();Doors.RegisterClient();ActorHolds.RegisterClient();Pause.RegisterClient();LevelTime.RegisterClient();Deposits.RegisterClient();ReloadAudio.RegisterClient();
            worldReplication.Reset();
            NetworkClient.RegisterHandler<WorldPartMessage>(message=>
            {
                var received=worldReplication.Receive(message);
                if(received==null) return;
                if(received.owner!=null) Welcome(new WelcomeMessage {owner=received.owner,actor=received.actor,worldJson=received.snapshot.Json},received.snapshot);
                else ReceiveWorld(new WorldMessage {json=received.snapshot.Json},received.snapshot);
            });
            NetworkClient.RegisterHandler<ActorMessage>(message =>
            {
                var actor = JsonUtility.FromJson<ActorState>(message.json);
                MergeActor(actor);
            });
            Projectiles.RegisterClient();
            NetworkClient.RegisterHandler<PoseMessage>(ReceivePose);
            NetworkClient.RegisterHandler<AttackMessage>(message =>
            {
                var attack=JsonUtility.FromJson<AttackEvent>(message.json);
                if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.Get(attack.weapon).projectileSpeed<=0 || Projectiles.AcceptEcho(attack.id)) AttackReceived?.Invoke(attack);
            });
            NetworkClient.RegisterHandler<NpcHitMessage>(message =>
            {TraceNpcHit("npc-hit-received",message);NpcHitReceived?.Invoke(message);});
            NetworkClient.RegisterHandler<FriendlyHitMessage>(message=>FriendlyHitReceived?.Invoke(message));
            NetworkClient.RegisterHandler<NoticeMessage>(message => Notice?.Invoke(message.text));
            NetworkClient.RegisterHandler<ClaimReplyMessage>(message=>{ClaimPrepared?.Invoke(message.transaction,message.accepted);Notice?.Invoke(message.reason);});
            NetworkClient.RegisterHandler<ModMismatchMessage>(message=>ModMismatch?.Invoke(JsonUtility.FromJson<ContentManifest>(System.Text.Encoding.UTF8.GetString(message.manifest))));
            NetworkClient.RegisterHandler<HealingReplyMessage>(message=>{if(!message.success) selfHealing.Reject(message.transaction);HealingFinished?.Invoke(message.transaction,message.success);Notice?.Invoke(message.reason);});
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
                BeginJoinSelection();
                joinSelection.RequireCurrent(CharacterId,Saves);
                if(!CharacterContent.TryPrepare(joinSelection.Saves,joinSelection.CharacterId,acceptCharacter,out var character)) return;
                Lobby.PublishOffers();
                NetworkClient.Send(new JoinMessage { character = joinSelection.CharacterId, displayName = joinSelection.CharacterId,
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

        private static WorldState CreateWorld()=>NewGameWorld.Create();

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
                Admission.RejectDuplicateCharacter(connection,message.character);
                return;
            }
            string owner = "connection:" + connection.connectionId;
            string entryNotice=null;
            if(actor!=null)
            {
                try
                {
                    var profile=string.IsNullOrEmpty(message.characterJson)?null:JsonUtility.FromJson<ActorState>(message.characterJson);
                    PonyAppearance.ValidateSettings(profile?.appearance,message.character);
                    var entered=PlayerWorldEntry.Reconcile(ServerWorld,actor,profile,out entryNotice);
                    if(!ReferenceEquals(entered,actor)) {ServerWorld.actors[ServerWorld.actors.IndexOf(actor)]=entered;actor=entered;}
                }
                catch(Exception error) when(error is InvalidOperationException || error is System.IO.InvalidDataException)
                {
                    Debug.LogWarning($"Вход персонажа отменён: world={ServerWorld.worldId}, character={message.character}; {error.Message}");
                    Admission.Reject(connection,error.Message);return;
                }
            }
            if (actor == null)
            {
                actor=string.IsNullOrEmpty(message.characterJson)?null:JsonUtility.FromJson<ActorState>(message.characterJson);
                if(actor!=null && actor.character!=message.character)
                    throw new InvalidOperationException($"Персонаж в пакете не соответствует ID: {actor.character}, ожидается {message.character}.");
                PonyAppearance.ValidateSettings(actor?.appearance,message.character);
                if(actor==null)
                {
                    actor=new ActorState();
                    actor.character=message.character;
                    actor.hair=PonyPaletteAsset.Load().ForNewCharacter(ServerWorld.actors);
                    PlayerStarterInventory.Give(actor);
                }
                actor.id="player:"+message.character;actor.character=message.character;actor.player=true;
                actor=WeaponInventoryMigration.Prepare(actor);
                CharacterPlacement.SpawnInHub(actor,ServerWorld.actors.Count(a=>a.player));
                ServerWorld.actors.Add(actor);
            }
            owners[connection.connectionId] = owner;
            ActorSessionRules.Connect(actor,owner);
            playerIds[owner] = actor.id;
            ownership.Reassign(ServerWorld, owners.Values.ToArray());
            ServerWorld.revision++;
            WorldReplication.Send(connection,PrepareRecovery(),owner,actor.id);
            if(entryNotice!=null) {Debug.LogWarning($"world={ServerWorld.worldId}, actor={actor.id}: {entryNotice}");connection.Send(new NoticeMessage {text=entryNotice});}
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

        private void Welcome(WelcomeMessage message,RecoverySnapshot verified=null)
        {
            var snapshot=verified??new RecoverySnapshot(message.worldJson);
            var welcomed=snapshot.CreateLiveWorld();
            try
            {
                BeginJoinSelection();
                joinSelection.RequireCurrent(CharacterId,Saves);
                joinSelection.RequireWelcome(welcomed,message.actor,message.owner);
            }
            catch(Exception error)
            {
                Debug.LogError("Ответ подключения отклонён до записи профиля: "+error);
                ShowNotice("Сервер вернул другого персонажа. Подключение отменено; профиль сохранён.");
                if(NetworkServer.active) StopHost();else StopClient();
                return;
            }
            LocalOwner = message.owner;
            LocalActorId = message.actor;
            WeaponRecoilReplication.NeutralizeWorld(welcomed,Pause.Revision);
            World = PersistCopy(snapshot,welcomed);
            Debug.Log($"Подключён выбранный персонаж: character={LocalActor.character}, actor={LocalActorId}, profile={Saves.CharacterDirectoryPath}.");
            if(LocalActor!=null && LocalActor.level!="hub")
            {
                string backup=Saves.PreserveCharacterSource();
                if(backup!=null) Debug.LogWarning($"Личный профиль сохранён перед возвращением в тело: world={World.worldId}, actor={LocalActorId}, backup={backup}. Последствия оставленного тела принадлежат миру; независимые состояния персонажа не объединяются автоматически.");
            }
            QueueRecoveryReceipt(snapshot);
            Changed?.Invoke();
        }

        private void ReceiveWorld(WorldMessage message,RecoverySnapshot verified=null)
        {
            using var timing=TransitionTiming.Measure("snapshot.apply",LocalActor?.level);
            var snapshot=verified??new RecoverySnapshot(message.json);
            var incoming = PersistCopy(snapshot);
            bool sameWorld=World!=null && World.worldId==incoming.worldId;
            // Validate before motion/selection helpers can replace an incoming sample.
            foreach(var actor in incoming.actors)
                WeaponRecoilReplication.Normalize(actor,Pause.Revision,Pause.IsPaused(actor.level),"снимок мира / входящий");
            if(!sameWorld) WeaponRecoilReplication.NeutralizeWorld(incoming,Pause.Revision);
            bool publishHealing=false;
            if (World != null && World.worldId == incoming.worldId)
            {
                foreach(var prop in WorldProps.All(incoming))
                    PropReplication.PreserveNewerPose(WorldProps.Find(World,prop.id),prop,LocalOwner);
                // A full backup must not rewind locally-owned pose or an unacknowledged health change.
                for(int actorIndex=0;actorIndex<incoming.actors.Count;actorIndex++)
                {
                    var actor=incoming.actors[actorIndex];
                    var local = World.Find(actor.id);
                    if(selfHealing.DiscardPreHealingState(incoming.worldId,local,actor,LocalOwner))
                    {incoming.actors[actorIndex]=local.Copy();continue;}
                    ActorHoldRules.PreserveNewerTarget(local,actor);
                    selfHealing.Observe(World.worldId,local);
                    if(selfHealing.TryApply(incoming.worldId,incoming.completedTransactions,local,ref actor,LocalOwner))
                    {PreserveOwnerInventory(actor,local);WeaponInventory.PreserveSelection(local,actor);incoming.actors[actorIndex]=actor;publishHealing|=actor.id==LocalActorId;continue;}
                    ActorMotionReplication.PreserveNewerMotion(local,actor);
                    if(actor.owner!=LocalOwner) RemotePosePhase.Reconcile(local,actor);
                    ObservePoseCorrection("world",local,actor.pose,actor.poseTime);
                    if (local == null || local.owner != LocalOwner || local.generation != actor.generation ||
                        local.lifecycleSequence!=actor.lifecycleSequence || local.level!=actor.level) continue;
                    ActorPose.CopyMotion(local,actor);
                    NpcPerception.CopyState(local,actor);
                    NpcAttackRules.CopyProgress(local,actor);
                    if (local.revision > actor.revision)
                    {
                        actor.extensions=local.extensions;
                        actor.attackId=local.attackId;actor.attackTarget=local.attackTarget;actor.attackBusy=local.attackBusy;
                        actor.attackRemaining=local.attackRemaining;actor.attackCooldown=local.attackCooldown;
                        actor.npcWeaponAction=Remains.Serialization.StateJson.Copy(local.npcWeaponAction);
                        actor.health = local.health; actor.down = local.down; actor.revision = local.revision;
                        actor.mana=local.mana;actor.damageSequence=local.damageSequence;actor.resolvedExplosions=local.resolvedExplosions;
                        actor.resolvedAttacks=local.resolvedAttacks;
                        actor.magazines=local.magazines;
                    }
                    PreserveOwnerInventory(actor,local);
                    WeaponInventory.PreserveSelection(local,actor);
                }
            }
            foreach(var actor in incoming.actors)
                WeaponRecoilReplication.Reconcile(sameWorld?World.Find(actor.id):null,actor,LocalOwner,Pause.Revision,
                    Pause.IsPaused(actor.level),"снимок мира / публикация");
            if(World?.worldId!=incoming.worldId) selfHealing.Reset();
            World = incoming;
            QueueRecoveryReceipt(snapshot);
            if(publishHealing && LocalActor!=null) PublishActor(LocalActor);else Changed?.Invoke();
        }

        private WorldState PersistCopy(RecoverySnapshot snapshot,WorldState parsed=null)
        {
            long started=BeginRecoveryMeasurement();
            var world=parsed??snapshot.CreateLiveWorld();
            FinishRecoveryMeasurement("recovery-received",started,snapshot.Json.Length);
            return world;
        }

        private void QueueRecoveryReceipt(RecoverySnapshot snapshot)
        {
            using var timing=TransitionTiming.Measure("snapshot.receipt",snapshot.WorldId);
            var actor=CaptureLocalCharacter();
            if(actor==null) throw new InvalidOperationException($"Нет личного персонажа для подтверждения сохранения: world={snapshot.WorldId}, actor={LocalActorId}.");
            var store=Saves;bool localHost=NetworkServer.active;
            var timeCheckpoint=playTime.CaptureCheckpoint(store.CharacterDirectoryPath);
            // One durable intent repairs either inter-file crash window. The host's
            // canonical archive is queued first; its local replica must not rewind the head.
            saveWrites.Enqueue(()=>{store.SaveRecoveryWithCharacter(snapshot,actor,!localHost);timeCheckpoint.Persist();},()=>
            {
                PlayTimePersisted?.Invoke(timeCheckpoint);
                if(!localHost) RecoveryPersisted?.Invoke(snapshot);
                if(NetworkClient.isConnected && World?.worldId==snapshot.WorldId)
                    NetworkClient.Send(new BackupAckMessage {world=snapshot.WorldId,revision=snapshot.Revision,snapshot=snapshot.Id});
            },SaveWriteFailed);
        }

        private ActorState CaptureLocalCharacter()
        {
            using var timing=TransitionTiming.Measure("character.capture",CharacterId);
            var actor=LocalActor;if(actor==null) return null;
            joinSelection?.RequireCurrent(CharacterId,Saves);
            if(actor.character!=CharacterId) throw new InvalidOperationException($"Запись чужого персонажа запрещена: выбран={CharacterId}, actor={actor.id}, character={actor.character}, profile={Saves.CharacterDirectoryPath}.");
            return actor.Copy();
        }

        public void SaveLocalCharacter()
        {
            var actor=CaptureLocalCharacter();
            if(actor!=null)
            {
                var store=Saves;
                var timeCheckpoint=playTime.CaptureCheckpoint(store.CharacterDirectoryPath);
                saveWrites.Enqueue(()=>{store.SaveCharacter(actor);timeCheckpoint.Persist();},
                    ()=>PlayTimePersisted?.Invoke(timeCheckpoint),SaveWriteFailed);
            }
        }

        public event Action<string,double,int> RecoveryMeasured;
        private long BeginRecoveryMeasurement()=>RecoveryMeasured==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
        private void FinishRecoveryMeasurement(string stage,long started,int characters)
        {
            if(started!=0) RecoveryMeasured?.Invoke(stage,
                (System.Diagnostics.Stopwatch.GetTimestamp()-started)/(double)System.Diagnostics.Stopwatch.Frequency,characters);
        }
        private RecoverySnapshot PrepareRecovery()
        {
            using var timing=TransitionTiming.Measure("snapshot.prepare",ServerWorld?.worldId);
            long started=BeginRecoveryMeasurement();
            var snapshot=Saves.CaptureWorldSnapshot(ServerWorld);
            var store=Saves;
            saveWrites.Enqueue(()=>store.SaveRecovery(snapshot),()=>
            {
                if(ServerWorld?.worldId==snapshot.WorldId) canonicalPersistedRevision=Math.Max(canonicalPersistedRevision,snapshot.Revision);
                RecoveryPersisted?.Invoke(snapshot);
                if(ServerWorld?.worldId==snapshot.WorldId) CompleteBackedUpGrants();
            },SaveWriteFailed);
            recoveryReceipts.Register(snapshot);
            RecoveryPrepared?.Invoke(snapshot.Id,snapshot.Revision);
            FinishRecoveryMeasurement("recovery-prepared",started,snapshot.Json.Length);
            return snapshot;
        }

        private void MergeActor(ActorState actor)
        {
            if (World == null) return;
            var current = World.Find(actor.id);
            WeaponRecoilReplication.Normalize(actor,Pause.Revision,Pause.IsPaused(actor.level),"состояние актёра / входящий");
            ActorHoldRules.PreserveNewerTarget(current,actor);
            if (current != null && ActorVersion.IsOlder(actor,current)) return;
            selfHealing.Observe(World.worldId,current);
            bool selfHealTransition=selfHealing.TryApply(World.worldId,World.completedTransactions,current,ref actor,LocalOwner);
            if(!selfHealTransition && selfHealing.AwaitingCommit(World.worldId,World.completedTransactions,current,actor,LocalOwner)) return;
            if(selfHealTransition) PreserveOwnerInventory(actor,current);
            if(!selfHealTransition && current!=null && current.owner==LocalOwner && current.generation==actor.generation &&
                current.lifecycleSequence==actor.lifecycleSequence && current.level==actor.level)
            {
                ActorPose.CopyMotion(current,actor);
                NpcPerception.CopyState(current,actor);
                NpcAttackRules.CopyProgress(current,actor);
                PreserveOwnerInventory(actor,current);
            }
            if(!selfHealTransition) ActorMotionReplication.PreserveNewerMotion(current,actor);
            if(actor.owner!=LocalOwner) RemotePosePhase.Reconcile(current,actor);
            if(current?.owner==LocalOwner) WeaponInventory.PreserveSelection(current,actor);
            WeaponRecoilReplication.Reconcile(current,actor,LocalOwner,Pause.Revision,Pause.IsPaused(actor.level),"состояние актёра / публикация");
            ObservePoseCorrection("actor",current,actor.pose,actor.poseTime);
            if (current != null) World.actors.Remove(current);
            World.actors.Add(actor);
            if(selfHealTransition) PublishActor(actor);else Changed?.Invoke();
        }

        private static void PreserveOwnerInventory(ActorState incoming,ActorState current)
        {
            if(!InventoryJournal.Merge(incoming,current,out var inventory,out var uses))
                throw new InvalidOperationException($"Не удалось согласовать расход инвентаря: actor={incoming.id}, revision={incoming.revision}.");
            incoming.inventory=inventory;incoming.inventoryUses=uses;
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
            incoming.inventoryAliases=Remains.Serialization.StateJson.Copy(actor.inventoryAliases);
            incoming.owner = actor.owner;
            // Hold grants belong to the shared coordinator; pose/health still belong to this client.
            incoming.hold=actor.hold;incoming.holdEpoch=actor.holdEpoch;
            incoming.connected = actor.connected;
            if(incoming.level!=actor.level)
            {
                Debug.LogWarning($"Отклонена смена локации через состояние: actor={actor.id}, source={actor.level}, destination={incoming.level}. Требуется запрос перехода.");
                connection.Send(new NoticeMessage { text="Смена локации требует подтверждённого перехода." });
                return;
            }
            WeaponRecoilReplication.Normalize(incoming,Pause.ServerRevision,Pause.IsServerPaused(incoming.level),"сервер / входящий актёр");
            ActorMotionReplication.PreserveNewerMotion(actor,incoming);
            WeaponRecoilReplication.Reconcile(actor,incoming,null,Pause.ServerRevision,Pause.IsServerPaused(incoming.level),"сервер / состояние актёра");
            ServerWorld.actors[ServerWorld.actors.IndexOf(actor)] = incoming;
            // Ordinary NPC deltas need not save and transmit the whole world.
            // Reassignment still runs at the same point, and all shared changes
            // or incapacitation transitions are checkpointed immediately.
            bool checkpoint=!actor.player && (actor.down!=incoming.down || (actor.health>0)!=(incoming.health>0));
            if(!actor.player && !incoming.attackBusy)
                checkpoint|=ownership.ReassignAndReportChanges(ServerWorld,owners.Values.ToArray());
            ServerWorld.revision++;
            if(checkpoint) {BroadcastWorld();return;}
            NetworkServer.SendToAll(new ActorMessage { json = JsonUtility.ToJson(incoming) });
        }

        public void PublishActor(ActorState actor)
        {
            selfHealing.Observe(World?.worldId,actor);
            Changed?.Invoke();
            if (NetworkClient.isConnected)
            {
                actor.motionSequence=unchecked(actor.motionSequence+1);
                StampMotion(actor);
                NetworkClient.Send(new ActorMessage { json = JsonUtility.ToJson(actor) });
            }
        }

        private void UpdatePose(NetworkConnectionToClient connection, PoseMessage message)
        {
            var actor = ServerWorld.Find(message.actor);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner ||
                actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence ||
                !serverPoses.Accept(message.actor,message.generation,message.lifecycleSequence,message.sequence)) return;
            if(!ActorMotionReplication.IsNewer(message.sequence,actor.motionSequence)) return;
            ApplyPose(actor, message);
            NetworkServer.SendToAll(message, Channels.Unreliable);
        }

        private void ReceivePose(PoseMessage message)
        {
            var actor = World?.Find(message.actor);
            if (actor == null || actor.owner == LocalOwner || actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence) return;
            if (!clientPoses.Accept(message.actor,message.generation,message.lifecycleSequence,message.sequence)) return;
            if(!ActorMotionReplication.IsNewer(message.sequence,actor.motionSequence)) return;
            ObservePoseCorrection("pose",actor,message.pose,message.poseTime);
            ApplyPose(actor, message,true);
            PoseReceived?.Invoke(actor);
        }

        private void ApplyPose(ActorState actor, PoseMessage message,bool remote=false)
        {
            // Inspect the reliable selection before applying the ordinary weapon pose.
            WeaponRecoilReplication.ApplyPoseSample(actor,message.weaponRecoil,message.weaponSelectionRevision,message.weapon,
                remote?Pause.Revision:Pause.ServerRevision,remote?Pause.IsPaused(actor.level):Pause.IsServerPaused(actor.level),
                remote?"поза / клиент":"поза / сервер");
            actor.motionSentAt=message.sentAt;actor.motionClockGeneration=message.generation;
            actor.x = message.x; actor.y = message.y; actor.vx = message.vx; actor.vy = message.vy;
            actor.facing = message.facing;
            if(remote) RemotePosePhase.Apply(actor,message.pose,message.poseSequence,message.poseTime);
            else {actor.pose=message.pose;actor.poseTime=message.poseTime;actor.poseSequence=message.poseSequence;actor.poseCorrectionRemaining=0;}
            actor.motionSequence=message.sequence;
            actor.climbing=message.climbing;
            actor.jumpSupportRemaining=message.jumpSupportRemaining;
            actor.crouched=message.crouched;
            // Selection travels reliably in ActorMessage. A delayed pose cannot change the active selection.
            if(message.weaponSelectionRevision==actor.weaponSelectionRevision)
            {
                actor.weapon=message.weapon;actor.weaponAngle=message.weaponAngle;
                actor.weaponTipX=message.weaponTipX;actor.weaponTipY=message.weaponTipY;
                actor.weaponReach=message.weaponReach;actor.weaponLift=message.weaponLift;
                actor.weaponFlipY=message.weaponFlipY;
                actor.heldItemPoseX=message.heldItemPoseX;actor.heldItemPoseY=message.heldItemPoseY;
                actor.heldItemPoseValid=message.heldItemPoseValid;
            }
            if(!actor.player && message.perception!=null) {actor.target=message.target;actor.perception=message.perception.Copy();}
            if(actor.attackId==message.attackId && actor.attackBusy==message.attackBusy)
            {
                actor.attackRemaining=message.attackRemaining;actor.attackCooldown=message.attackCooldown;
            }
        }

        private static void StampMotion(ActorState actor)
        {actor.motionSentAt=Time.unscaledTimeAsDouble;actor.motionClockGeneration=actor.generation;}
        public void PublishPose(ActorState actor)
        {
            WeaponRecoilReplication.Normalize(actor,Pause.Revision,Pause.IsPaused(actor.level),"публикация позы");
            StampMotion(actor);
            NetworkClient.Send(new PoseMessage
        {
            sentAt=actor.motionSentAt,
            actor = actor.id, generation = actor.generation,lifecycleSequence=actor.lifecycleSequence, sequence = actor.motionSequence=unchecked(actor.motionSequence+1),
            x = actor.x, y = actor.y, vx = actor.vx, vy = actor.vy, facing = actor.facing, pose = actor.pose,poseTime=actor.poseTime,poseSequence=actor.poseSequence,climbing=actor.climbing,crouched=actor.crouched,jumpSupportRemaining=actor.jumpSupportRemaining,
            weapon=actor.weapon,weaponAngle=actor.weaponAngle,weaponTipX=actor.weaponTipX,weaponTipY=actor.weaponTipY,weaponReach=actor.weaponReach,weaponLift=actor.weaponLift,weaponFlipY=actor.weaponFlipY,
            weaponSelectionRevision=actor.weaponSelectionRevision,weaponRecoil=actor.weaponRecoil.Copy(),heldItemPoseX=actor.heldItemPoseX,heldItemPoseY=actor.heldItemPoseY,heldItemPoseValid=actor.heldItemPoseValid,
            attackId=actor.attackId,attackBusy=actor.attackBusy,attackRemaining=actor.attackRemaining,attackCooldown=actor.attackCooldown,
            target=actor.player?null:actor.target,perception=actor.player?null:actor.perception.Copy()
        }, Channels.Unreliable);
        }

        public void Fire(AttackEvent attack)
        {
            if(PrepareProjectile(attack)) FirePrepared(attack);
        }
        // Gameplay callers prepare before spending ammunition/mana. Receivers never call this.
        public bool PrepareProjectile(AttackEvent attack)
        {
            if(GameplayFailurePending) return false;
            if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.Get(attack.weapon).projectileSpeed<=0) return true;
            var actor=World?.Find(attack.attacker);
            if(actor==null || actor.owner!=LocalOwner || actor.level!=attack.level || Pause.IsPaused(attack.level)) return false;
            attack.sourceOwner=LocalOwner;attack.sourcePlayer=actor.player;attack.friendlyFireDecision=World.friendlyFireDecision;
            try
            {
                var definition=WeaponCatalog.Get(attack.weapon);
                Remains.Gameplay.RangedShotPreparation.ValidateOwner(attack,actor,World.worldId,definition,Rules.WeaponRecoil);
                ProjectileLaunch.PrepareOwnerLaunch(attack,definition,UnityEngine.Random.value,UnityEngine.Random.value,Rules.ShotDirection);
                return true;
            }
            catch(Exception error)
            {
                var failure=new InvalidOperationException($"Ошибка подготовки выстрела: actor={actor.id}, shot={attack.id}, weapon={attack.weapon}, rule={Rules.ShotDirection.GetType().FullName}. Боеприпасы не расходуются до успешной подготовки.",error);
                ReportGameplayPreparationFailure(failure);return false;
            }
        }
        public void ReportGameplayPreparationFailure(Exception error)
        {
            if(GameplayFailurePending) return;
            GameplayFailurePending=true;Debug.LogError(error);GameplayPreparationFailed?.Invoke(error);
        }
        public void FirePrepared(AttackEvent attack)
        {
            if(GameplayFailurePending) return;
            if(NpcNaturalAttack.IsNatural(attack))
            {
                var source=World?.Find(attack.attacker);
                if(source==null || source.owner!=LocalOwner || Pause.IsPaused(attack.level)) return;
                NpcNaturalAttack.ValidateEvent(attack,source);
                NetworkClient.Send(new AttackMessage {json=JsonUtility.ToJson(attack)});return;
            }
            var definition=WeaponCatalog.Get(attack.weapon);
            if(definition.projectileSpeed<=0) {NetworkClient.Send(new AttackMessage {json=JsonUtility.ToJson(attack)});return;}
            var actor=World?.Find(attack.attacker);
            if(actor==null || actor.owner!=LocalOwner || actor.level!=attack.level || Pause.IsPaused(attack.level)) return;
            attack.sourceOwner=LocalOwner;attack.sourcePlayer=actor.player;attack.friendlyFireDecision=World.friendlyFireDecision;
            ProjectileLaunch.Create(attack,definition);
            // Reliable launch precedes progress and contact messages from its local simulation.
            NetworkClient.Send(new AttackMessage {json=JsonUtility.ToJson(attack)});
            Projectiles.BeginLocal(attack);Recoil.Commit(actor,attack);
            if(!GameplayFailurePending) AttackReceived?.Invoke(attack);
        }
        private void RelayAttack(NetworkConnectionToClient connection, AttackMessage message)
        {
            var attack = JsonUtility.FromJson<AttackEvent>(message.json);
            if(attack.weapon=="grenade") {Debug.LogWarning($"Взрыв без сохраняемой сущности отклонён: {attack.id}.");return;}
            void Reject(string reason)
            {
                Debug.LogWarning($"Запуск отклонён: shot={attack.id}, actor={attack.attacker}, level={attack.level}, reason={reason}");
                connection.Send(new ProjectileRejectedMessage {id=attack.id,reason=reason});
            }
            if (!owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.Find(attack.attacker)?.owner != owner)
            {Reject("Выстрел отменён: изменился владелец персонажа.");return;}
            if(string.IsNullOrEmpty(attack.level) || (attack.weapon!="grenade" && attack.level!=ServerWorld.Find(attack.attacker).level))
            {Reject("Выстрел отменён: изменилась локация персонажа.");return;}
            attack.sourceOwner=owner;attack.sourcePlayer=ServerWorld.Find(attack.attacker).player;
            attack.friendlyFireDecision=ServerWorld.friendlyFireDecision;
            if(NpcNaturalAttack.IsNatural(attack))
            {
                try {NpcNaturalAttack.ValidateEvent(attack,ServerWorld.Find(attack.attacker));}
                catch(InvalidOperationException error) {Reject(error.Message);return;}
            }
            var definition=NpcNaturalAttack.IsNatural(attack)?null:WeaponCatalog.Get(attack.weapon);
            if(definition?.meleeMotion!=null && !MeleeTerrainOcclusion.Validate(attack,out string meleeReason))
            {Reject(meleeReason);return;}
            float projectileSpeed=definition?.projectileSpeed??0;
            if(projectileSpeed>0)
            {
                // An already-fired packet can arrive after pause. Retain its frozen flight;
                // new local input is blocked by the firing client, not discarded in transit.
                try {Remains.Gameplay.RangedShotPreparation.ValidateReceived(attack,ServerWorld.worldId);if(!Projectiles.Launch(attack)) return;}
                catch(Exception error) when(error is ArgumentException || error is InvalidOperationException)
                {Reject("Некорректные параметры выстрела: "+error.Message);return;}
            }
            string contactReason=DoorDamage.RecordAccepted(attack,ServerWorld.Find(attack.attacker));
            if(contactReason!=null) {Reject(contactReason);return;}
            NetworkServer.SendToAll(new AttackMessage {json=JsonUtility.ToJson(attack)});
        }

        public void ReportNpcHit(NpcHitMessage message)
        {
            TraceNpcHit("npc-hit-reported",message);
            // Keep the routed contact even for a locally owned target: the host may
            // already have handed it off. Send before publishing the local result.
            NetworkClient.Send(message);
            var target=World?.Find(message.target);
            if(target!=null && !target.player && target.owner==LocalOwner && target.generation==message.generation)
                NpcHitReceived?.Invoke(message);
            // The normal receiver checks scope/pause and uses the same durable
            // attack receipt. Its later network echo cannot apply damage twice.
        }
        public void ReportFriendlyHit(FriendlyHitMessage message)=>NetworkClient.Send(message);
        private void RelayFriendlyHit(NetworkConnectionToClient connection,FriendlyHitMessage message)
        {
            var attack=JsonUtility.FromJson<AttackEvent>(message.attackJson);
            var target=ServerWorld.Find(message.target);
            var source=ServerWorld.Find(attack.attacker);
            if(target==null || target.generation!=message.generation || target.lifecycleSequence!=message.lifecycle ||
                !owners.TryGetValue(connection.connectionId,out var owner) ||
                !(!NpcNaturalAttack.IsNatural(attack) && WeaponCatalog.Get(attack.weapon).projectileSpeed>0?Projectiles.IsDecisionOwner(owner,attack):source!=null && source.player && source.owner==owner && attack.sourceOwner==owner) ||
                !FriendlyFireDecision.UsesAttacker(attack,target) || !AttackScope.CanAffect(attack,target,ServerWorld.friendlyFire)) return;
            foreach(var pair in owners)
                if(pair.Value==target.owner && NetworkServer.connections.TryGetValue(pair.Key,out var destination))
                {destination.Send(message);return;}
        }
        private void RelayNpcHit(NetworkConnectionToClient connection, NpcHitMessage message)
        {
            TraceNpcHit("npc-hit-relay",message,true);
            var attack = JsonUtility.FromJson<AttackEvent>(message.attackJson);
            var target = ServerWorld.Find(message.target);
            if (target == null || target.player || string.IsNullOrEmpty(attack?.id) || !AttackScope.CanAffect(attack,target,ServerWorld.friendlyFire) ||
                !owners.TryGetValue(connection.connectionId, out string owner) || !(!NpcNaturalAttack.IsNatural(attack) && WeaponCatalog.Get(attack.weapon).projectileSpeed>0?Projectiles.IsDecisionOwner(owner,attack):ServerWorld.Find(attack.attacker)?.owner==owner)) return;
            if(NpcNaturalAttack.IsNatural(attack))
            {
                try {NpcNaturalAttack.ValidateEvent(attack,ServerWorld.Find(attack.attacker));}
                catch(InvalidOperationException error) {Debug.LogWarning(error.Message);return;}
            }
            npcHits.Accept(message,attack);
        }

        public void RequestClaim(string container,string item,int quantity=0) => RequestTrackedClaim(container,item,quantity);
        public string RequestTrackedClaim(string container,string item,int quantity=0)
        {
            var actor=LocalActor;
            if(actor==null || !NetworkClient.isConnected) return null;
            string transaction=Guid.NewGuid().ToString("N");
            NetworkClient.Send(new ClaimMessage {transaction=transaction,container=container,item=item,quantity=quantity,
                actorGeneration=actor.generation,actorLifecycle=actor.lifecycleSequence});
            return transaction;
        }
        public string RequestRevive(string target)=>RequestHealing(target,false);
        public void RequestSelfHeal()=>RequestSelfHeal(null);
        public void RequestSelfHeal(string itemId)=>RequestHealing(LocalActorId,true,itemId);
        private string RequestHealing(string targetId,bool selfHeal,string itemId=null)
        {
            var healer=LocalActor;var target=World?.Find(targetId);
            if(healer==null || target==null) return null;
            string transaction=Guid.NewGuid().ToString("N");
            if(selfHeal)
            {
                string reason=HealingRules.Rejection(healer,target,true);
                if(reason!=null) {Notice?.Invoke(reason);return null;}
                selfHealing.Capture(World.worldId,transaction,healer);SelfHealingRequested?.Invoke(transaction);
            }
            NetworkClient.Send(new ReviveMessage {target=targetId,transaction=transaction,selfHeal=selfHeal,
                healerGeneration=healer.generation,healerLifecycle=healer.lifecycleSequence,damageSequence=healer.damageSequence,
                targetGeneration=target.generation,targetLifecycle=target.lifecycleSequence,item=itemId});
            return transaction;
        }
        public void ReturnToFactory() => Travel.RequestFactory();
        // Isolated regression scenarios retain explicit fixture transitions.
        public bool DebugTravelAllowed {get;set;}

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
            if(!current || !HealingRules.ApplyOnce(ServerWorld,healer,target,message.selfHeal,message.item,message.transaction))
            {connection.Send(new HealingReplyMessage {transaction=message.transaction,success=false,reason="Лечение отменено: изменилось состояние, расстояние или нет лечебного предмета."});return;}
            BroadcastWorld();
            connection.Send(new HealingReplyMessage {transaction=message.transaction,success=true,
                reason=message.selfHeal?"Лечебный предмет использован.":"Союзник поднят. Лечебный предмет использован."});
        }

        private void Claim(NetworkConnectionToClient connection, ClaimMessage message)
        {
            if (!owners.TryGetValue(connection.connectionId, out string owner)) return;
            var actor = ServerWorld.Find(playerIds[owner]);
            bool current=actor!=null && actor.generation==message.actorGeneration && actor.lifecycleSequence==message.actorLifecycle;
            if(!current)
            {connection.Send(new ClaimReplyMessage {transaction=message.transaction,accepted=false,reason="Подбор отменён: изменилось состояние персонажа."});return;}
            if(!string.IsNullOrEmpty(message.transaction) &&
                ServerWorld.pendingGrants.Any(grant=>grant.transaction==message.transaction && grant.recipient==actor.id && grant.containerId==message.container))
            {connection.Send(new ClaimReplyMessage {transaction=message.transaction,accepted=true,reason="Запрос подбора уже принят. Состояние выдачи подтверждается снимком мира."});return;}
            bool success = inventory.PrepareClaim(message.transaction,message.container, message.item, actor,out string reason,message.quantity,Pause.IsServerPaused(actor.level));
            connection.Send(new ClaimReplyMessage {transaction=message.transaction,accepted=success,
                reason=success?"Подбор подтверждается копиями мира…":reason});
            if (success) { BroadcastWorld(); CompleteBackedUpGrants(); }
        }

        internal bool HasInventoryBackups(long revision)=>canonicalPersistedRevision>=revision &&
            !owners.Keys.Any(id=>!backupRevisions.TryGetValue(id,out long rev) || rev<revision);
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
            changed|=DroppedItemTransfer.PruneEmpty(ServerWorld);
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
            using var timing=TransitionTiming.Measure("snapshot.broadcast",ServerWorld?.worldId);
            Pause.Publish();
            var snapshot=PrepareRecovery();
            foreach(int id in owners.Keys)
                if(NetworkServer.connections.TryGetValue(id,out var connection)) WorldReplication.Send(connection,snapshot);
            // New authority arrives before its pending hits on the same reliable channel.
            // A generation change must not wait for the periodic retry timer.
            npcHits?.Flush();
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
            FlushSaveWrites();
            if(npcHits!=null) {Destroy(npcHits);npcHits=null;}
            Pause.ResetServer();
            CancelInvoke(nameof(CheckpointWorld));
            if (ServerWorld != null) Saves.SaveWorld(ServerWorld);
            LevelTime.ResetServer();
            Mechanisms.ResetServer();DoorDamage.ResetServer();
            QueueStopNotification();
        }
        public override void OnStopClient()
        {
            recoil?.Clear();
            selfHealing.Reset();
            CharacterContent.Reset();ModMismatch?.Invoke(null);
            Admission.EndClient();
            Travel.ResetClient();Lobby.ResetClient();worldReplication.Reset();
            Projectiles.ResetClient();
            SaveLocalCharacter();
            FlushSaveWrites();joinSelection=null;
            World=null;LocalActorId=null;LocalOwner=null;
            playTime.Stop();
            ContainerInspection.ResetClient();DoorDamage.ResetClient();Doors.ResetClient();Pause.ResetClient();LevelTime.ResetClient();Mechanisms.ResetClient();
            Changed?.Invoke();
            QueueStopNotification();
        }
        private void QueueStopNotification()
        {
            if(stopNotification==null) stopNotification=StartCoroutine(NotifyAfterShutdown());
        }
        private IEnumerator NotifyAfterShutdown()
        {
            // Mirror invokes OnStopClient/OnStopServer before their Shutdown.
            // A host is fully stopped only after both sides have released the session.
            yield return null;
            while(NetworkClient.active || NetworkServer.active) yield return null;
            stopNotification=null;
            SessionStopped?.Invoke();
        }
        private void CancelStopNotification()
        {
            if(stopNotification==null) return;
            StopCoroutine(stopNotification);stopNotification=null;
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
