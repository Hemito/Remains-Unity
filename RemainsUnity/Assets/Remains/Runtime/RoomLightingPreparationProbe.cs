using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Diagnostics;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct LightingPreparationObserved : NetworkMessage
    {public string actor,world,level,foreground,background;public int phase,generation;}
    public struct LightingPreparationRelease : NetworkMessage {public int phase;}

    // An input-only visit to the saved hub/map/factory. No placement fixture or light override.
    [DefaultExecutionOrder(10000)]
    public sealed class RoomLightingPreparationProbe : MonoBehaviour
    {
        [Serializable] private sealed class FrameObservation
        {
            public int frame,cullingMask;public double elapsed;public float dt;public Vector2 actorPosition;
            public bool isReady,isUpdating,pending,loading,inputBlocked,cameraEnabled,presentationReady,inventoryOpen;
        }
        [Serializable] private sealed class Visit
        {
            public string name,world,actor,level,room;public int generation,pendingFrames,readyFrame;
            public double observedAt,readyAt,initialPrepareMilliseconds,getPixelsMilliseconds,decodeMilliseconds,evaluateMilliseconds,
                cpuMilliseconds,uploadMilliseconds,totalBuildMilliseconds,captureMilliseconds;
            public float minimumDt,maximumDt,cleanMinimumDt,cleanMaximumDt;public int cleanFrames;public long revision;
            public bool loadingCaptured,gatesVerified,inventoryGateAttempted,inventoryGateRejected;
            public RoomLightingGpuBounds loadingRect;
            public FrameObservation[] frames;public RoomLightingGpuResult gpu;
            [NonSerialized] public RoomLighting source;
            [NonSerialized] public readonly List<FrameObservation> trace=new();
        }
        [Serializable] private sealed class Report
        {
            public bool success,noFixture,ordinaryMapEntry,ordinaryDoorExit;public string detail,world,actor,level;
            public int phase;public Vector2 position;public double synchronousBudgetMilliseconds;public Visit[] visits;
            public TransitionTimingSample[] transitionTimings;
            public PlayTimeWrite[] playTimeWrites;
        }
        [Serializable] private sealed class PlayTimeWrite
        {
            public string profileDirectory;
            public double elapsedSeconds,loadMilliseconds,writeMilliseconds,persistMilliseconds;
            public bool persisted;
        }
        private PrototypeRuntime runtime;
        private readonly List<Visit> visits=new();
        private readonly List<PlayTimeWrite> playTimeWrites=new();
        private readonly HashSet<string> factoryWitnesses=new(StringComparer.Ordinal),returnWitnesses=new(StringComparer.Ordinal);
        private Visit current;
        private TravelPoint map,exit;
        private RoomLadder ladder;
        private string world,actorId;
        private int phase,approach;
        private float deadline;
        private double syncBudget;
        private bool finished,comparing,loadingCapture,settledCapture,clickedMap,jumped,mapE,exitE,factoryReleased,returnReported;
        private bool ordinaryMapEntry,ordinaryDoorExit;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Actor=>runtime.Session.LocalActor;
        private string Progress=>$"phase={phase}, approach={approach}, actor={Actor?.id}, level={Actor?.level}, position={Actor?.Position}, health={Actor?.health}, preparing={runtime.IsLevelPreparing}, ready={runtime.LevelPresentationReady}, visit={current?.name}, pendingFrames={current?.pendingFrames}, travel={runtime.Session.Travel.Pending}.";

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+150;syncBudget=ReadBudget();
            if(value.Options.ProbePeers is <1 or >2 || value.Session.DebugTravelAllowed || string.IsNullOrEmpty(value.Options.Report) || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Подготовка света требует обычных переходов, нового каталога и 1–2 участников.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проба света не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Session.PlayTimePersisted+=ObservePlayTime;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<LightingPreparationRelease>(message=>
            {if(message.phase==1) factoryReleased=true;else if(message.phase==2) Finish(phase==10,"Обычная карта/дверь и три актуальные пары света проверены обеими репликами.");else Finish(false,"Неизвестный барьер света: "+message.phase);});
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<LightingPreparationObserved>((connection,message)=>
            {
                try
                {
                    var actor=value.Session.ConnectedActor(connection);
                    string expectedLevel=message.phase==1?"factory:0":message.phase==2?"hub":null;
                    if(actor==null || actor.id!=message.actor || actor.generation!=message.generation || actor.level!=expectedLevel ||
                        value.Session.ServerWorld.worldId!=message.world || message.level!=expectedLevel || string.IsNullOrEmpty(message.foreground) || string.IsNullOrEmpty(message.background))
                        throw new InvalidOperationException("Неверное свидетельство подготовки света: "+message.actor+"/"+message.phase);
                    var witnessed=message.phase==1?factoryWitnesses:returnWitnesses;witnessed.Add(actor.id);
                    if(witnessed.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new LightingPreparationRelease {phase=message.phase});
                }
                catch(Exception error) {Finish(false,error.Message);}
            });
        }
        private static double ReadBudget()
        {
            var args=Environment.GetCommandLineArgs();
            for(int index=0;index<args.Length-1;index++) if(args[index]=="--lighting-sync-budget-ms")
            {
                if(double.TryParse(args[index+1],NumberStyles.Float,CultureInfo.InvariantCulture,out double result) && result>0 && result<2800) return result;
                throw new InvalidOperationException("Синхронный бюджет света должен быть конечным, положительным и меньше исходных 2800 мс.");
            }
            throw new InvalidOperationException("Пробе нужен явный --lighting-sync-budget-ms (предварительно 500).");
        }
        private PlayerCommand Command()
        {
            if(finished || Actor==null || !runtime.LevelPresentationReady || runtime.IsLevelPreparing) return default;
            if(phase==2)
            {
                if(runtime.Session.World.actors.Count(actor=>actor.player && actor.connected)!=runtime.Options.ProbePeers) return default;
                float target=ladder.x+ladder.movementOffset;
                if(approach==0)
                {
                    if(Mathf.Abs(Actor.x-target)>.06f) return Towards(target);
                    approach=1;
                }
                if(approach==1)
                {
                    if(Actor.y<map.Arrival.y+RoomSupport.ContactTolerance) return new PlayerCommand {movement=Vector2.up};
                    approach=2;
                }
                if(!jumped) {jumped=true;return new PlayerCommand {jump=true,movement=Vector2.right};}
                if(Mathf.Abs(Actor.x-map.Arrival.x)>.08f) return Towards(map.Arrival.x);
                if(map.Rejection(Actor)!=null || !ActorTerrainMotion.Supported(runtime.Geometry.Get("hub"),Anatomy.Movement(Actor),Actor.Position)) return default;
                var chosen=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,map.Bounds.center,runtime.Session.Travel.Points("hub"));
                if(chosen.kind!=InteractionKind.Map || chosen.id!=map.id) {Finish(false,"Курсор/E не выбирает настоящую карту: "+chosen.kind+"/"+chosen.id);return default;}
                mapE=true;phase=3;return new PlayerCommand {hasAim=true,aim=map.Bounds.center,interact=true};
            }
            if(phase==6 && factoryReleased && !exitE)
            {
                if(exit.Rejection(Actor)!=null) return Towards(exit.Arrival.x);
                var chosen=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,exit.Bounds.center,runtime.Session.Travel.Points("factory:0"));
                if(chosen.kind!=InteractionKind.Exit || chosen.id!=exit.id) {Finish(false,"Курсор/E не выбирает настоящую выходную дверь: "+chosen.kind+"/"+chosen.id);return default;}
                exitE=true;phase=7;return new PlayerCommand {hasAim=true,aim=exit.Bounds.center,interact=true};
            }
            return default;
        }
        private PlayerCommand Towards(float x)=>new() {movement=Vector2.right*Mathf.Sign(x-Actor.x)};
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут подготовки света: "+Progress);
                if(Actor==null || !NetworkClient.isConnected) return;
                if(Actor.down || Actor.health<=0) throw new InvalidOperationException("Игрок потерял управление в обычном маршруте: "+Progress);
                if(world==null) {world=runtime.Session.World.worldId;actorId=Actor.id;}
                if(world!=runtime.Session.World.worldId || actorId!=Actor.id) throw new InvalidOperationException("Маршрут сменил мир/персонажа: "+Progress);
                Observe();
                if(phase==0 && Actor.level=="hub" && Settled()) {phase=1;BeginComparison();}
                if(phase==3 && runtime.Hud.WorldMap.IsOpen && !clickedMap) {clickedMap=true;StartCoroutine(Checked(ChooseFactory(),"Карта"));}
                if(phase==4 && Actor.level=="factory:0" && !runtime.Session.Travel.Pending && Settled())
                {ordinaryMapEntry=mapE;phase=5;BeginComparison();}
                if(phase==7 && Actor.level=="hub" && !runtime.Session.Travel.Pending && Settled())
                {ordinaryDoorExit=exitE;phase=8;BeginComparison();}
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private bool Settled()=>current!=null && current.source==runtime.ActiveRoomLighting && current.source.IsReady && !current.source.IsUpdating &&
            runtime.LevelPresentationReady && !runtime.IsLevelPreparing && !runtime.Hud.RoomPresentationPending && !runtime.Hud.RoomLoadingVisible &&
            ReferenceEquals(current.source.Room,runtime.Geometry.Get(Actor.level)) && current.source.AppliedOcclusionRevision==runtime.Geometry.OcclusionRevision(Actor.level);
        private void Observe()
        {
            var source=runtime.ActiveRoomLighting;if(source==null) return;
            if(current==null || source!=current.source)
            {
                if(visits.Count>=3) throw new InvalidOperationException("Маршрут создал лишнюю подготовку света: "+Progress);
                current=new Visit {name=visits.Count==0?"hub-initial":Actor.level=="factory:0"?"factory":"hub-return",source=source,
                    world=world,actor=Actor.id,level=Actor.level,generation=Actor.generation,observedAt=Time.unscaledTimeAsDouble};visits.Add(current);
            }
            if(current.level!=Actor.level) throw new InvalidOperationException("Видимый свет сохранил другую локацию: "+Progress);
            if(!source.IsReady)
            {
                var camera=runtime.WorldCamera;bool pending=runtime.IsLevelPreparing && runtime.Hud.RoomPresentationPending;
                if(!source.IsUpdating || !pending || !runtime.Hud.RoomLoadingVisible || !runtime.Hud.BlocksGameplayInput || runtime.LevelPresentationReady ||
                    camera==null || !camera.enabled || camera.cullingMask!=0 || runtime.Hud.InventoryOpen)
                    throw new InvalidOperationException("Неготовая локация не защищена загрузкой/вводом/камерой: "+Progress);
                if(current.name=="factory" && !current.inventoryGateAttempted)
                {
                    current.inventoryGateAttempted=true;runtime.Hud.ToggleInventory();
                    current.inventoryGateRejected=!runtime.Hud.InventoryOpen && runtime.Hud.RoomPresentationPending;
                    if(!current.inventoryGateRejected) throw new InvalidOperationException("Настоящий Tab consumer открыл инвентарь во время подготовки: "+Progress);
                }
                current.pendingFrames++;current.gatesVerified=true;
                current.minimumDt=current.pendingFrames==1?Time.unscaledDeltaTime:Mathf.Min(current.minimumDt,Time.unscaledDeltaTime);
                current.maximumDt=Mathf.Max(current.maximumDt,Time.unscaledDeltaTime);
                if(!current.loadingCaptured)
                {
                    current.cleanFrames++;current.cleanMinimumDt=current.cleanFrames==1?Time.unscaledDeltaTime:Mathf.Min(current.cleanMinimumDt,Time.unscaledDeltaTime);
                    current.cleanMaximumDt=Mathf.Max(current.cleanMaximumDt,Time.unscaledDeltaTime);
                }
                if(current.trace.Count>=2048) throw new InvalidOperationException("Переполнено ограниченное свидетельство кадров подготовки: "+Progress);
                current.trace.Add(new FrameObservation {frame=Time.frameCount,elapsed=Time.unscaledTimeAsDouble,dt=Time.unscaledDeltaTime,actorPosition=Actor.Position,
                    isReady=source.IsReady,isUpdating=source.IsUpdating,pending=pending,loading=runtime.Hud.RoomLoadingVisible,inputBlocked=runtime.Hud.BlocksGameplayInput,
                    cullingMask=camera.cullingMask,cameraEnabled=camera.enabled,presentationReady=runtime.LevelPresentationReady,inventoryOpen=runtime.Hud.InventoryOpen});
                if(current.name=="factory" && !current.loadingCaptured) loadingCapture=true;
            }
        }
        private IEnumerator ChooseFactory()
        {
            yield return UiPointerProbe.Click(runtime.Hud.WorldMap.FactoryRect);
            if(!runtime.Hud.WorldMap.CanTravel) {Finish(false,"Настоящая карта не разрешает выбранный завод.");yield break;}
            phase=4;yield return UiPointerProbe.Click(runtime.Hud.WorldMap.TravelRect);
        }
        private void BeginComparison()
        {
            if(comparing) throw new InvalidOperationException("Повторная GPU проверка текущего посещения.");
            if(current.name=="hub-initial")
            {
                map=runtime.Session.Travel.Points("hub").Single(point=>point.kind==TravelPointKind.Map);
                ladder=runtime.Geometry.Get("hub").ladders.Where(value=>value.bottom<1 && value.top>=map.Arrival.y && value.x<map.Arrival.x).OrderByDescending(value=>value.x).First();
            }
            comparing=true;StartCoroutine(Checked(CompareVisit(current),"Сравнение света"));
        }
        private IEnumerator Checked(IEnumerator iterator,string action)
        {
            try
            {
                while(!finished)
                {
                    object next;
                    try {if(!iterator.MoveNext()) yield break;next=iterator.Current;}
                    catch(Exception error) {Finish(false,action+": "+error.Message);yield break;}
                    yield return next;
                }
            }
            finally {(iterator as IDisposable)?.Dispose();}
        }
        private IEnumerator CompareVisit(Visit visit)
        {
            var source=visit.source;visit.room=source.Room.id;visit.revision=source.AppliedOcclusionRevision;visit.readyFrame=Time.frameCount;visit.readyAt=Time.unscaledTimeAsDouble;
            visit.initialPrepareMilliseconds=source.InitialPrepareMilliseconds;visit.getPixelsMilliseconds=source.InitialGetPixelsMilliseconds;
            visit.decodeMilliseconds=source.InitialDecodeMilliseconds;visit.evaluateMilliseconds=source.InitialEvaluateMilliseconds;
            visit.cpuMilliseconds=source.InitialCpuMilliseconds;visit.uploadMilliseconds=source.InitialUploadMilliseconds;visit.totalBuildMilliseconds=source.InitialBuildMilliseconds;
            visit.frames=visit.trace.ToArray();if(visit.pendingFrames==0) visit.minimumDt=0;
            if(visit.initialPrepareMilliseconds>=syncBudget || visit.initialPrepareMilliseconds<0)
            {Finish(false,$"Синхронная подготовка {visit.name} превысила бюджет: actual={visit.initialPrepareMilliseconds:F2}, budget={syncBudget:F2} мс.");yield break;}
            if(visit.name=="factory" && (visit.pendingFrames<2 || !visit.loadingCaptured || !visit.gatesVerified || !visit.inventoryGateRejected))
            {Finish(false,"Нет нескольких настоящих кадров подготовки завода/loading PNG и input gate.");yield break;}
            bool Current()=>!finished && world==runtime.Session.World?.worldId && Actor?.id==visit.actor && Actor.level==visit.level && Actor.generation==visit.generation &&
                runtime.ActiveRoomLighting==source && ReferenceEquals(source.Room,runtime.Geometry.Get(visit.level)) && source.AppliedOcclusionRevision==runtime.Geometry.OcclusionRevision(visit.level);
            var iterator=RoomLightingGpuEvidence.Compare(source,Current,result=>visit.gpu=result,deadline);
            try
            {
                while(!finished)
                {
                    object next;
                    try {if(!iterator.MoveNext()) break;next=iterator.Current;}
                    catch(Exception error) {Finish(false,"GPU посещения "+visit.name+": "+error.Message);yield break;}
                    yield return next;
                }
            }
            finally {(iterator as IDisposable)?.Dispose();}
            if(finished) yield break;
            if(visit.gpu==null) {Finish(false,"Нет результата GPU посещения "+visit.name);yield break;}
            RoomLightingGpuEvidence.WriteMap(runtime.Options.Report+"."+visit.name+".foreground.png",visit.gpu.foreground.width,visit.gpu.foreground.height,visit.gpu.foregroundPixels);
            RoomLightingGpuEvidence.WriteMap(runtime.Options.Report+"."+visit.name+".background.png",visit.gpu.background.width,visit.gpu.background.height,visit.gpu.backgroundPixels);
            settledCapture=true;comparing=false;
        }
        private void LateUpdate()
        {
            if(finished || current==null) return;
            try
            {
                if(loadingCapture && !current.source.IsReady && current.name=="factory")
                {
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    var rect=UiPointerProbe.ScreenBounds(runtime.Hud.RoomLoadingRect);
                    var loading=runtime.Hud.RoomLoadingRect.GetComponent<RoomLoadingView>();
                    loading.Validate();
                    if(rect.xMin>1 || rect.yMin>1 || rect.xMax<Screen.width-1 || rect.yMax<Screen.height-1 || runtime.WorldCamera.cullingMask!=0 || !runtime.Hud.RoomLoadingVisible)
                        throw new InvalidOperationException("Непрозрачный actual loading HUD не покрывает кадр/камера не скрыта: "+rect);
                    current.loadingRect=new RoomLightingGpuBounds {x=rect.x,y=rect.y,width=rect.width,height=rect.height};
                    CaptureLoading(runtime.Options.Report+".factory-loading.png");
                    current.loadingCaptured=true;loadingCapture=false;current.captureMilliseconds+=timer.Elapsed.TotalMilliseconds;
                    File.WriteAllText(runtime.Options.Report+".factory-loading.json",StateJson.ToJson(current,true));
                }
                if(settledCapture)
                {
                    bool Current()=>runtime.ActiveRoomLighting==current.source && Actor?.level==current.level && Actor.generation==current.generation &&
                        runtime.Session.World.worldId==current.world && ReferenceEquals(current.source.Room,runtime.Geometry.Get(current.level)) &&
                        current.source.AppliedOcclusionRevision==runtime.Geometry.OcclusionRevision(current.level);
                    RoomLightingGpuEvidence.EnsureCurrent(current.gpu,Current);
                    NetworkFrameCapture.Save(runtime.WorldCamera,runtime.Options.Report+"."+current.name+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                    NetworkSceneRecord.Save(runtime,80+visits.Count);settledCapture=false;
                    if(phase==1) phase=2;
                    else if(phase==5) {exit=runtime.Session.Travel.Points("factory:0").Single(point=>point.kind==TravelPointKind.Exit);phase=6;SendObserved(1);}
                    else if(phase==8) {phase=10;returnReported=true;SendObserved(2);}
                    else throw new InvalidOperationException("Неожиданная фаза settled кадра света: "+phase);
                }
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void CaptureLoading(string path)
        {
            // Separate offscreen diagnostic camera; the live camera remains enabled with mask=0.
            var holder=new GameObject("Диагностическая камера загрузки");var camera=holder.AddComponent<Camera>();
            try {camera.CopyFrom(runtime.WorldCamera);camera.enabled=false;camera.cullingMask=~0;
                NetworkFrameCapture.Save(camera,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);}
            finally {Destroy(holder);}
        }
        private void SendObserved(int observedPhase)=>NetworkClient.Send(new LightingPreparationObserved {actor=Actor.id,world=world,level=Actor.level,
            generation=Actor.generation,phase=observedPhase,foreground=current.gpu.foreground.sha256,background=current.gpu.background.sha256});
        private void ObservePlayTime(CharacterPlayTime.CapturedCheckpoint checkpoint)
        {
            if(finished) return;
            if(playTimeWrites.Count>=8192) throw new InvalidOperationException("Переполнен журнал фоновой записи игрового времени.");
            playTimeWrites.Add(new PlayTimeWrite {profileDirectory=checkpoint.ProfileDirectory,elapsedSeconds=checkpoint.ElapsedSeconds,
                loadMilliseconds=checkpoint.LoadMilliseconds,writeMilliseconds=checkpoint.WriteMilliseconds,
                persistMilliseconds=checkpoint.PersistMilliseconds,persisted=checkpoint.IsPersisted});
        }
        private void OnDestroy() {if(runtime!=null) runtime.Session.PlayTimePersisted-=ObservePlayTime;}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            success=success && returnReported && visits.Count==3 && ordinaryMapEntry && ordinaryDoorExit;
            foreach(var visit in visits) visit.frames??=visit.trace.ToArray();
            TransitionTimingSample[] timings;
            try
            {
                timings=TransitionTiming.ToArray();
                if(success && timings.Length==0) throw new InvalidOperationException("Журнал actual переходов пуст или не включён.");
            }
            catch(Exception error)
            {
                success=false;timings=Array.Empty<TransitionTimingSample>();
                detail+="; ошибка журнала перехода: "+error.Message;
                Debug.LogError("Проба подготовки света: исходный результат сохранён; "+detail,this);
            }
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,noFixture=true,ordinaryMapEntry=ordinaryMapEntry,ordinaryDoorExit=ordinaryDoorExit,
                detail=detail+": "+Progress,world=world,actor=Actor?.id,level=Actor?.level,position=Actor?.Position??default,phase=phase,synchronousBudgetMilliseconds=syncBudget,
                visits=visits.ToArray(),transitionTimings=timings,playTimeWrites=playTimeWrites.ToArray()},true));
            exitCode=success?0:1;Invoke(nameof(Exit),.5f);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
    }
}
