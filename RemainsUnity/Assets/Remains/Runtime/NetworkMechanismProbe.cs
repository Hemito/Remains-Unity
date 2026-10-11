using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // These messages coordinate evidence barriers only. Gameplay uses ordinary E/held E,
    // SessionTravel and SessionPause; neither message mutates canonical mechanisms.
    public struct MechanismProbeStage : NetworkMessage {public int stage;public string snapshot,source;public double repairProgress;}
    public struct MechanismProbeObserved : NetworkMessage {public int stage;public bool success;public string observer,snapshot,detail;}
    public struct MechanismProbeFinish : NetworkMessage {public bool success;public string detail;}

    [DefaultExecutionOrder(10000)]
    public sealed class NetworkMechanismProbe : MonoBehaviour
    {
        private const string Level="factory:0";
        private const string ButtonId=Level+":original:plant:18:p7r4AoLcL0QxKFy7";
        private const string PlatformId=Level+":original:plant:18:TFAERLMCSehXRcAt";
        // A bounded initial clock/boarding correction is measurable evidence, not a
        // fixture relocation. Record the error; a passenger still must traverse the route.
        private const float CarryTolerance=.3f;
        private enum Stage {None,Prepared,Repair,RepairCapture,RepairContinue,Ready,Activate,Board,Ride,Pause,Resume,Arrived}
        private PrototypeRuntime runtime;
        private Stage stage,serverStage;
        private float deadline,stageAt,freezeAt,arrivalAt=-1;
        private bool finished,travelRequested,fixture,boarded,pressed,reported,pauseRequested,pauseBaseline,pauseVerified,rideStarted;
        private string expectedSnapshot,serverSnapshot,sourceActor;
        private double capturedRepairProgress,pausedClock,rideClock,boardClock,durationSeconds,expectedRemainingSeconds,arrivalClock;
        private Vector2 fixturePosition,boardingPosition,boardingAnchor,pausedPosition,pausedAnchor;
        private long initialButtonRevision,initialPlatformRevision,segmentSequence;
        private int initialTransactions,contactSamples,movingSamples,repairEEdges,activationEEdges;
        private double localRepairProgressAtCapture;
        private float maximumRelativeError,maximumHeightError,pauseClockError,pausePositionError,pausePlatformError;
        private readonly Dictionary<string,DurableProof> snapshots=new(StringComparer.Ordinal);
        private readonly List<DurableProof> durableProofs=new();
        private readonly List<MotionPoint> motion=new();
        private readonly Queue<CarrierFrame> carrierFrames=new();
        private readonly Queue<ClockEpoch> clockEpochs=new();
        private readonly HashSet<int> observed=new();
        private readonly List<string> captures=new();
        private string latestSnapshot;
        private double nextTrace;
        private Vector2 previousCarrierPosition,previousCarrierAnchor;
        private double previousCarrierClock;
        private WorldState previousCarrierWorld;
        private string failureCaptureError;
        private BoardingEvidence boardingEvidence;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Actor=>runtime.Session.LocalActor;
        private MechanismButtonState Button=>runtime.Session.World?.mechanismButtons.Find(b=>b.id==ButtonId);
        private MovingPlatformState Platform=>runtime.Session.World?.movingPlatforms.Find(p=>p.id==PlatformId);
        private double Clock=>runtime.Session.LevelTime.ReadLocal(Level);
        private bool Paused=>runtime.Session.Pause.IsPaused(Level);

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+120;
            if(value.Options.ProbePeers!=2 || !value.Session.DebugTravelAllowed || value.Hud==null ||
                !value.Options.Smoke.Contains("-frames-") || string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report))
                throw new InvalidOperationException("Проверка механизмов требует двух участников, разрешённый probe-переход, HUD, кадры и отдельные сохранения.");
            if(Screen.width!=1920 || Screen.height!=1080)
                throw new InvalidOperationException($"Проверка механизмов требует 1920×1080; получено {Screen.width}×{Screen.height}.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка механизмов не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            value.CommandSource=Command;
            value.Session.RecoveryPersisted+=RecoveryPersisted;
            value.Session.LevelTime.EpochChanged+=ClockEpochChanged;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<MechanismProbeStage>(Begin);
                NetworkClient.RegisterHandler<MechanismProbeFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<MechanismProbeObserved>(AcceptObserved);
        }

        private void ClockEpochChanged()
        {
            if(finished || !runtime.Session.LevelTime.HasLocal(Level)) return;
            clockEpochs.Enqueue(new ClockEpoch {stage=(int)stage,frame=Time.frameCount,realSeconds=Time.unscaledTime,
                networkSeconds=NetworkTime.time,networkLocalSeconds=NetworkTime.localTime,networkRtt=NetworkTime.rtt,
                clockSequence=runtime.Session.LevelTime.ClientSequence,levelSeconds=Clock,
                running=runtime.Session.LevelTime.RunsLocal(Level),paused=Paused,pauseRevision=runtime.Session.Pause.Revision});
            while(clockEpochs.Count>80) clockEpochs.Dequeue();
        }
        private void RecoveryPersisted(RecoverySnapshot snapshot)
        {
            if(finished) return;
            try
            {
                if(snapshot==null) throw new InvalidOperationException("Событие завершённой записи не содержит снимок.");
                // Only the exact immutable archive proves this completed write. The selected
                // world.json may already name a newer queued checkpoint on either replica.
                string archive=Path.Combine("Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
                string path=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,archive);
                if(!File.Exists(path) || File.ReadAllText(path)!=snapshot.Json)
                    throw new InvalidOperationException($"Архив завершённого снимка не совпал: world={snapshot.WorldId}, snapshot={snapshot.Id}, path={path}.");
                latestSnapshot=snapshot.Id;
                var world=snapshot.CreateLiveWorld();
                var button=world.mechanismButtons.Find(b=>b.id==ButtonId);
                var platform=world.movingPlatforms.Find(p=>p.id==PlatformId);
                if(button==null || platform==null) return;
                snapshots[snapshot.Id]=new DurableProof {identityHash=snapshot.Id,world=snapshot.WorldId,worldRevision=snapshot.Revision,
                    button=button.id,platform=platform.id,repairState=button.repairState,buttonRevision=button.revision,pressed=button.pressed,
                    platformRevision=platform.revision,endpoint=platform.endpoint,segmentSequence=platform.segment?.sequence??0,
                    segmentFrom=platform.segment?.fromEndpoint??-1,segmentTo=platform.segment?.toEndpoint??-1,
                    mechanismTransactions=world.completedTransactions.Count(id=>id.StartsWith("mechanism:",StringComparison.Ordinal)),
                    observedArchive=true,archive=archive};
            }
            catch(Exception error) {Fail("Не подтверждён точный архив recovery: "+error.Message);}
        }
        private void Advance(Stage next,string snapshot=null,double progress=0)
        {
            serverStage=next;serverSnapshot=snapshot;observed.Clear();
            Debug.Log($"Проверка платформы: server stage={next}, snapshot={snapshot}, deadline={deadline}.");
            NetworkServer.SendToAll(new MechanismProbeStage {stage=(int)next,snapshot=snapshot,source=runtime.Session.LocalActorId,repairProgress=progress});
        }
        private void Begin(MechanismProbeStage message)
        {
            stage=(Stage)message.stage;expectedSnapshot=message.snapshot;sourceActor=message.source;
            if(stage==Stage.RepairCapture) capturedRepairProgress=message.repairProgress;
            stageAt=Time.unscaledTime;pressed=reported=pauseRequested=pauseBaseline=false;
            if(stage==Stage.Ready || stage==Stage.Board || stage==Stage.Arrived)
                Debug.Log($"Проверка платформы: observer={runtime.Session.LocalActorId}, stage={stage}, required snapshot={expectedSnapshot}.");
        }
        private void AcceptObserved(NetworkConnectionToClient connection,MechanismProbeObserved message)
        {
            var sender=runtime.Session.ConnectedActor(connection);
            if(sender==null || sender.id!=message.observer) return;
            if(!message.success)
            {NetworkServer.SendToAll(new MechanismProbeFinish {success=false,detail=message.detail});return;}
            if(message.stage!=(int)serverStage) return;
            if(serverSnapshot!=null && message.snapshot!=serverSnapshot)
            {Fail("Участник подтвердил другую durable identity: "+message.observer+"/"+message.snapshot);return;}
            if(!observed.Add(connection.connectionId) || observed.Count!=2) return;
            switch(serverStage)
            {
                case Stage.Prepared:Advance(Stage.Repair);break;
                case Stage.RepairCapture:runtime.Session.Pause.SetManual(false);Advance(Stage.RepairContinue);break;
                case Stage.Ready:Advance(Stage.Activate);break;
                case Stage.Board:runtime.Session.Pause.SetManual(false);Advance(Stage.Ride);break;
                case Stage.Pause:runtime.Session.Pause.SetManual(false);Advance(Stage.Resume);break;
                case Stage.Arrived:NetworkServer.SendToAll(new MechanismProbeFinish {success=true,
                    detail="Авторская платформа: удержание E, подтверждённый ремонт без запуска, отдельное E, обычная перевозка двух владельцев, остановка локальных часов/опор и durable identity обеих реплик."});break;
                default:Fail("Получен неожиданный барьер: "+serverStage);break;
            }
        }
        private PlayerCommand Command()
        {
            if(finished || Actor==null || !fixture) return default;
            bool repairing=stage is Stage.Repair or Stage.RepairCapture or Stage.RepairContinue;
            if(Host && (repairing || stage==Stage.Activate))
            {
                var point=runtime.MechanismInteraction.Points.Single(p=>p.Button.id==ButtonId);
                bool edge=!pressed && stage is Stage.Repair or Stage.Activate;
                if(edge) {pressed=true;if(stage==Stage.Repair) repairEEdges++;else activationEEdges++;}
                return new PlayerCommand {hasAim=true,aim=point.Bounds.center,interact=edge,revive=repairing};
            }
            return default; // Ordinary idle controller must carry each locally owned passenger.
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline || stage!=Stage.None && Time.unscaledTime-stageAt>20)
                    throw new InvalidOperationException("Таймаут фазы механизмов.");
                var actor=Actor;if(actor==null || runtime.Session.World==null) return;
                if(actor.owner!=runtime.Session.LocalOwner || !actor.connected || actor.down || actor.health<=0)
                    throw new InvalidOperationException("Проверка потеряла активного локального владельца персонажа.");
                if(runtime.Session.World.actors.Count(a=>a.player && a.connected)!=2) return;
                if(!travelRequested)
                {travelRequested=true;runtime.Session.Travel.RequestFactory();return;}
                if(actor.level!=Level || runtime.Session.Travel.Pending) return;
                if(!fixture) PrepareFixture();
                if(Host && serverStage==Stage.None) Advance(Stage.Prepared);
                if(Host) Coordinate();
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void PrepareFixture()
        {
            if(WorldRooms.Definition(runtime.Session.World,Level)!=LegacyFactoryFixture.DefinitionId)
                throw new InvalidOperationException("Проверка получила legacy-завод вместо авторского NewGameWorld; исключите mechanism из LegacyPrototypeFixture.");
            var button=Button??throw new InvalidOperationException("Нет авторской левой кнопки "+ButtonId);
            var platform=Platform??throw new InvalidOperationException("Нет авторской платформы "+PlatformId);
            if(button.repairState!=MechanismRepairStates.NeedsRepair || button.pressed || platform.endpoint!=0 || platform.segment!=null)
                throw new InvalidOperationException("Начальное состояние механизмов не соответствует новому миру.");
            var room=runtime.Geometry.Get(Level);var hull=Anatomy.Movement(Actor);
            var type=MechanismCatalogAsset.Load().Button(button.definition).CreateDefinition();
            durationSeconds=MechanismCatalogAsset.Load().Platform(platform.definition).CreateDefinition().durationSeconds;
            if(Math.Abs(durationSeconds-6)>.001) throw new InvalidOperationException("Проверка ожидает временный авторский маршрут за 6 секунд; catalog duration="+durationSeconds);
            float x=button.x+(Host?-.3f:.3f);
            var candidates=room.platforms.Where(s=>x+hull.width*.5f>s.x && x-hull.width*.5f<s.x+s.width)
                .Select(s=>new Vector2(x,RoomSupport.Top(s,x,hull.width)))
                .Where(p=>ActorTerrainMotion.IsClear(room,hull,p) && ActorTerrainMotion.Supported(room,hull,p) &&
                    Vector2.Distance(p,new Vector2(button.x,button.y))<=type.interactionRadius)
                .OrderBy(p=>Vector2.Distance(p,new Vector2(button.x,button.y))).ToArray();
            if(candidates.Length==0) throw new InvalidOperationException($"Нет свободной авторской опоры у button={button.id}, anchor=({button.x},{button.y}), x={x}, radius={type.interactionRadius}.");
            fixturePosition=candidates[0];PlaceOwnPlayer(fixturePosition);
            initialButtonRevision=button.revision;initialPlatformRevision=platform.revision;
            initialTransactions=runtime.Session.World.completedTransactions.Count(id=>id.StartsWith("mechanism:",StringComparison.Ordinal));
            fixture=true;stageAt=Time.unscaledTime;
        }
        private void PlaceOwnPlayer(Vector2 position)
        {
            var actor=Actor;
            if(actor==null || actor.owner!=runtime.Session.LocalOwner || actor.level!=Level)
                throw new InvalidOperationException("Fixture может ставить только собственного персонажа после подтверждённого перехода.");
            actor.Position=position;actor.vx=actor.vy=0;actor.revision++;runtime.Session.PublishActor(actor);
        }
        private void Coordinate()
        {
            if(stage!=serverStage) return;
            if(stage==Stage.Repair && runtime.MechanismInteraction.Repairing && runtime.MechanismInteraction.Progress>=.2)
            {
                if(!pauseRequested) {pauseRequested=true;runtime.Session.Pause.SetManual(true);}
                if(Paused && !runtime.Session.LevelTime.RunsLocal(Level))
                    Advance(Stage.RepairCapture,null,runtime.MechanismInteraction.Progress);
            }
            else if(stage==Stage.RepairContinue && Button.repairState==MechanismRepairStates.Ready &&
                !runtime.MechanismInteraction.Pending && !runtime.MechanismInteraction.Repairing)
            {
                string hash=FindDurable(Stage.Ready);
                if(hash!=null) Advance(Stage.Ready,hash);
            }
            else if(stage==Stage.Activate && Platform.segment!=null && !runtime.MechanismInteraction.Pending)
            {
                if(!pauseRequested) {pauseRequested=true;runtime.Session.Pause.SetManual(true);}
                string hash=FindDurable(Stage.Board);
                if(Paused && !runtime.Session.LevelTime.RunsLocal(Level) && hash!=null) Advance(Stage.Board,hash);
            }
            else if(stage==Stage.Ride && rideStarted && Clock-rideClock>=2)
            {
                if(!pauseRequested) {pauseRequested=true;runtime.Session.Pause.SetManual(true);}
                if(Paused && !runtime.Session.LevelTime.RunsLocal(Level)) Advance(Stage.Pause);
            }
            else if(stage==Stage.Resume && Platform.segment==null && Platform.endpoint==1)
            {
                string hash=FindDurable(Stage.Arrived);
                if(hash!=null) Advance(Stage.Arrived,hash);
            }
        }
        private string FindDurable(Stage expected)
        {
            if(latestSnapshot!=null && snapshots.TryGetValue(latestSnapshot,out var proof) && DurableMatches(proof,expected)) return latestSnapshot;
            return null;
        }
        private bool DurableMatches(DurableProof proof,Stage expected)
        {
            if(proof.world!=runtime.Session.World.worldId || proof.repairState!=MechanismRepairStates.Ready) return false;
            if(expected==Stage.Ready) return !proof.pressed && proof.buttonRevision==initialButtonRevision+1 &&
                proof.platformRevision==initialPlatformRevision && proof.segmentSequence==0 && proof.endpoint==0 && proof.mechanismTransactions==initialTransactions+1;
            if(expected==Stage.Board) return proof.pressed && proof.buttonRevision==initialButtonRevision+2 &&
                proof.platformRevision==initialPlatformRevision+1 && proof.segmentSequence==initialPlatformRevision+1 &&
                proof.endpoint==0 && proof.segmentFrom==0 && proof.segmentTo==1 && proof.mechanismTransactions==initialTransactions+2;
            return proof.pressed && proof.buttonRevision==initialButtonRevision+2 && proof.platformRevision==initialPlatformRevision+2 &&
                proof.segmentSequence==0 && proof.endpoint==1 && proof.mechanismTransactions==initialTransactions+2;
        }
        private bool DurableReady()
        {
            if(expectedSnapshot==null || !snapshots.TryGetValue(expectedSnapshot,out var proof)) return false;
            if(!DurableMatches(proof,stage)) throw new InvalidOperationException("Снимок барьера содержит неверное состояние механизмов: "+StateJson.ToJson(proof));
            if(!durableProofs.Any(p=>p.stage==(int)stage))
            {var copy=StateJson.Copy(proof);copy.stage=(int)stage;durableProofs.Add(copy);
                File.WriteAllText(runtime.Options.Report+$".phase-{(int)stage}.durable.json",StateJson.ToJson(copy,true));}
            return true;
        }
        private void LateUpdate()
        {
            if(finished || !fixture || stage==Stage.None || Actor?.level!=Level) return;
            try
            {
                if(boarded) ObservePassenger();
                if(reported) return;
                switch(stage)
                {
                    case Stage.Prepared:
                        if(Time.unscaledTime-stageAt<.5f) return;
                        if(MechanismButtonAccess.Rejection(Actor,Button,MechanismCatalogAsset.Load().Button(Button.definition).CreateDefinition().interactionRadius)!=null)
                            throw new InvalidOperationException("Fixture вне радиуса кнопки.");
                        Observe();break;
                    case Stage.RepairCapture:
                        if(!Paused || runtime.Session.LevelTime.RunsLocal(Level) || Time.unscaledTime-stageAt<.4f) return;
                        if(Button.repairState!=MechanismRepairStates.NeedsRepair || Platform.segment!=null || capturedRepairProgress<=0 || capturedRepairProgress>=1 ||
                            Host && (!runtime.MechanismInteraction.Repairing || Math.Abs(runtime.MechanismInteraction.Progress-capturedRepairProgress)>.05))
                            throw new InvalidOperationException("Удержание E не сохранило частичный ремонт на общей паузе.");
                        localRepairProgressAtCapture=runtime.MechanismInteraction.Progress;
                        Capture("repair");Observe();break;
                    case Stage.Ready:
                        if(!DurableReady() || Time.unscaledTime-stageAt<.6f) return;
                        if(Button.repairState!=MechanismRepairStates.Ready || Button.pressed || Platform.segment!=null || Platform.endpoint!=0)
                            throw new InvalidOperationException("Ремонт сам запустил платформу до нового E.");
                        Observe();break;
                    case Stage.Board:
                        if(!Paused || runtime.Session.LevelTime.RunsLocal(Level) || !DurableReady() || Time.unscaledTime-stageAt<.3f) return;
                        if(!boarded)
                        {
                            var sample=Sample();double clock=Clock;var platform=Platform;
                            ValidateBoarding(platform,sample,clock,initialPlatformRevision+1);
                            boardingPosition=new Vector2(sample.Anchor.x+(Host?-.3f:.3f),sample.SupportBounds.yMax);
                            if(!ActorTerrainMotion.IsClear(runtime.Geometry.Get(Level),Anatomy.Movement(Actor),boardingPosition))
                                throw new InvalidOperationException("Над движущейся опорой нет свободного места для fixture.");
                            PlaceOwnPlayer(boardingPosition);boardingAnchor=sample.Anchor;boardClock=clock;
                            expectedRemainingSeconds=platform.segment.EndElapsedSeconds-clock;
                            segmentSequence=platform.segment.sequence;
                            boardingEvidence=new BoardingEvidence {observer=Actor.id,world=runtime.Session.World.worldId,platform=platform.id,
                                snapshot=expectedSnapshot,localSeconds=clock,startSeconds=platform.segment.startElapsedSeconds,
                                endSeconds=platform.segment.EndElapsedSeconds,prestartSeconds=Math.Max(0,platform.segment.startElapsedSeconds-clock),
                                expectedRemainingSeconds=expectedRemainingSeconds,progress=sample.Progress,started=sample.Started,completed=sample.Completed,
                                sampledAnchor=sample.Anchor,fromEndpoint=platform.segment.fromEndpoint==0?platform.Start:platform.End,
                                fixture=boardingPosition,segmentSequence=segmentSequence,platformRevision=platform.revision,
                                clockSequence=runtime.Session.LevelTime.ClientSequence,pauseRevision=runtime.Session.Pause.Revision,paused=Paused};
                            File.WriteAllText(runtime.Options.Report+".phase-7.boarding.json",StateJson.ToJson(boardingEvidence,true));
                            boarded=true;return;
                        }
                        if(Time.unscaledTime-stageAt<.6f) return;
                        Observe();break;
                    case Stage.Ride:
                        if(!Paused && runtime.Session.LevelTime.RunsLocal(Level) && !rideStarted)
                        {rideStarted=true;rideClock=Clock;}
                        break;
                    case Stage.Pause:
                        if(!Paused || runtime.Session.LevelTime.RunsLocal(Level) || Time.unscaledTime-stageAt<.4f) return;
                        if(!pauseBaseline)
                        {pauseBaseline=true;pausedClock=Clock;pausedPosition=Actor.Position;pausedAnchor=Sample().Anchor;freezeAt=Time.unscaledTime;return;}
                        pauseClockError=Math.Max(pauseClockError,(float)Math.Abs(Clock-pausedClock));
                        pausePositionError=Math.Max(pausePositionError,Vector2.Distance(Actor.Position,pausedPosition));
                        pausePlatformError=Math.Max(pausePlatformError,Vector2.Distance(Sample().Anchor,pausedAnchor));
                        if(pauseClockError>.002f || pausePositionError>.03f || pausePlatformError>.01f)
                            throw new InvalidOperationException("Общая пауза не заморозила локальные часы, пассажира и опору.");
                        if(Time.unscaledTime-freezeAt<.8f) return;
                        if(Sample().Progress<.2 || Sample().Progress>.85 || Platform.segment?.sequence!=segmentSequence)
                            throw new InvalidOperationException("Середина пути или sequence изменились на паузе.");
                        pauseVerified=true;Capture("moving");Observe();break;
                    case Stage.Arrived:
                        if(!DurableReady() || Platform.segment!=null || Platform.endpoint!=1) return;
                        // The exact canonical completion can precede continuous local playback.
                        // Wait for this observer's actual support, then keep the same assertions.
                        var arrivalSample=Sample();
                        if(arrivalSample.Moving || Vector2.Distance(arrivalSample.Anchor,Platform.End)>.001f) return;
                        if(arrivalAt<0) {arrivalAt=Time.unscaledTime;arrivalClock=Clock;return;}
                        if(Time.unscaledTime-arrivalAt<.5f) return;
                        float expected=Platform.End.x-boardingAnchor.x;
                        float actual=Actor.x-boardingPosition.x;
                        if(Math.Abs(actual-expected)>CarryTolerance || expected<15 || actual<15 || Math.Abs(Platform.End.x-Platform.Start.x-16.8f)>.02f ||
                            contactSamples<30 || movingSamples<20 || !rideStarted || Math.Abs(arrivalClock-boardClock-expectedRemainingSeconds)>.5)
                            throw new InvalidOperationException($"Нет независимого доказательства перевозки: displacement={actual}, expected={expected}, contacts={contactSamples}, moving={movingSamples}, clock={Clock-boardClock}.");
                        Capture("arrived");Observe();break;
                }
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private MovingPlatformSample Sample()=>runtime.Platforms.TrySample(PlatformId,out var sample)?sample:
            throw new InvalidOperationException("Runtime не построил фактическую опору платформы "+PlatformId);
        private static void ValidateBoarding(MovingPlatformState platform,MovingPlatformSample sample,double clock,long expectedRevision)
        {
            var segment=platform?.segment;
            if(platform!=null && segment!=null) platform.Validate();
            if(platform==null || platform.id!=PlatformId || platform.removed || segment==null ||
                platform.revision!=expectedRevision || segment.sequence!=expectedRevision || platform.endpoint!=0 ||
                segment.fromEndpoint!=0 || segment.toEndpoint!=1 || !double.IsFinite(clock) || clock<0 ||
                !double.IsFinite(sample.Progress) || sample.Progress<0 || sample.Progress>.1 || sample.Completed || clock>=segment.EndElapsedSeconds)
                throw new InvalidOperationException($"Посадка не получила ранний подтверждённый segment: platform={platform?.id}, revision={platform?.revision}/{expectedRevision}, sequence={segment?.sequence}, clock={clock}, start/end={segment?.startElapsedSeconds}/{segment?.EndElapsedSeconds}, progress={sample.Progress}, started={sample.Started}, completed={sample.Completed}.");
            bool prestart=clock<segment.startElapsedSeconds;
            var from=segment.fromEndpoint==0?platform.Start:platform.End;
            if(prestart && (sample.Started || sample.Progress!=0 || Vector2.Distance(sample.Anchor,from)>.0001f) ||
                !prestart && !sample.Started)
                throw new InvalidOperationException($"Локальная опора не совпала с границей start: clock={clock}, start={segment.startElapsedSeconds}, prestart={prestart}, started={sample.Started}, progress={sample.Progress}, anchor={sample.Anchor}, from={from}.");
        }
        private void ObservePassenger()
        {
            var sample=Sample();var actor=Actor;
            float relative=Math.Abs((actor.x-sample.Anchor.x)-(boardingPosition.x-boardingAnchor.x));
            float height=Math.Abs(actor.y-sample.SupportBounds.yMax);
            maximumRelativeError=Math.Max(maximumRelativeError,relative);maximumHeightError=Math.Max(maximumHeightError,height);
            contactSamples++;if(sample.Moving && !Paused) movingSamples++;
            var recorded=RecordCarrierFrame(sample,relative,height);
            bool lost=relative>CarryTolerance || height>.12f;
            if(lost || Time.unscaledTime>=nextTrace)
            {
                nextTrace=Time.unscaledTime+.1;
                motion.Add(new MotionPoint {stage=(int)stage,realSeconds=Time.unscaledTime,levelSeconds=recorded.levelSeconds,actor=actor.Position,
                    platform=sample.Anchor,relativeError=relative,heightError=height,paused=Paused,frozenWindow=stage==Stage.Pause && pauseBaseline,
                    sequence=Platform.segment?.sequence??0});
            }
            // Record the failing frame before rejecting it. No correction or fixture rebase.
            if(lost)
                throw new InvalidOperationException($"Обычный контроллер потерял относительный контакт: actor={actor.id}, relativeError={relative}, heightError={height}, player={actor.Position}, anchor={sample.Anchor}, bounds={sample.SupportBounds}, frame={recorded.frame}, clockSequence={recorded.clockSequence}, physicsContinuous={recorded.physicsContinuous}, physicsClock={recorded.physicsPreviousSeconds}->{recorded.physicsSeconds}, delta={recorded.deltaSeconds}, clockAdvance={recorded.clockAdvance}, sampleSkew={recorded.clockSampleSkew}.");
        }
        private CarrierFrame RecordCarrierFrame(MovingPlatformSample sample,float relative,float height)
        {
            // Bounded scalar observations of the actual published frame. This does not
            // evaluate platform motion, rebuild geometry, read assets or alter a passenger.
            var actor=Actor;var world=runtime.Session.World;double clock=Clock;
            bool prior=carrierFrames.Count>0;
            var frame=new CarrierFrame {stage=(int)stage,frame=Time.frameCount,realSeconds=Time.unscaledTime,
                deltaSeconds=Time.deltaTime,unscaledDeltaSeconds=Time.unscaledDeltaTime,levelSeconds=clock,
                clockAdvance=prior?clock-previousCarrierClock:0,clockSequence=runtime.Session.LevelTime.ClientSequence,
                networkSeconds=NetworkTime.time,networkLocalSeconds=NetworkTime.localTime,networkRtt=NetworkTime.rtt,
                clockRunning=runtime.Session.LevelTime.RunsLocal(Level),paused=Paused,pauseRevision=runtime.Session.Pause.Revision,
                worldRevision=world.revision,worldReplaced=prior && !ReferenceEquals(world,previousCarrierWorld),
                geometryTopologyVersion=runtime.Geometry.TopologyVersion,actor=actor.Position,platform=sample.Anchor,
                actorStep=prior?actor.Position-previousCarrierPosition:Vector2.zero,
                platformStep=prior?sample.Anchor-previousCarrierAnchor:Vector2.zero,
                actorMotionSequence=actor.motionSequence,actorRevision=actor.revision,velocity=new Vector2(actor.vx,actor.vy),
                relativeError=relative,heightError=height,progress=sample.Progress,segmentSequence=Platform.segment?.sequence??0};
            var published=runtime.Geometry.Get(Level).dynamicSurfaces;
            if(published!=null)
            {
                frame.physicsPublished=published.IsPublished;frame.physicsSequence=published.Sequence;
                frame.physicsTopologyVersion=published.TopologyVersion;frame.physicsPreviousSeconds=published.PreviousElapsedSeconds;
                frame.physicsSeconds=published.ElapsedSeconds;frame.clockSampleSkew=clock-published.ElapsedSeconds;
                if(published.IsPublished)
                    for(int i=0;i<published.Count;i++)
                    {
                        var pose=published.Pose(i);if(pose.Id!=PlatformId) continue;
                        frame.physicsFound=true;frame.physicsContinuous=pose.Continuous;frame.physicsDelta=pose.Delta;
                        frame.physicsPrevious=new Vector2(pose.Previous.x,pose.Previous.y);
                        frame.physicsCurrent=new Vector2(pose.Current.x,pose.Current.y);break;
                    }
            }
            carrierFrames.Enqueue(frame);while(carrierFrames.Count>240) carrierFrames.Dequeue();
            previousCarrierClock=clock;previousCarrierPosition=actor.Position;previousCarrierAnchor=sample.Anchor;previousCarrierWorld=world;
            return frame;
        }
        private void Capture(string name)
        {
            NetworkSceneRecord.Save(runtime,(int)stage);
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".mechanism-"+name+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
            captures.Add(name);
        }
        private void Observe()
        {
            reported=true;NetworkClient.Send(new MechanismProbeObserved {stage=(int)stage,success=true,
                observer=Actor.id,snapshot=expectedSnapshot,detail=Context()});
        }
        private string Context()=> $"stage={stage}, server={serverStage}, actor={Actor?.id}, player={Actor?.Position}, health={Actor?.health}, damage={Actor?.damageSequence}, generation/lifecycle={Actor?.generation}/{Actor?.lifecycleSequence}, button={ButtonId}/{Button?.repairState}/{Button?.revision}, platform={PlatformId}/{Platform?.endpoint}/{Platform?.revision}, sequence={Platform?.segment?.sequence}, repair={runtime.MechanismInteraction.Progress}/{runtime.MechanismInteraction.Repairing}/{runtime.MechanismInteraction.Pending}, clock={(runtime.Session.LevelTime.HasLocal(Level)?Clock:-1)}, paused={Paused}, hash={expectedSnapshot}, latest={latestSnapshot}, observations={observed.Count}, deadline={deadline}";
        private void Fail(string reason)
        {
            if(finished) return;string detail=reason+" "+Context();
            if(NetworkClient.isConnected) NetworkClient.Send(new MechanismProbeObserved {stage=(int)stage,success=false,observer=Actor?.id,detail=detail});
            if(Host && NetworkServer.active) NetworkServer.SendToAll(new MechanismProbeFinish {success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            if(success && (stage!=Stage.Arrived || captures.Count!=3 || durableProofs.Count!=3 || !boarded || !pauseVerified))
            {success=false;detail="Неполный локальный набор доказательств. "+detail;}
            finished=true;
            string finishContext=Context();
            if(!success && boarded)
            {
                try {Capture("failed");}
                catch(Exception error)
                {failureCaptureError=error.Message;Debug.LogWarning("Не удалось снять кадр ошибки перевозки: "+error.Message,this);}
            }
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,context=finishContext,role=runtime.Options.Role,
                observer=Actor?.id,sourceActor=sourceActor,world=runtime.Session.World?.worldId,button=ButtonId,platform=PlatformId,debugTravel=true,
                fixturePosition=fixturePosition,boardingPosition=boardingPosition,boardingAnchor=boardingAnchor,
                playerEnd=Actor?.Position??default,routeDistance=Platform==null?0:Platform.End.x-Platform.Start.x,
                carriedDistance=Actor==null?0:Actor.x-boardingPosition.x,expectedCarriedDistance=Platform==null?0:Platform.End.x-boardingAnchor.x,
                segmentSequence=segmentSequence,contactSamples=contactSamples,movingSamples=movingSamples,
                maximumRelativeError=maximumRelativeError,maximumHeightError=maximumHeightError,
                pauseClockError=pauseClockError,pausePositionError=pausePositionError,pausePlatformError=pausePlatformError,
                durationSeconds=durationSeconds,expectedRemainingSeconds=expectedRemainingSeconds,arrivalObserved=arrivalAt>=0,
                observedRemainingSeconds=arrivalAt>=0?arrivalClock-boardClock:0,
                sourceRepairProgress=capturedRepairProgress,localRepairProgressAtCapture=localRepairProgressAtCapture,repairEEdges=repairEEdges,activationEEdges=activationEEdges,
                width=1920,height=1080,captures=captures.ToArray(),durable=durableProofs.ToArray(),motion=motion.ToArray(),
                carrierFrames=carrierFrames.ToArray(),clockEpochs=clockEpochs.ToArray(),failureCaptureError=failureCaptureError,
                boarding=boardingEvidence,clockPhaseLimitation="Непрерывные локальные часы могут иметь разную абсолютную фазу. Общая пауза фиксирует текущую фазу каждого участника; эта проверка не доказывает синхронность фаз."},true));
            Invoke(nameof(Exit),1.5f);exitSuccess=success;
        }
        private bool exitSuccess;
        private void Exit()=>Application.Quit(exitSuccess?0:2);
        private void OnDestroy()
        {
            if(runtime==null) return;
            runtime.Session.RecoveryPersisted-=RecoveryPersisted;
            runtime.Session.LevelTime.EpochChanged-=ClockEpochChanged;
        }
        [Serializable] private sealed class DurableProof
        {public int stage,endpoint,segmentFrom,segmentTo,mechanismTransactions;public string identityHash,world,button,platform,repairState,archive;
            public long worldRevision,buttonRevision,platformRevision,segmentSequence;public bool pressed,observedArchive;}
        [Serializable] private sealed class MotionPoint
        {public int stage;public double realSeconds,levelSeconds;public Vector2 actor,platform;public float relativeError,heightError;public bool paused,frozenWindow;public long sequence;}
        [Serializable] private sealed class ClockEpoch
        {public int stage,frame;public double realSeconds,networkSeconds,networkLocalSeconds,networkRtt,levelSeconds;public long clockSequence,pauseRevision;public bool running,paused;}
        [Serializable] private sealed class BoardingEvidence
        {public string observer,world,platform,snapshot;public double localSeconds,startSeconds,endSeconds,prestartSeconds,expectedRemainingSeconds,progress;
            public Vector2 sampledAnchor,fromEndpoint,fixture;public long segmentSequence,platformRevision,clockSequence,pauseRevision;public bool started,completed,paused;}
        [Serializable] private sealed class CarrierFrame
        {
            public int stage,frame;
            public double realSeconds,levelSeconds,clockAdvance,clockSampleSkew,networkSeconds,networkLocalSeconds,networkRtt,
                physicsPreviousSeconds,physicsSeconds,progress;
            public float deltaSeconds,unscaledDeltaSeconds,relativeError,heightError;
            public long clockSequence,pauseRevision,worldRevision,geometryTopologyVersion,actorMotionSequence,actorRevision,
                segmentSequence,physicsSequence,physicsTopologyVersion;
            public Vector2 actor,platform,actorStep,platformStep,velocity,physicsPrevious,physicsCurrent,physicsDelta;
            public bool clockRunning,paused,worldReplaced,physicsPublished,physicsFound,physicsContinuous;
        }
        [Serializable] private sealed class Report
        {public bool success,debugTravel,arrivalObserved;public string detail,context,role,observer,sourceActor,world,button,platform;public Vector2 fixturePosition,boardingPosition,boardingAnchor,playerEnd;
            public float routeDistance,carriedDistance,expectedCarriedDistance,maximumRelativeError,maximumHeightError,pauseClockError,pausePositionError,pausePlatformError;
            public double sourceRepairProgress,localRepairProgressAtCapture,durationSeconds,expectedRemainingSeconds,observedRemainingSeconds;
            public int contactSamples,movingSamples,repairEEdges,activationEEdges,width,height;public long segmentSequence;public string[] captures;public DurableProof[] durable;public MotionPoint[] motion;
            public CarrierFrame[] carrierFrames;public ClockEpoch[] clockEpochs;public string failureCaptureError,clockPhaseLimitation;public BoardingEvidence boarding;}
    }
}
