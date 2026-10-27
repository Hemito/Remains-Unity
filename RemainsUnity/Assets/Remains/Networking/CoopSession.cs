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
        // Read many times per frame (every body view, every system step): check the remembered slot first, scan only when the
        // list changed under it. The id is compared on every read, so a replaced or moved actor is never returned stale.
        public ActorState LocalActor
        {
            get
            {
                var actors=World?.actors;string id=LocalActorId;
                if(actors==null || id==null) return null;
                if((uint)localActorSlot<(uint)actors.Count && actors[localActorSlot].id==id) return actors[localActorSlot];
                for(int i=0;i<actors.Count;i++) if(actors[i].id==id) {localActorSlot=i;return actors[i];}
                return null;
            }
        }
        private int localActorSlot=-1;
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
        // Each subscriber in its own FrameHitchLog section (subscribers rebuild presentation on world changes).
        // What the current Changed notification changed (valid during the handlers; All outside and for every
        // notification that does not know better). A consumer skips work for unchanged parts.
        public WorldChangeKind LastChange {get;private set;}=WorldChangeKind.All;
        // Actor update streams by pieces (ActorStreams): this client's sends, the host's broadcasts, and the bases
        // each receiver holds (per connection on the host).
        private readonly ActorPiecesSender clientActors=new(),serverActors=new();
        private readonly ActorPiecesReceiver clientActorsReceived=new();
        private readonly Dictionary<int,ActorPiecesReceiver> serverActorsReceived=new();
        // The changed part keys of the current notification ("containers:<id>", "props:<id>"...) when known exactly
        // (a delta that kept every list's composition); null means any part may have changed.
        public IReadOnlyCollection<string> LastChangedParts {get;private set;}
        private void RaiseChanged(WorldChangeKind kind=WorldChangeKind.All,IReadOnlyCollection<string> parts=null)
        {
            var handlers=Changed;if(handlers==null) return;
            LastChange=kind;LastChangedParts=kind==WorldChangeKind.All?null:parts;
            try
            {
                foreach(Action handler in handlers.GetInvocationList())
                {
                    var method=handler.Method;
                    string owner=method.DeclaringType?.Name??"?";
                    using(Remains.Diagnostics.FrameHitchLog.Section("Мир изменился → "+(method.DeclaringType?.DeclaringType?.Name??owner)+"."+method.Name)) handler();
                }
            }
            finally {LastChange=WorldChangeKind.All;LastChangedParts=null;}
        }
        // Interacting client first (decisions.md, 2026-10-24; ClientPredictions). Undone when the canonical copy does not
        // confirm it within PredictionSeconds (a reliable request normally confirms in a round trip).
        private readonly ClientPredictions predictions=new();
        public const float PredictionSeconds=8;
        // apply changes the live world (false: nothing to predict); confirmed reads the canonical world; revert puts the
        // canonical part back into the live world. Needs the canonical copy of transport v2.
        public bool Predict(string key,WorldChangeKind kind,Func<WorldState,bool> apply,Func<WorldState,bool> confirmed,Action<WorldState,WorldState> revert)
        {
            if(World==null || !tickReceiver.Ready || !apply(World)) return false;
            predictions.Add(new ClientPredictions.Entry {Key=key,Kind=kind,Confirmed=confirmed,Revert=revert,Deadline=Time.unscaledTime+PredictionSeconds});
            RaiseChanged(kind,new[]{key});
            return true;
        }
        public void RejectPrediction(string key,string reason)
        {
            if(predictions.Count==0) return;
            SettlePredictions(null,new[]{key},reason);
        }
        private void SettlePredictions(float? now,ICollection<string> rejected=null,string reason=null)
        {
            var undo=predictions.Settle(tickReceiver.Ready?tickReceiver.Canonical:null,now,rejected);
            if(undo==null || World==null || !tickReceiver.Ready) return;
            var kind=WorldChangeKind.None;var keys=new List<string>();
            foreach(var entry in undo)
            {
                entry.Revert(tickReceiver.Canonical,World);kind|=entry.Kind;keys.Add(entry.Key);
                Debug.LogWarning($"Локальное действие отменено: {entry.Key} — {(reason!=null?"хост отклонил: "+reason:$"хост не подтвердил за {PredictionSeconds} с")}; возвращено состояние хоста.",this);
            }
            RaiseChanged(kind,keys);
        }
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
        // Optional diagnostics: detached capture and the exact profile from that
        // completed FIFO operation, rather than a later mutable profile head.
        public event Action<RecoverySnapshot,ActorState> RecoveryReceiptPrepared;
        public event Action<RecoverySnapshot,string> RecoveryReceiptPersisted;
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
        private SessionEntryPreparation entryPreparation;
        public SessionEntryPreparation EntryPreparation=>entryPreparation ??= new SessionEntryPreparation(this);
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
        private SessionLamps lamps;
        public SessionLamps Lamps=>lamps ??= new SessionLamps(this);
        private SessionMines mines;
        public SessionMines Mines=>mines??=new SessionMines(this);
        private SessionHazards hazards;
        public SessionHazards Hazards=>hazards??=new SessionHazards(this);
        private SessionDoorDamage doorDamage;
        public SessionDoorDamage DoorDamage=>doorDamage ??= new SessionDoorDamage(this);
        private SessionHealingAudio healingAudio;
        public SessionHealingAudio HealingAudio=>healingAudio??=new SessionHealingAudio(this);
        private SessionDoors doors;
        public SessionDoors Doors=>doors ??= new SessionDoors(this);
        private SessionContainerInspection containerInspection;
        public SessionContainerInspection ContainerInspection=>containerInspection ??= new SessionContainerInspection(this);
        private SessionActorHolds actorHolds;
        public SessionActorHolds ActorHolds=>actorHolds ??= new SessionActorHolds(this);
        private SessionPropImpacts propImpacts;
        public SessionPropImpacts PropImpacts=>propImpacts ??= new SessionPropImpacts(this);
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
        private readonly Dictionary<int,List<BackupAckMessage>> pendingRecoveryAcks=new();
        private readonly WorldReplication worldReplication=new();
        // Transport v2 (docs/history/transport-v2-design-20261023.md): the live world follows world ticks; snapshots
        // remain recovery copies (every RecoveryIntervalSeconds and while a pickup waits for durable copies). The previous
        // snapshot-delta transport was removed 2026-10-26 (protocol 82).
        private int ticksLogged;
        // Recovery copies keep the previous 3 s cadence until the tick journal replaces them (design step 3); shared
        // events no longer each compose a snapshot.
        private const double TickIntervalSeconds=.05,SlowTickIntervalSeconds=1,RecoveryIntervalSeconds=3;
        private readonly WorldTickComposer tickComposer=new();
        private readonly HashSet<int> tickReady=new();
        private readonly Dictionary<string,long> snapshotTickSeq=new(StringComparer.Ordinal);
        private double lastTickAt,lastSlowTickAt,lastRecoveryAt=double.NegativeInfinity;
        private readonly WorldTickReceiver tickReceiver=new();
        // Delta replication (host-migration.md): the snapshot each connection holds, and the recent host snapshots
        // a delta can start from. A connection on another base gets a full snapshot.
        private readonly Dictionary<int,string> deliveredSnapshots=new();
        private readonly ResyncLimit resyncLimit=new();
        // The reliable backlog per connection (null without a KCP transport): a peer behind by PauseBacklog segments
        // (~3.5 MB) gets no world ticks until its link drains below ResumeBacklog, then one full world re-bases it.
        public RemainsKcpTransport Backlog;
        private const int PauseBacklog=3000,ResumeBacklog=300;
        private readonly HashSet<int> tickPaused=new();
        // Full-snapshot requests per connection within a sliding window.
        private sealed class ResyncLimit
        {
            public const int Count=5;public const double WindowSeconds=60;
            private readonly Dictionary<int,Queue<double>> times=new();
            public bool Allow(int connection,double now)
            {
                if(!times.TryGetValue(connection,out var queue)) times[connection]=queue=new Queue<double>();
                while(queue.Count>0 && now-queue.Peek()>WindowSeconds) queue.Dequeue();
                if(queue.Count>=Count) return false;
                queue.Enqueue(now);return true;
            }
            public void Forget(int connection)=>times.Remove(connection);
            public void Clear()=>times.Clear();
        }
        // Tick seq of the last prepared copies (a full world names the tick it includes); a few, not the copies themselves.
        private readonly Queue<string> preparedSnapshotIds=new();
        private const int PreparedSnapshotsTracked=4;
        private OwnershipService ownership = new();
        private SharedInventory inventory;
        private readonly HashSet<string> recoveredActors=new();

        public override void OnStartServer()
        {
            using var serverStartupTiming=Remains.Diagnostics.FrameHitchLog.Section("Запуск сервера: всего");
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
            backupRevisions.Clear();recoveryReceipts.Clear();pendingRecoveryAcks.Clear();owners.Clear();playerIds.Clear();deliveredSnapshots.Clear();preparedSnapshotIds.Clear();resyncLimit.Clear();tickReady.Clear();tickPaused.Clear();snapshotTickSeq.Clear();lastRecoveryAt=double.NegativeInfinity;
            Travel.RegisterServer();
            ContainerInspection.RegisterServer();
            Mechanisms.RegisterServer();Mines.RegisterServer();Hazards.RegisterServer();
            DoorDamage.RegisterServer();Lamps.RegisterServer();Doors.RegisterServer();ActorHolds.RegisterServer();PropImpacts.RegisterServer();Pause.RegisterServer();Deposits.RegisterServer();Projectiles.RegisterServer();ReloadAudio.RegisterServer();HealingAudio.RegisterServer();
            Admission.RegisterServer();EntryPreparation.RegisterServer();
            InvokeRepeating(nameof(ExpireEntryPreparation),1,1);
            ServerWorld=null;
            Lobby.RegisterServer(UseLobby);
            if(!UseLobby) StartSelectedWorld(null,false);
            NetworkServer.RegisterHandler<JoinMessage>(Remains.Networking.MessageTiming.Server<JoinMessage>(Join));
            serverActors.Clear();serverActorsReceived.Clear();
            NetworkServer.RegisterHandler<ActorMessage>(Remains.Networking.MessageTiming.Server<ActorMessage>(UpdateActor));
            NetworkServer.RegisterHandler<ActorResyncRequest>(Remains.Networking.MessageTiming.Server<ActorResyncRequest>((connection,request)=>
            {
                if(serverActors.TryFull(request.actor,out var full)) connection.Send(full);
                else Debug.LogWarning($"Запрос полного состояния персонажа без отправленной базы: actor={request.actor}, connection={connection.connectionId}.");
            }));
            NetworkServer.RegisterHandler<PoseMessage>(Remains.Networking.MessageTiming.Server<PoseMessage>(UpdatePose));
            NetworkServer.RegisterHandler<AttackMessage>(Remains.Networking.MessageTiming.Server<AttackMessage>(RelayAttack));
            NetworkServer.RegisterHandler<NpcHitMessage>(Remains.Networking.MessageTiming.Server<NpcHitMessage>(RelayNpcHit));
            NetworkServer.RegisterHandler<FriendlyHitMessage>(Remains.Networking.MessageTiming.Server<FriendlyHitMessage>(RelayFriendlyHit));
            NetworkServer.RegisterHandler<ClaimMessage>(Remains.Networking.MessageTiming.Server<ClaimMessage>(Claim));
            NetworkServer.RegisterHandler<ReviveMessage>(Remains.Networking.MessageTiming.Server<ReviveMessage>(Revive));
            NetworkServer.RegisterHandler<WorldResyncRequest>(Remains.Networking.MessageTiming.Server<WorldResyncRequest>((connection,request)=>
            {
                if(ServerWorld==null) return;
                if(!owners.ContainsKey(connection.connectionId))
                {Debug.LogWarning($"Запрос полного снимка от неподключённого игрока отклонён: connection={connection.connectionId}, snapshot={request.snapshot}.");return;}
                // A client that keeps failing the same data would make every broadcast a full snapshot: bound it.
                if(!resyncLimit.Allow(connection.connectionId,Time.unscaledTimeAsDouble))
                {
                    Debug.LogError($"Клиент слишком часто запрашивает полный снимок ({ResyncLimit.Count} за {ResyncLimit.WindowSeconds} с), соединение закрыто: connection={connection.connectionId}, snapshot={request.snapshot}, причина: {request.reason}");
                    connection.Disconnect();return;
                }
                Debug.LogWarning($"Клиент запросил полный снимок: connection={connection.connectionId}, snapshot={request.snapshot}, причина: {request.reason}");
                deliveredSnapshots.Remove(connection.connectionId);
                if(ServerWorld!=null)
                {
                    // Framing may fail before Welcome. Replay the server-assigned admission identity then.
                    var full=PrepareRecovery();
                    if(string.IsNullOrEmpty(request.world) && owners.TryGetValue(connection.connectionId,out var owner) && playerIds.TryGetValue(owner,out var actor))
                        SendFullWorld(connection,full,owner,actor);
                    else SendFullWorld(connection,full);
                }
            }));
            NetworkServer.RegisterHandler<BackupAckMessage>(Remains.Networking.MessageTiming.Server<BackupAckMessage>((connection, ack) =>
            {
                if(ServerWorld==null) return;
                if (ack.world != ServerWorld.worldId) Debug.LogWarning($"Подтверждение чужого мира: клиент={connection.connectionId}, мир={ack.world}, ожидается={ServerWorld.worldId}.");
                else AcceptRecoveryAck(connection.connectionId,ack,true);
            }));
            ServerReady?.Invoke();
        }

        internal void StartSelectedWorld(RecoverySnapshot snapshot,bool createNew,bool acceptContentMismatch=false)
        {
            using var startupTiming=Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: всего");
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
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: загрузка сохранения"))
                ServerWorld = createNew?CreateWorld():Saves.LoadWorld() ?? CreateWorld();
            WorldChanges.Track(ServerWorld);   // shared mutations of the session world are marked (delta replication)
            DoorDamage.ResetServer();Lamps.ResetServer();
            ContainerAppearancePersistence.Prepare(ServerWorld,source==null?()=>{}:
                ()=>Debug.Log("Исходный мир сохранён перед миграцией вида контейнеров: "+Saves.PreserveRecoverySource()));
            ActorControlMigration.Prepare(ServerWorld,Rules.Knockdown,Saves.PreserveRecoverySource);
            MechanismPersistence.Prepare(ServerWorld,source==null?()=>{}:
                ()=>Debug.Log("Исходный мир сохранён перед добавлением механизмов: "+Saves.PreserveRecoverySource()));
            npcHits?.Reset();
            var contentLevels=ServerWorld.actors.Select(a=>a.level).Concat(ServerWorld.containers.Select(c=>c.level))
                .Concat(ServerWorld.props.Select(p=>p.level)).Concat(ServerWorld.doors.Select(d=>d.level)).Append("hub").Distinct().ToArray();
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: источники заводов"))
                FactoryContentSources.Prepare(ServerWorld,contentLevels);
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: лампы"))
                foreach(var level in contentLevels) LampOperations.Install(ServerWorld,level,LampContent.Read(ServerWorld,level));
            RoomContentBatch[] roomContent;
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: чтение комнат"))
                roomContent=contentLevels.Select(level=>RoomContentBatch.Read(RoomAuthoring.Load(level,ServerWorld),level)).ToArray();
            if(HubTrainingContent.NeedsInstall(ServerWorld) || FactoryRaiderContent.NeedsInstall(ServerWorld) ||
                roomContent.Any(batch=>RoomContentInstaller.NeedsInstall(ServerWorld,batch)))
            {
                if(source!=null) Debug.Log("Сохранена копия мира перед обновлением базового контента: "+Saves.PreserveRecoverySource());
                using var installTiming=Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: установка комнат");
                HubTrainingContent.Install(ServerWorld);FactoryRaiderContent.Install(ServerWorld);
                foreach(var batch in roomContent) RoomContentInstaller.Apply(ServerWorld,batch);
            }
            DoorDuplicates.Remove(ServerWorld,source==null?null:()=>Debug.Log("Исходный мир сохранён перед удалением двойных дверей: "+Saves.PreserveRecoverySource()));
            FriendlyFireDecision.Validate(ServerWorld.friendlyFireDecision);
            NpcWeaponInventoryMigration.Apply(ServerWorld,source==null?null:()=>Debug.Log("Сохранена копия мира перед преобразованием оружия NPC: "+Saves.PreserveRecoverySource()));
            NpcWeaponActions.ValidateWorld(ServerWorld);
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: геометрия контактов"))
                foreach(string level in contentLevels) PropImpacts.PrepareLevel(level,RoomLayout.Load(level,ServerWorld));
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: идентичность лута"))
                CreatureDeathLootIds.PrepareWorld(ServerWorld);
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: контракты дверей"))
            PrepareDoorCapabilities(
                contentLevels.SelectMany(level=>RoomDoorCapabilityContent.Read(RoomAuthoring.Load(level,ServerWorld),level)),
                source!=null);
            WorldHistory.BeginContinuation(ServerWorld);
            LevelTimeMigration.Prepare(ServerWorld,contentLevels.Concat(ServerWorld.rooms.Select(room=>room.level)),source==null?null:
                ()=>Debug.Log("Исходный мир сохранён перед установкой часов уровней: "+Saves.PreserveRecoverySource()));
            LevelTime.BeginServer();
            Mechanisms.ReconcileServer();
            GrenadeRules.BeginSession(ServerWorld);SessionMines.BeginSession(ServerWorld);
            recoveredActors.Clear();
            using(Remains.Diagnostics.FrameHitchLog.Section("Запуск мира: позиции актёров"))
            {
                // One explicit geometry/support source per level for this load only. Every actor still
                // repairs in original list order, against supports restored at that level's saved clock.
                var placementRooms=new Dictionary<string,RoomLayout>(StringComparer.Ordinal);
                foreach(var actor in ServerWorld.actors)
                {
                    if(!placementRooms.TryGetValue(actor.level,out var room))
                    {
                        using(Remains.Diagnostics.FrameHitchLog.Section("Позиции актёров: загрузка уровня"))
                            room=RoomLayout.Load(actor.level,ServerWorld);
                        using(Remains.Diagnostics.FrameHitchLog.Section("Позиции актёров: восстановление платформ"))
                            Remains.Gameplay.PlatformSimulation.RestoreSupport(ServerWorld,room,actor.level,LevelTime.ReadServer(actor.level));
                        placementRooms.Add(actor.level,room);
                    }
                    using(Remains.Diagnostics.FrameHitchLog.Section("Позиции актёров: проверка актёра"))
                        if(SavedActorPlacement.Repair(actor,room,"загрузка мира "+ServerWorld.worldId)) recoveredActors.Add(actor.id);
                }
                Remains.Diagnostics.FrameHitchLog.Note($"Восстановление позиций: world={ServerWorld.worldId}, actors={ServerWorld.actors.Count}, preparedLevels={placementRooms.Count}, repaired={recoveredActors.Count}.");
            }
            if(recoveredActors.Count>0)
            {
                string backup=Saves.PreserveRecoverySource();
                Debug.LogWarning($"Исходный сейв сохранён перед восстановлением позиций: world={ServerWorld.worldId}, actors={recoveredActors.Count}, backup={backup}.");
                ServerWorld.revision++;Saves.SaveRecovery(CaptureServerSnapshot());
            }
            ownership.BeginSession(ServerWorld);
            inventory = new SharedInventory(ServerWorld,Rules.Loot);
            InvokeRepeating(nameof(CheckpointWorld),3,3);
        }

        internal bool PrepareDoorCapabilities(IEnumerable<DoorCapabilityLayout> layouts,bool preserveSource)
            => DoorCapabilityMigration.Prepare(ServerWorld,layouts,preserveSource
                ?()=>Debug.Log("Исходный мир сохранён перед добавлением прочности дверей: "+Saves.PreserveRecoverySource())
                :()=>{});

        internal void DiscardUnstartedWorld()
        {
            DoorDamage.ResetServer();Lamps.ResetServer();Mines.ResetServer();Hazards.ResetServer();HealingAudio.ResetServer();CancelInvoke(nameof(CheckpointWorld));LevelTime.ResetServer();Mechanisms.ResetServer();ServerWorld=null;inventory=null;recoveredActors.Clear();
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
            Admission.RegisterClient();EntryPreparation.RegisterClient();
            Lobby.RegisterClient();
            Travel.RegisterClient();
            ContainerInspection.RegisterClient();
            Mechanisms.RegisterClient();Mines.RegisterClient();Hazards.RegisterClient();
            DoorDamage.RegisterClient();Lamps.RegisterClient();Doors.RegisterClient();ActorHolds.RegisterClient();PropImpacts.RegisterClient();Pause.RegisterClient();LevelTime.RegisterClient();Deposits.RegisterClient();ReloadAudio.RegisterClient();HealingAudio.RegisterClient();
            worldReplication.Forget();tickReceiver.Forget();
            NetworkClient.RegisterHandler<WorldTickMessage>(Remains.Networking.MessageTiming.Client<WorldTickMessage>(ReceiveTick));
            NetworkClient.RegisterHandler<WorldPartMessage>(Remains.Networking.MessageTiming.Client<WorldPartMessage>(message=>
            {
                ReceivedWorld received;
                try
                {
                    received=worldReplication.ReceiveAndPublish(message,published=>
                    {
                        // Canonical advance and runtime reconciliation share the failure/reset boundary.
                        if(published.owner!=null) Welcome(new WelcomeMessage {owner=published.owner,actor=published.actor},published.snapshot);
                        else ReceiveWorld(new WorldMessage(),published.snapshot);
                        // A full world (join, resync) is the base of the ticks after its seq.
                        tickReceiver.Reset(published.snapshot.CreateLiveWorld(),published.tickSeq);
                        predictions.Clear();   // the live world was replaced by the host's
                        Debug.Log($"Тики мира: основа — полный снимок на тике {published.tickSeq}, актёров {tickReceiver.Canonical.actors.Count}.");
                    });
                }
                catch(Exception failure) when(WorldReplication.IsPublishFailure(failure))
                {
                    // Not repairable by a resync (admission or an unexpected error): stop loudly, never loop full snapshots.
                    Debug.LogError($"Принятый мир не опубликован, сессия остановлена: snapshot={failure.Data[WorldReplication.PublishFailureKey]}. {failure}");
                    ShowNotice("Ошибка применения мира. Сессия остановлена; подробности — в журнале игры.");
                    if(NetworkServer.active) StopHost();else if(NetworkClient.active) StopClient();
                    return;
                }
                if(received==null) return;
                if(received.resync)
                {
                    Debug.LogWarning("Изменение мира не применено, запрошен полный снимок: "+received.reason);
                    NetworkClient.Send(new WorldResyncRequest {world=World?.worldId,snapshot=message.snapshot,reason=received.reason});
                    return;
                }
            }));
            clientActors.Clear();clientActorsReceived.Clear();
            NetworkClient.RegisterHandler<ActorMessage>(Remains.Networking.MessageTiming.Client<ActorMessage>(message =>
            {
                if(!clientActorsReceived.TryRead(message,out var actor))
                {NetworkClient.Send(new ActorResyncRequest {actor=message.actor});return;}
                MergeActor(actor);
            }));
            // The host lacks the base of this client's actor stream: the current state goes whole.
            NetworkClient.RegisterHandler<ActorResyncRequest>(Remains.Networking.MessageTiming.Client<ActorResyncRequest>(request =>
            {
                clientActors.Forget(request.actor);
                var actor=World?.Find(request.actor);
                if(actor!=null && actor.owner==LocalOwner) NetworkClient.Send(clientActors.Next(actor));
            }));
            Projectiles.RegisterClient();
            NetworkClient.RegisterHandler<PoseMessage>(Remains.Networking.MessageTiming.Client<PoseMessage>(ReceivePose));
            NetworkClient.RegisterHandler<AttackMessage>(Remains.Networking.MessageTiming.Client<AttackMessage>(message =>
            {
                var attack=message.Read();
                if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.Get(attack.weapon).projectileSpeed<=0 || Projectiles.AcceptEcho(attack.id)) AttackReceived?.Invoke(attack);
            }));
            NetworkClient.RegisterHandler<NpcHitMessage>(Remains.Networking.MessageTiming.Client<NpcHitMessage>(message =>
            {TraceNpcHit("npc-hit-received",message);NpcHitReceived?.Invoke(message);}));
            NetworkClient.RegisterHandler<FriendlyHitMessage>(Remains.Networking.MessageTiming.Client<FriendlyHitMessage>(message=>FriendlyHitReceived?.Invoke(message)));
            NetworkClient.RegisterHandler<NoticeMessage>(Remains.Networking.MessageTiming.Client<NoticeMessage>(message => Notice?.Invoke(message.text)));
            NetworkClient.RegisterHandler<ClaimReplyMessage>(Remains.Networking.MessageTiming.Client<ClaimReplyMessage>(message=>{ClaimPrepared?.Invoke(message.transaction,message.accepted);Notice?.Invoke(message.reason);}));
            NetworkClient.RegisterHandler<ModMismatchMessage>(Remains.Networking.MessageTiming.Client<ModMismatchMessage>(message=>ModMismatch?.Invoke(JsonUtility.FromJson<ContentManifest>(System.Text.Encoding.UTF8.GetString(message.manifest)))));
            NetworkClient.RegisterHandler<HealingReplyMessage>(Remains.Networking.MessageTiming.Client<HealingReplyMessage>(message=>{if(!message.success) selfHealing.Reject(message.transaction);HealingFinished?.Invoke(message.transaction,message.success);Notice?.Invoke(message.reason);}));
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
                var join=new JoinMessage { character = joinSelection.CharacterId, displayName = joinSelection.CharacterId,
                    characterUtf8=JoinCharacter.Encode(character==null?null:JsonUtility.ToJson(character)),
                    modFingerprint=WeaponCatalog.ModFingerprint,acceptMismatch=acceptMismatch };
                JoinCharacter.RequireFits(join,Transport.active.GetMaxPacketSize(Channels.Reliable));
                NetworkClient.Send(join);
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
            EntryPreparation.Begin(connection,message);
        }

        // This is the single entry rule used on a detached preview and again on the
        // current canonical body. Preview state is never copied back into the world.
        private static ActorState PrepareEntryActor(WorldState world,JoinMessage message,out string entryNotice)
        {
            entryNotice=null;
            var matching=world.actors.Where(value=>value.character==message.character).ToArray();
            if(matching.Length>1) throw new InvalidOperationException($"В мире {world.worldId} несколько тел персонажа {message.character}. Вход отменён без выбора произвольного тела.");
            var actor=matching.FirstOrDefault();
            if(actor?.connected==true) throw new InvalidOperationException("Персонаж уже подключён: "+message.character);
            string characterJson=JoinCharacter.Decode(message.characterUtf8);
            var profile=string.IsNullOrEmpty(characterJson)?null:JsonUtility.FromJson<ActorState>(characterJson);
            if(profile!=null && profile.character!=message.character)
                throw new InvalidOperationException($"Персонаж в пакете не соответствует ID: {profile.character}, ожидается {message.character}.");
            PonyAppearance.ValidateSettings(profile?.appearance,message.character);
            if(actor!=null)
            {
                var entered=PlayerWorldEntry.Reconcile(world,actor,profile,out entryNotice);
                if(!ReferenceEquals(entered,actor)) {world.actors[world.actors.IndexOf(actor)]=entered;actor=entered;}
            }
            else
            {
                actor=profile;
                if(actor==null)
                {
                    actor=new ActorState {character=message.character};
                    actor.hair=PonyPaletteAsset.Load().ForNewCharacter(world.actors);
                    PlayerStarterInventory.Give(actor);
                }
                actor.id="player:"+message.character;actor.character=message.character;actor.player=true;
                actor=WeaponInventoryMigration.Prepare(actor);
                CharacterPlacement.SpawnInHub(actor,world.actors.Count(value=>value.player));
                world.actors.Add(actor);
            }
            return actor;
        }
        internal WorldState CreateEntryView(JoinMessage message,out ActorState actor)
        {
            if(ServerWorld==null) throw new InvalidOperationException("Нет выбранного мира для подготовки входа.");
            var view=RecoverySnapshot.FromComposition(ServerWorld,ComposeFromTicks(ServerWorld,false)).CreateLiveWorld();
            actor=PrepareEntryActor(view,message,out _);
            actor.connected=false;
            return EntryPreparationContext.NarrowView(view,actor.id,actor.level);
        }
        internal void AdmitPreparedEntry(NetworkConnectionToClient connection,JoinMessage message,EntryPreparationContext context)
        {
            if(owners.ContainsKey(connection.connectionId)) throw new InvalidOperationException("Повторный вход уже подключённого клиента.");
            var actor=PrepareEntryActor(ServerWorld,message,out string entryNotice);
            if(!context.Matches(ServerWorld,actor.id,actor.level))
                throw new InvalidOperationException("Локация персонажа изменилась при подтверждении входа.");
            string owner="connection:"+connection.connectionId;
            owners[connection.connectionId]=owner;
            ActorSessionRules.Connect(actor,owner);
            playerIds[owner]=actor.id;
            ownership.Reassign(ServerWorld,owners.Values.ToArray());
            ServerWorld.revision++;
            SendFullWorld(connection,PrepareRecovery(),owner,actor.id);
            if(entryNotice!=null) {Debug.LogWarning($"world={ServerWorld.worldId}, actor={actor.id}: {entryNotice}");connection.Send(new NoticeMessage {text=entryNotice});}
            if(recoveredActors.Contains(actor.id)) connection.Send(new NoticeMessage {text="Недопустимая позиция из старого сохранения восстановлена на свободной опоре. Здоровье и предметы сохранены; исходный сейв скопирован в Recovery."});
            BroadcastWorld();
        }
        private void ExpireEntryPreparation()=>entryPreparation?.Expire(Time.realtimeSinceStartupAsDouble);

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            Admission.Disconnected(connection.connectionId);
            Lobby.Disconnected(connection.connectionId);EntryPreparation.Disconnected(connection.connectionId);
            if (owners.Remove(connection.connectionId, out string owner))
            {
                backupRevisions.Remove(connection.connectionId);pendingRecoveryAcks.Remove(connection.connectionId);deliveredSnapshots.Remove(connection.connectionId);resyncLimit.Forget(connection.connectionId);tickReady.Remove(connection.connectionId);tickPaused.Remove(connection.connectionId);
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
            serverActorsReceived.Remove(connection.connectionId);
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
                FactoryContentSources.Prepare(welcomed,welcomed.actors.Where(a=>a.player && a.connected).Select(a=>a.level).Distinct());
                EntryPreparation.Take(welcomed,message.actor);
            }
            catch(Exception error)
            {
                Debug.LogError("Ответ подключения отклонён до записи профиля: "+error);
                ShowNotice("Ответ хоста не соответствует выбранному персонажу или подготовленной локации. Подключение отменено; профиль сохранён.");
                if(NetworkServer.active) StopHost();else StopClient();
                return;
            }
            LocalOwner = message.owner;
            LocalActorId = message.actor;
            foreach(var actor in welcomed.actors) CosmeticEquipment.ReconcileInventory(actor);
            WeaponRecoilReplication.NeutralizeWorld(welcomed,Pause.Revision);
            World = PersistCopy(snapshot,welcomed);
            Debug.Log($"Подключён выбранный персонаж: character={LocalActor.character}, actor={LocalActorId}, profile={Saves.CharacterDirectoryPath}.");
            if(LocalActor!=null && LocalActor.level!="hub")
            {
                string backup=Saves.PreserveCharacterSource();
                if(backup!=null) Debug.LogWarning($"Личный профиль сохранён перед возвращением в тело: world={World.worldId}, actor={LocalActorId}, backup={backup}. Последствия оставленного тела принадлежат миру; независимые состояния персонажа не объединяются автоматически.");
            }
            QueueRecoveryReceipt(snapshot);
            RaiseChanged();
            if(ReferenceEquals(World,welcomed) && !GameplayFailurePending) EntryPreparation.CompleteAdmission();
        }

        // Transport v2: a world tick advances the canonical copy, then the live world through the same reconciliation
        // as a snapshot (owned poses, unacknowledged health, self-healing), for the changed parts only.
        private void ReceiveTick(WorldTickMessage message)
        {
            if(World==null || LocalActorId==null) return;   // before admission: the full world carries the base seq
            WorldTick tick;WorldTickReceiver.Result result;
            try
            {
                using(Remains.Diagnostics.FrameHitchLog.Section("Тик мира: применение копии (клиент)")) result=tickReceiver.Receive(message,out tick);
            }
            catch(Exception error) when(error is System.IO.InvalidDataException || error is InvalidOperationException || error is System.Text.DecoderFallbackException)
            {
                tickReceiver.Forget();
                Debug.LogWarning($"Тик мира {message.seq} не применён, запрошен полный снимок. {error}");
                NetworkClient.Send(new WorldResyncRequest {world=World.worldId,snapshot="tick:"+message.seq,reason="тик "+message.seq+": "+error.Message});
                return;
            }
            if(result==WorldTickReceiver.Result.Gap)
            {
                long expected=tickReceiver.Seq+1;tickReceiver.Forget();
                Debug.LogWarning($"Пропущен тик мира: получен {message.seq}, ожидался {expected}; запрошен полный снимок.");
                NetworkClient.Send(new WorldResyncRequest {world=World.worldId,snapshot="tick:"+message.seq,reason=$"пропуск тика {expected}"});
                return;
            }
            if(result!=WorldTickReceiver.Result.Applied) return;
            WorldState live;
            using(Remains.Diagnostics.FrameHitchLog.Section("Тик мира: рабочая копия (клиент)"))
            {
                live=WorldTickLists.ShallowCopy(World);
                WorldTickApplier.Apply(live,tickReceiver.LastParsed,Remains.Serialization.ValidatedStateCopy.Copy);
            }
            var reused=new HashSet<string>(StringComparer.Ordinal);
            var changedActors=new HashSet<string>(StringComparer.Ordinal);
            foreach(var list in tick.lists) if(list.name=="actors" && list.keys!=null) foreach(string key in list.keys) changedActors.Add(key.Substring("actors:".Length));
            var current=new HashSet<ActorState>(World.actors);   // reference identity: the shallow copy keeps unchanged objects
            foreach(var actor in live.actors) if(!changedActors.Contains(actor.id) && current.Contains(actor)) reused.Add(actor.id);
            if(ticksLogged<3)   // the first ticks of a session, for diagnosis
            {
                ticksLogged++;
                var summary=string.Join(", ",tick.lists.Select(l=>$"{l.name} {l.keys?.Length??0}/-{l.removed?.Length??0}{(l.order!=null?" порядок "+l.order.Length:"")}"));
                Debug.Log($"Тик мира {tick.seq}: {summary}{(tick.skeleton!=null?", поля мира":"")}; актёров {World.actors.Count}→{live.actors.Count}, игроков {World.actors.Count(a=>a.player)}→{live.actors.Count(a=>a.player)}, канон {tickReceiver.Canonical.actors.Count}; игроки: {string.Join(";",live.actors.Where(a=>a.player).Select(a=>a.character+":"+a.level+":"+(a.connected?"on":"off")))}.");
            }
            ReceiveWorld(new WorldMessage(),null,WorldTickReceiver.Changes(tick),WorldTickReceiver.ChangedParts(tick),live,reused,
                tickReceiver.CopyCanonicalActor,WorldTickReceiver.FindBody,true);
            if(predictions.Count>0) SettlePredictions(null);
            if(tick.recovery!=null)
            {
                // The host's recovery point: the same copy from this client's tick texts, written and acknowledged.
                RecoverySnapshot copy;
                using(Remains.Diagnostics.FrameHitchLog.Section("Тик мира: копия восстановления (клиент)"))
                    copy=RecoverySnapshot.FromComposition(tickReceiver.Canonical,tickReceiver.Texts.Composition(tick.recovery));
                // REMAINS_TICK_DUMP=<folder>: the client's recovery copy by revision, to compare with the host's archive.
                string dump=Environment.GetEnvironmentVariable("REMAINS_TICK_DUMP");
                if(!string.IsNullOrEmpty(dump)) System.IO.File.WriteAllText(System.IO.Path.Combine(dump,$"client-{LocalOwner?.Replace(':','_')}-r{copy.Revision}.json"),copy.Json);
                using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: подтверждение (клиент)")) QueueRecoveryReceipt(copy);
            }
        }

        private void ReceiveWorld(WorldMessage message,RecoverySnapshot verified=null,WorldChangeKind changes=WorldChangeKind.All,IReadOnlyCollection<string> changedParts=null,
            WorldState live=null,HashSet<string> reusedActors=null,Func<string,ActorState> copyCanonicalActor=null,Func<WorldState,string,PropState> findBody=null,bool fromTick=false)
        {
            if(World==null || LocalActorId==null) throw new InvalidOperationException("Получен игровой снимок до фактического входа в мир.");
            using var timing=TransitionTiming.Measure("snapshot.apply",LocalActor?.level);
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section(fromTick?"Тик мира: приём (клиент)":"Снимок: приём (клиент)");
            var snapshot=fromTick?null:verified??new RecoverySnapshot(message.json);
            if(fromTick && live==null) throw new ArgumentException("Тик мира без рабочей копии мира.");
            WorldState incoming;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: копия мира (клиент)")) incoming=live??snapshot.CreateLiveWorld();
            selfHealing.MaterializeCanonical(incoming,reusedActors,copyCanonicalActor);
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: контент (клиент)")) FactoryContentSources.Prepare(incoming,incoming.actors.Where(a=>a.player && a.connected).Select(a=>a.level).Distinct());
            if(!fromTick) incoming=PersistCopy(snapshot,incoming);
            bool sameWorld=World!=null && World.worldId==incoming.worldId;
            // Validate before motion/selection helpers can replace an incoming sample.
            foreach(var actor in incoming.actors)
                if(!(reusedActors?.Contains(actor.id)??false))
                {
                    // Consumer keys describe replaced live objects, including journal-only healing and lifetime changes.
                    if(changedParts is HashSet<string> parts) parts.Add("actors:"+actor.id);
                    changes|=WorldChangeKind.Actors;
                    WeaponRecoilReplication.Normalize(actor,Pause.Revision,Pause.IsPaused(actor.level),"снимок мира / входящий");
                }
            if(!sameWorld) WeaponRecoilReplication.NeutralizeWorld(incoming,Pause.Revision);
            bool publishHealing=false;
            if (World != null && World.worldId == incoming.worldId)
            {
                // By ID dictionary: the former WorldProps.Find per body was O(bodies²) per snapshot (~2M compares).
                // Unchanged tick bodies already are the live objects kept by the shallow copy (WorldTickLists.ShallowCopy).
                if(changes.Any(WorldChangeKind.Containers|WorldChangeKind.Props))
                {
                    if(changedParts!=null && findBody!=null)
                    {
                        foreach(string key in changedParts)
                        {
                            if(!key.StartsWith("props:",StringComparison.Ordinal) && !key.StartsWith("containers:",StringComparison.Ordinal)) continue;
                            var prop=findBody(incoming,key);
                            if(prop==null) throw new InvalidOperationException("Нет изменённого физического тела: "+key);
                            PropReplication.PreserveNewerPose(findBody(World,key),prop,LocalOwner,PropImpacts.ClientImpulseEpoch);
                        }
                    }
                    else
                    {
                        var localBodies=new Dictionary<string,PropState>(StringComparer.Ordinal);
                        foreach(var prop in WorldProps.All(World)) localBodies[prop.id]=prop;
                        foreach(var prop in WorldProps.All(incoming))
                            PropReplication.PreserveNewerPose(localBodies.TryGetValue(prop.id,out var local)?local:null,prop,LocalOwner,PropImpacts.ClientImpulseEpoch);
                    }
                }
                // A full backup must not rewind locally-owned pose or an unacknowledged health change.
                for(int actorIndex=0;actorIndex<incoming.actors.Count;actorIndex++)
                {
                    var actor=incoming.actors[actorIndex];
                    if(reusedActors?.Contains(actor.id)??false) continue;
                    var local = World.Find(actor.id);
                    if(selfHealing.DiscardPreHealingState(incoming.worldId,local,actor,LocalOwner))
                    {incoming.actors[actorIndex]=local.Copy();continue;}
                    ActorHoldRules.PreserveNewerTarget(local,actor);
                    PropImpactRules.PreserveTarget(local,actor);
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
            {
                if(reusedActors?.Contains(actor.id)??false) continue;
                CosmeticEquipment.Reconcile(sameWorld?World.Find(actor.id):null,actor);
                WeaponRecoilReplication.Reconcile(sameWorld?World.Find(actor.id):null,actor,LocalOwner,Pause.Revision,
                    Pause.IsPaused(actor.level),"снимок мира / публикация");
            }
            if(World?.worldId!=incoming.worldId) selfHealing.Reset();
            World = incoming;
            if(!fromTick) using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: подтверждение (клиент)")) QueueRecoveryReceipt(snapshot);
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: событие «мир изменился» (клиент)"))
                if(publishHealing && LocalActor!=null) PublishActor(LocalActor,sameWorld?changes:WorldChangeKind.All);
                else RaiseChanged(sameWorld?changes:WorldChangeKind.All,sameWorld?changedParts:null);
        }

        private WorldState PersistCopy(RecoverySnapshot snapshot,WorldState parsed=null)
        {
            long started=BeginRecoveryMeasurement();
            var world=parsed??snapshot.CreateLiveWorld();
            FinishRecoveryMeasurement("recovery-received",started,snapshot);
            return world;
        }

        private void QueueRecoveryReceipt(RecoverySnapshot snapshot)
        {
            using var timing=TransitionTiming.Measure("snapshot.receipt",snapshot.WorldId);
            var actor=CaptureLocalCharacter();
            if(actor==null) throw new InvalidOperationException($"Нет личного персонажа для подтверждения сохранения: world={snapshot.WorldId}, actor={LocalActorId}.");
            RecoveryReceiptPrepared?.Invoke(snapshot,actor.Copy());
            var store=Saves;bool localHost=NetworkServer.active;
            var timeCheckpoint=playTime.CaptureCheckpoint(store.CharacterDirectoryPath);
            bool captureReceipt=RecoveryReceiptPersisted!=null;string characterJson=null;
            // One durable intent repairs either inter-file crash window. The host's
            // canonical archive is queued first; its local replica must not rewind the head.
            saveWrites.Enqueue(()=>
            {
                _=snapshot.Id;   // the file SHA for the acknowledgement, on the save thread (not in the main-thread callback)
                store.SaveRecoveryWithCharacter(snapshot,actor,!localHost);timeCheckpoint.Persist();
                if(captureReceipt) characterJson=store.ReadCharacterSource();
            },()=>
            {
                if(captureReceipt) RecoveryReceiptPersisted?.Invoke(snapshot,characterJson);
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
            // A detached copy for the save thread, by the trusted reader: the writer strictly re-parses it
            // (SaveStore.CaptureCharacterJson), so a strict copy here only doubled ~5 ms per received snapshot.
            return Remains.Serialization.StateJson.FromTrustedJson<ActorState>(Remains.Serialization.StateJson.ToJson(actor));
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
        // The text length only when measured: a composed snapshot builds its whole text lazily (RecoverySnapshot.Json).
        private void FinishRecoveryMeasurement(string stage,long started,RecoverySnapshot snapshot)
        {
            if(started!=0) RecoveryMeasured?.Invoke(stage,
                (System.Diagnostics.Stopwatch.GetTimestamp()-started)/(double)System.Diagnostics.Stopwatch.Frequency,snapshot.Json.Length);
        }
        private void SendFullWorld(NetworkConnectionToClient connection,RecoverySnapshot snapshot,string owner=null,string actor=null)
        {
            long seq=snapshotTickSeq.TryGetValue(snapshot.ReplicationId,out long recorded)?recorded:tickComposer.Seq;
            WorldReplication.Send(connection,snapshot,owner,actor,seq);
            deliveredSnapshots[connection.connectionId]=snapshot.ReplicationId;
            tickReady.Add(connection.connectionId);   // ticks after seq follow this full world on the same channel
        }

        private void AcceptRecoveryAck(int peer,BackupAckMessage ack,bool allowPending)
        {
            if(!owners.ContainsKey(peer))
            {
                Debug.LogWarning($"Подтверждение снимка от клиента без назначенного владельца: клиент={peer}, снимок={ack.snapshot}, ревизия={ack.revision}.",this);
                return;
            }
            if(recoveryReceipts.Matches(ack.snapshot,ack.revision))
            {
                backupRevisions[peer]=Math.Max(backupRevisions.GetValueOrDefault(peer),ack.revision);
                RecoveryAcknowledged?.Invoke(peer,ack.snapshot,ack.revision);
                CompleteBackedUpGrants();return;
            }
            if(allowPending && recoveryReceipts.Pending(ack.revision))
            {
                if(!pendingRecoveryAcks.TryGetValue(peer,out var pending)) pendingRecoveryAcks.Add(peer,pending=new());
                if(pending.Exists(value=>value.snapshot==ack.snapshot && value.revision==ack.revision)) return;
                if(pending.Count<256) {pending.Add(ack);return;}
            }
            Debug.LogWarning($"Неизвестное подтверждение снимка: клиент={peer}, снимок={ack.snapshot}, ревизия={ack.revision}.");
        }
        private void RetryRecoveryAcks()
        {
            // Remove before callbacks: accepting a receipt can enqueue another checkpoint.
            var completed=new List<(int peer,BackupAckMessage ack)>();
            foreach(var pair in pendingRecoveryAcks)
                for(int i=pair.Value.Count-1;i>=0;i--)
                    if(!recoveryReceipts.Pending(pair.Value[i].revision))
                    {completed.Add((pair.Key,pair.Value[i]));pair.Value.RemoveAt(i);}
            foreach(var value in completed) AcceptRecoveryAck(value.peer,value.ack,false);
        }
        private RecoverySnapshot PrepareRecovery()
        {
            using var timing=TransitionTiming.Measure("snapshot.prepare",ServerWorld?.worldId);
            long started=BeginRecoveryMeasurement();
            RecoverySnapshot snapshot;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: захват")) snapshot=CaptureServerSnapshot();
            var store=Saves;
            saveWrites.Enqueue(()=>store.SaveRecovery(snapshot),()=>
            {
                recoveryReceipts.Complete(snapshot);
                RetryRecoveryAcks();
                if(ServerWorld?.worldId==snapshot.WorldId) canonicalPersistedRevision=Math.Max(canonicalPersistedRevision,snapshot.Revision);
                RecoveryPersisted?.Invoke(snapshot);
                if(ServerWorld?.worldId==snapshot.WorldId) CompleteBackedUpGrants();
            },SaveWriteFailed);
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: регистрация")) recoveryReceipts.Register(snapshot);
            // Every change before this capture is in it; a later tick may repeat some parts (ticks carry absolute states).
            if(!snapshotTickSeq.ContainsKey(snapshot.ReplicationId)) preparedSnapshotIds.Enqueue(snapshot.ReplicationId);
            snapshotTickSeq[snapshot.ReplicationId]=tickComposer.Seq;
            while(preparedSnapshotIds.Count>PreparedSnapshotsTracked) snapshotTickSeq.Remove(preparedSnapshotIds.Dequeue());
            // Legacy diagnostic subscribers explicitly request the file SHA. Ordinary replication never does.
            RecoveryPrepared?.Invoke(snapshot.Id,snapshot.Revision);
            FinishRecoveryMeasurement("recovery-prepared",started,snapshot);
            return snapshot;
        }

        private void MergeActor(ActorState actor)
        {
            if (World == null) return;
            var current = World.Find(actor.id);
            WeaponRecoilReplication.Normalize(actor,Pause.Revision,Pause.IsPaused(actor.level),"состояние актёра / входящий");
            ActorHoldRules.PreserveNewerTarget(current,actor);
            PropImpactRules.PreserveTarget(current,actor);
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
            CosmeticEquipment.Reconcile(current,actor);
            WeaponRecoilReplication.Reconcile(current,actor,LocalOwner,Pause.Revision,Pause.IsPaused(actor.level),"состояние актёра / публикация");
            ObservePoseCorrection("actor",current,actor.pose,actor.poseTime);
            if (current != null) World.actors.Remove(current);
            World.actors.Add(actor);
            if(selfHealTransition) PublishActor(actor);else RaiseChanged(WorldChangeKind.Actors,new[]{"actors:"+actor.id});
        }

        private static void PreserveOwnerInventory(ActorState incoming,ActorState current)
        {
            if(!InventoryJournal.Merge(incoming,current,out var inventory,out var uses))
                throw new InvalidOperationException($"Не удалось согласовать расход инвентаря: actor={incoming.id}, revision={incoming.revision}.");
            incoming.inventory=inventory;incoming.inventoryUses=uses;
            CosmeticEquipment.Reconcile(current,incoming);
        }

        private void UpdateActor(NetworkConnectionToClient connection, ActorMessage message)
        {
            if(!serverActorsReceived.TryGetValue(connection.connectionId,out var stream)) serverActorsReceived.Add(connection.connectionId,stream=new ActorPiecesReceiver());
            if(!stream.TryRead(message,out var incoming)) {connection.Send(new ActorResyncRequest {actor=message.actor});return;}
            var actor = ServerWorld.Find(incoming.id);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner) return;
            if(!ActorVersion.CanPublishOwnerState(incoming,actor))
            {
                try
                {
                    if(CosmeticEquipment.TryApplyOwnerSelection(actor,incoming))
                    {
                        ServerWorld.revision++;
                        NetworkServer.SendToAll(serverActors.Next(actor));
                    }
                }
                catch(InvalidOperationException error)
                {Debug.LogWarning($"Отклонён отдельный выбор визуального снаряжения: actor={actor.id}. {error.Message}");}
                return;
            }
            // Shared inventory is host-coordinated; movement/combat/stat decisions remain client-owned.
            if(!InventoryJournal.Merge(actor,incoming,out var inventoryCopy,out var uses))
            {
                Debug.LogWarning($"Отклонён несогласованный расход инвентаря: actor={actor.id}, revision={incoming.revision}.");
                connection.Send(new NoticeMessage {text="Расход предметов не подтверждён: состояние инвентаря не совпало. Переподключитесь к сессии."});
                return;
            }
            incoming.inventory=inventoryCopy;incoming.inventoryUses=uses;
            incoming.inventoryAliases=Remains.Serialization.StateJson.Copy(actor.inventoryAliases);
            try {CosmeticEquipment.Reconcile(actor,incoming);CosmeticEquipment.Validate(incoming,CosmeticCatalog.Current);}
            catch(InvalidOperationException error)
            {
                Debug.LogWarning($"Отклонено визуальное снаряжение владельца: actor={actor.id}. {error.Message}");
                connection.Send(new NoticeMessage {text="Визуальное снаряжение не подтверждено. Проверьте журнал ошибок."});return;
            }
            incoming.owner = actor.owner;
            // Hold grants belong to the shared coordinator; pose/health still belong to this client.
            incoming.hold=actor.hold;incoming.holdEpoch=actor.holdEpoch;
            incoming.connected = actor.connected;
            PropImpactRules.PreserveTarget(actor,incoming);
            if(incoming.level!=actor.level)
            {
                Debug.LogWarning($"Отклонена смена локации через состояние: actor={actor.id}, source={actor.level}, destination={incoming.level}. Требуется запрос перехода.");
                connection.Send(new NoticeMessage { text="Смена локации требует подтверждённого перехода." });
                return;
            }
            WeaponRecoilReplication.Normalize(incoming,Pause.ServerRevision,Pause.IsServerPaused(incoming.level),"сервер / входящий актёр");
            ActorMotionReplication.PreserveNewerMotion(actor,incoming);
            WeaponRecoilReplication.Reconcile(actor,incoming,null,Pause.ServerRevision,Pause.IsServerPaused(incoming.level),"сервер / состояние актёра");
            CreatureDeathLoot.Plan deathLoot;
            try {deathLoot=CreatureDeathLoot.Prepare(ServerWorld,actor,incoming);}
            catch(InvalidOperationException error)
            {
                Debug.LogError($"Не принято состояние гибели NPC: world={ServerWorld.worldId}, actor={actor.id}, type={actor.actorType}. {error.Message}");
                connection.Send(new NoticeMessage {text="Ошибка данных добычи NPC. Гибель не сохранена; подробности в журнале."});return;
            }
            if(deathLoot==null)ServerWorld.actors[ServerWorld.actors.IndexOf(actor)]=incoming;
            else deathLoot.ApplyAccepted(ServerWorld);
            // Ordinary NPC deltas need not save and transmit the whole world.
            // Reassignment still runs at the same point, and all shared changes
            // or incapacitation transitions are checkpointed immediately.
            bool checkpoint=!actor.player && (actor.down!=incoming.down || (actor.health>0)!=(incoming.health>0));
            if(!actor.player && !incoming.attackBusy)
                checkpoint|=ownership.ReassignAndReportChanges(ServerWorld,owners.Values.ToArray());
            ServerWorld.revision++;
            if(checkpoint) {BroadcastWorld();return;}
            NetworkServer.SendToAll(serverActors.Next(incoming));
        }

        // changes: what the notification reports; publishing an actor changes only actors (a received world passes
        // its own changes when it publishes the local actor right after).
        public void PublishActor(ActorState actor)=>PublishActor(actor,WorldChangeKind.Actors);
        private void PublishActor(ActorState actor,WorldChangeKind changes)
        {
            selfHealing.Observe(World?.worldId,actor);
            RaiseChanged(changes,changes==WorldChangeKind.Actors?new[]{"actors:"+actor.id}:null);
            if (NetworkClient.isConnected)
            {
                actor.motionSequence=unchecked(actor.motionSequence+1);
                StampMotion(actor);
                NetworkClient.Send(clientActors.Next(actor));
            }
        }

        private void UpdatePose(NetworkConnectionToClient connection, PoseMessage message,int channel)
        {
            var actor = ServerWorld.Find(message.actor);
            if (actor == null || !owners.TryGetValue(connection.connectionId, out string owner) || actor.owner != owner ||
                actor.generation != message.generation || actor.lifecycleSequence!=message.lifecycleSequence ||
                !serverPoses.Accept(message.actor,message.generation,message.lifecycleSequence,message.sequence)) return;
            if(!ActorMotionReplication.IsNewer(message.sequence,actor.motionSequence)) return;
            ApplyPose(actor, message);
            // The sample reaches every client in this message; ticks refresh the actor's text spread over a second.
            WorldChanges.MarkMotion(ServerWorld,actor);
            NetworkServer.SendToAll(message, channel);
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
        public void PublishPose(ActorState actor,bool final=false)
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
        }, final?Channels.Reliable:Channels.Unreliable);
        }

        public void Fire(AttackEvent attack)
        {
            if(PrepareProjectile(attack)) FirePrepared(attack);
        }
        // Gameplay callers prepare before spending ammunition/mana. Receivers never call this.
        // A shot as its volley: the shot itself and, for a pellet weapon, its other pellets, each prepared (fanned
        // around the aim, own spread and speed) before any ammunition is spent. Null when any pellet fails.
        public System.Collections.Generic.List<AttackEvent> PrepareVolley(AttackEvent shot)
        {
            var definition=WeaponCatalog.Get(shot.weapon);int count=definition.projectileSpeed>0?definition.Pellets:1;
            var template=count>1?JsonUtility.FromJson<AttackEvent>(JsonUtility.ToJson(shot)):null;
            var volley=new System.Collections.Generic.List<AttackEvent>(count);
            for(int i=0;i<count;i++)
            {
                var pellet=i==0?shot:JsonUtility.FromJson<AttackEvent>(JsonUtility.ToJson(template));
                if(i>0) {pellet.id=Guid.NewGuid().ToString("N");pellet.volley=shot.id;}
                // Weapon.shoot: pellet k leaves at (k − (kol − 1)/2) × deviation/2 from the aim.
                if(!PrepareProjectile(pellet,(i-(count-1)*.5f)*definition.shotSpreadHalfAngleDegrees)) return null;
                volley.Add(pellet);
            }
            return volley;
        }
        public void FireVolley(System.Collections.Generic.List<AttackEvent> volley) {foreach(var shot in volley) FirePrepared(shot);}
        public bool PrepareProjectile(AttackEvent attack)=>PrepareProjectile(attack,0);
        private bool PrepareProjectile(AttackEvent attack,float fanDegrees)
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
                if(fanDegrees!=0)
                {
                    var aim=(Vector2)(Quaternion.Euler(0,0,fanDegrees)*(Vector3)(attack.End-attack.Origin));
                    attack.endX=attack.originX+aim.x;attack.endY=attack.originY+aim.y;
                }
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
                NetworkClient.Send(AttackMessage.From(attack));return;
            }
            var definition=WeaponCatalog.Get(attack.weapon);
            if(definition.projectileSpeed<=0) {NetworkClient.Send(AttackMessage.From(attack));return;}
            var actor=World?.Find(attack.attacker);
            if(actor==null || actor.owner!=LocalOwner || actor.level!=attack.level || Pause.IsPaused(attack.level)) return;
            attack.sourceOwner=LocalOwner;attack.sourcePlayer=actor.player;attack.friendlyFireDecision=World.friendlyFireDecision;
            ProjectileLaunch.Create(attack,definition);
            // Reliable launch precedes progress and contact messages from its local simulation.
            NetworkClient.Send(AttackMessage.From(attack));
            Projectiles.BeginLocal(attack);if(attack.volley==null) Recoil.Commit(actor,attack);
            if(!GameplayFailurePending) AttackReceived?.Invoke(attack);
        }
        private void RelayAttack(NetworkConnectionToClient connection, AttackMessage message)
        {
            var attack = message.Read();
            if(Remains.Content.WeaponCatalog.IsExplosive(attack.weapon)) {Debug.LogWarning($"Взрыв без сохраняемой сущности отклонён: {attack.id}.");return;}
            void Reject(string reason)
            {
                Debug.LogWarning($"Запуск отклонён: shot={attack.id}, actor={attack.attacker}, level={attack.level}, reason={reason}");
                connection.Send(new ProjectileRejectedMessage {id=attack.id,reason=reason});
            }
            if (!owners.TryGetValue(connection.connectionId, out string owner) || ServerWorld.Find(attack.attacker)?.owner != owner)
            {Reject("Выстрел отменён: изменился владелец персонажа.");return;}
            if(string.IsNullOrEmpty(attack.level) || (!Remains.Content.WeaponCatalog.IsExplosive(attack.weapon) && attack.level!=ServerWorld.Find(attack.attacker).level))
            {Reject("Выстрел отменён: изменилась локация персонажа.");return;}
            attack.sourceOwner=owner;attack.sourcePlayer=ServerWorld.Find(attack.attacker).player;
            attack.friendlyFireDecision=ServerWorld.friendlyFireDecision;
            try {FactoryEnemyScaling.ValidateCaptured(attack,ServerWorld.Find(attack.attacker));}
            catch(InvalidOperationException error) {Reject(error.Message);return;}
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
            contactReason=Lamps.RecordAccepted(attack,ServerWorld.Find(attack.attacker));
            if(contactReason!=null) {Reject(contactReason);return;}
            contactReason=Hazards.RecordAccepted(attack,ServerWorld.Find(attack.attacker));
            if(contactReason!=null) {Reject(contactReason);return;}
            NetworkServer.SendToAll(AttackMessage.From(attack));
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
            if(NpcNaturalAttack.IsNatural(attack) || WeaponCatalog.Get(attack.weapon).projectileSpeed<=0)
            {
                try {FactoryEnemyScaling.ValidateCaptured(attack,source);}
                catch(InvalidOperationException error) {Debug.LogWarning(error.Message);return;}
            }
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
            else if(WeaponCatalog.Get(attack.weapon).projectileSpeed<=0)
            {
                try {FactoryEnemyScaling.ValidateCaptured(attack,ServerWorld.Find(attack.attacker));}
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
            HealingAudio.Publish(healer,message.transaction,"heal");
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
            // The shared change reaches clients as a tick now (before the messages that follow it on this channel).
            FlushTick(true);
            if(!RecoveryDue()) {Pause.Publish();npcHits?.Flush();return;}
            using var timing=TransitionTiming.Measure("snapshot.broadcast",ServerWorld?.worldId);
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Снимок мира");
            Pause.Publish();
            lastRecoveryAt=Time.unscaledTimeAsDouble;
            RecoverySnapshot snapshot;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: подготовка")) snapshot=PrepareRecovery();
            Remains.Diagnostics.FrameHitchLog.Note(SnapshotPartCache.LastReport);
            // Clients compose this copy from the recovery-point tick; only a connection without any full world gets one.
            foreach(int id in owners.Keys)
                if(!deliveredSnapshots.ContainsKey(id) && NetworkServer.connections.TryGetValue(id,out var connection)) SendFullWorld(connection,snapshot);
            // New authority arrives before its pending hits on the same reliable channel.
            // A generation change must not wait for the periodic retry timer.
            npcHits?.Flush();
        }

        private bool RecoveryDue()=>ServerWorld.pendingGrants.Count>0 || Time.unscaledTimeAsDouble-lastRecoveryAt>=RecoveryIntervalSeconds ||
            owners.Keys.Any(id=>!deliveredSnapshots.ContainsKey(id));
        // A server-world snapshot, composed from the tick composer after a slow tick (no world serialization).
        private RecoverySnapshot CaptureServerSnapshot()=>Saves.CaptureWorldSnapshot(ServerWorld,ComposeFromTicks);
        // The capture is a recovery point: its tick carries the template, and each client composes the same copy from
        // its tick texts, writes it in the background and acknowledges it (no snapshot deltas are sent).
        // recoveryPoint=false: a view that is not persisted (an entry preparation); clients must not write a copy the
        // host never archives (their acknowledgement requires the host's exact archive).
        private SnapshotPartCache.Composition ComposeFromTicks(WorldState world)=>ComposeFromTicks(world,true);
        private SnapshotPartCache.Composition ComposeFromTicks(WorldState world,bool recoveryPoint)
        {
            string template;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: шаблон")) template=WorldTickLists.Template(world);
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: тик")) FlushTick(true,recoveryPoint?template:null);
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: из тиков")) return tickComposer.Composition(template);
        }
        private void FlushTick(bool slow,string recovery=null)
        {
            if(ServerWorld==null) return;
            double now=Time.unscaledTimeAsDouble;
            lastTickAt=now;if(slow) lastSlowTickAt=now;
            WorldTick tick;
            using(Remains.Diagnostics.FrameHitchLog.Section("Тик мира: сборка")) tick=tickComposer.Next(ServerWorld,part=>WorldChanges.IsMarked(ServerWorld,part),slow,recovery!=null,
                part=>WorldChanges.IsMotionMarked(ServerWorld,part));
            if(tick!=null) tick.recovery=recovery;
            // The composer is the only consumer of marks (every server-world snapshot is composed from its texts).
            if(slow) WorldChanges.ClearState(ServerWorld);
            else foreach(var part in tickComposer.Consumed) WorldChanges.Unmark(ServerWorld,part);
            foreach(var part in tickComposer.MotionConsumed) WorldChanges.UnmarkMotion(ServerWorld,part);
            if(slow) WorldChanges.RetainMotion(ServerWorld,ServerWorld.actors);
            if(tick==null || tickReady.Count==0) return;
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Тик мира: отправка");
            byte[] payload=WorldTickSender.Encode(tick);
            int budget=NetworkMessages.MaxContentSize(Channels.Reliable);
            List<int> paused=null;
            foreach(int id in tickReady)
            {
                if(!NetworkServer.connections.TryGetValue(id,out var connection)) continue;
                int backlog=Backlog!=null?Backlog.ServerBacklog(id):0;
                if(backlog>=PauseBacklog) {(paused??=new List<int>()).Add(id);continue;}
                WorldTickSender.Send(payload,tick,budget,part=>connection.Send(part));
            }
            if(paused!=null) foreach(int id in paused)
            {
                tickReady.Remove(id);tickPaused.Add(id);
                Debug.LogWarning($"Тики мира к клиенту {id} приостановлены: в очереди отправки {Backlog.ServerBacklog(id)} пакетов, канал не успевает. "+
                    $"После разгрузки (меньше {ResumeBacklog}) клиент получит полный мир.",this);
            }
        }
        public ActorState ConnectedActor(NetworkConnectionToClient connection) =>
            owners.TryGetValue(connection.connectionId,out var owner) && playerIds.TryGetValue(owner,out var id)?ServerWorld.Find(id):null;
        // A commit whose follow-up messages do not depend on the snapshot preceding them (lamp break effects):
        // several in one frame (an explosion breaking many lamps) become one snapshot at the end of the frame.
        private bool commitAtFrameEnd;
        public void CommitWorldAtFrameEnd()=>commitAtFrameEnd=true;
        public override void LateUpdate()
        {
            base.LateUpdate();
            if(NetworkServer.active && ServerWorld!=null)
            {
                double now=Time.unscaledTimeAsDouble;
                if(now-lastTickAt>=TickIntervalSeconds) FlushTick(now-lastSlowTickAt>=SlowTickIntervalSeconds);
                if(tickPaused.Count>0) ResumeDrainedTicks();
            }
            if(predictions.Count>0 && NetworkClient.active) SettlePredictions(Time.unscaledTime);
            if(!commitAtFrameEnd) return;
            commitAtFrameEnd=false;
            if(NetworkServer.active && ServerWorld!=null) CommitWorld();
        }
        private void ResumeDrainedTicks()
        {
            List<int> drained=null;
            foreach(int id in tickPaused) if(Backlog==null || Backlog.ServerBacklog(id)<=ResumeBacklog) (drained??=new List<int>()).Add(id);
            if(drained==null) return;
            RecoverySnapshot snapshot=null;
            foreach(int id in drained)
            {
                tickPaused.Remove(id);
                if(!NetworkServer.connections.TryGetValue(id,out var connection)) continue;
                snapshot??=PrepareRecovery();
                Debug.Log($"Тики мира к клиенту {id} возобновлены: очередь разгружена, отправлен полный мир {snapshot.Id}.",this);
                SendFullWorld(connection,snapshot);
            }
        }
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
            CancelInvoke(nameof(CheckpointWorld));CancelInvoke(nameof(ExpireEntryPreparation));EntryPreparation.ResetServer();
            if (ServerWorld != null) Saves.SaveRecovery(CaptureServerSnapshot());
            LevelTime.ResetServer();
            Mechanisms.ResetServer();DoorDamage.ResetServer();Lamps.ResetServer();
            QueueStopNotification();
        }
        public override void OnStopClient()
        {
            recoil?.Clear();
            selfHealing.Reset();
            CharacterContent.Reset();ModMismatch?.Invoke(null);
            Admission.EndClient();
            EntryPreparation.ResetClient();Travel.ResetClient();Lobby.ResetClient();worldReplication.Forget();tickReceiver.Forget();predictions.Clear();
            Projectiles.ResetClient();
            SaveLocalCharacter();
            FlushSaveWrites();joinSelection=null;
            World=null;LocalActorId=null;LocalOwner=null;
            playTime.Stop();
            ContainerInspection.ResetClient();DoorDamage.ResetClient();Lamps.ResetClient();Doors.ResetClient();Pause.ResetClient();LevelTime.ResetClient();Mechanisms.ResetClient();
            RaiseChanged();
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
            FactoryContentSources.Clear();
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
