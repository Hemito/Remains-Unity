using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using StateJson=Remains.Serialization.StateJson;

namespace Remains.Runtime
{
    public struct OpeningProbeFixture : NetworkMessage {public string json;}
    public struct OpeningProbeObserved : NetworkMessage {public string actor,door;public int generation,phase;public bool valid;public string detail;}
    public struct OpeningProbeBegin : NetworkMessage {public int phase;}
    public struct OpeningProbeFinish : NetworkMessage {public bool success;public string detail,snapshot;}

    // Only the initial player placement is a fixture. Every opening, shot and
    // subsequent movement uses production commands in the authored factory.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryOfficeOpeningProbe : MonoBehaviour
    {
        private const string Level="factory:0",Prefix=Level+":original:plant:53:";
        private const float CameraStartupSeconds=3;
        [Serializable] private sealed class Baseline {public string id,owner;public int generation,rounds;public Vector2 position;}
        [Serializable] private sealed class Fixture {public string kind,door,driver;public Baseline[] players;public string initialDoor;}
        [Serializable] private sealed class ResumeExpected
        {public int schema;public string world,snapshot,door,foreground,background,profile;}
        [Serializable] private sealed class WindowFrame
        {
            public string phase,door,definition,sprite;
            public bool fullyVisible,rendererEnabled,orthographic;
            public int width,height,cullingMask,artworkLayer;
            public float orthographicSize,aspect,nearClip,farClip,minX,maxX,minY,maxY,minDepth,maxDepth;
            public Vector3 cameraPosition,cameraForward,artworkCenter,artworkSize;
            public Vector2 physicalMin,physicalMax;
            public Vector3[] artworkViewport,physicalViewport,cameraWorldCorners;
        }
        [Serializable] private sealed class Report
        {
            public bool success,explicitInitialPlayerFixture,closedObserved,openedObserved,destroyedObserved,geometryVerified,
                traversalVerified,savedReloadVerified,reinstallVerified,duplicateDamageVerified,externalReloadVerified,independentExpectedVerified,sourceArchiveVerified,windowVisibleVerified;
            public string detail,kind,door,snapshot,world,mode,sourceSnapshot,expectedDoorJson,actualDoorJson;public int shots,acceptedInteractions,captures,rounds,damageReplies;
            public float health;public Vector2 position;public Baseline baseline;public DoorState result;
            public bool lightingVerified,lightingChangedVerified,lightingCloseRestored,bankApproachVerified;
            public LightingObservation[] lighting;
        }
        [Serializable] private sealed class LightingMapEvidence
        {public string name,sha256,expectedSha256;public int width,height,bytes,maxError,changedPixels,closedMaxError,openMaxError;}
        [Serializable] private sealed class LightBounds {public float x,y,width,height;}
        [Serializable] private sealed class LightingObservation
        {
            public string phase,world,room,profile,graphics;
            public int frame,generation,scenePhase,candidatePixels,dirtyPixels,uniqueDecodedTextures;
            public long doorRevision,occlusionRevision,appliedRevision,decodedPixels;
            public double requestedAt,verifiedAt,initialBuildMilliseconds,updateMilliseconds,cpuMilliseconds,uploadMilliseconds,latencyMilliseconds,referenceMilliseconds,readbackMilliseconds;
            public LightBounds bounds;public Vector2 playerPosition;
            public LightingMapEvidence foreground,background;
            public bool shaderBindingsVerified,sourceGuardVerified;
            [NonSerialized] public RoomLighting source;
            [NonSerialized] public RoomLayout sourceRoom;
            [NonSerialized] public Texture sourceForeground,sourceBackground;
            [NonSerialized] public RoomLightProfile sourceProfile;
            [NonSerialized] public Rect sourceBounds;
        }
        private sealed class LightReadback {public bool done;public string error;public byte[] pixels;}
        private PrototypeRuntime runtime;private Fixture fixture;private Baseline baseline;
        private bool traveled,installed,finished,completed,begun,openViewsReady,requestedOpen,requestedClose,fired,reported,aimReady;
        private bool closedObserved,openedObserved,destroyedObserved,geometryVerified,traversalVerified,savedReloadVerified,reinstallVerified;
        private float deadline,openedAt=-1;private int shots,acceptedInteractions,captures;
        private string kind,doorId,capture,lastCapture,snapshot;
        private DoorDamageRequestData damageRequest;private bool replaySent,duplicateDamageVerified;private int damageReplies;
        private bool diskProofStarted,fixtureActivated;
        private bool bankApproachVerified,bankReported,banksReady,closedAgainObserved,closedViewsReady,reopenedObserved,reopenedViewsReady,requestedReopen;
        private readonly HashSet<string> bankWitnesses=new(StringComparer.Ordinal),closedAgainWitnesses=new(StringComparer.Ordinal),reopenedWitnesses=new(StringComparer.Ordinal);
        private readonly List<LightingObservation> lighting=new();
        private string lightingPending;
        private byte[] closedForeground,closedBackground,openForeground,openBackground;
        private bool lightingChangedVerified,lightingCloseRestored;
        private ResumeExpected resumeExpected;
        private string expectedDoorJson,sourceArchive;
        private ActorState expectedResumePlayer;
        private bool externalReloadVerified,independentExpectedVerified,sourceArchiveVerified,windowVisibleVerified;
        private float resumeReadyAt=-1,freshReadyAt=-1;
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-",StringComparison.Ordinal);
        private readonly HashSet<string> witnesses=new(StringComparer.Ordinal);
        private readonly HashSet<string> closedWitnesses=new(StringComparer.Ordinal),ownAttacks=new(StringComparer.Ordinal);
        private readonly HashSet<string> openWitnesses=new(StringComparer.Ordinal);
        private bool Host=>runtime.Options.Role=="host";
        private bool Driver=>runtime.Options.ProbePeers==1 || !Host;
        private ActorState Actor=>runtime.Session.LocalActor;
        private DoorState Door=>runtime.Session.World?.doors.SingleOrDefault(d=>d.id==doorId);
        private string Progress=>$"kind={kind}, door={doorId}, position={Actor?.Position}, HP={Actor?.health}, generation={Actor?.generation}/{baseline?.generation}, open={Door?.open}, destroyed={Door?.Destroyed}, shots={shots}, E={acceptedInteractions}, lastCapture={lastCapture}, pendingCapture={capture}, fixtureActive={fixtureActivated}, closed={closedObserved}, closedWitnesses={closedWitnesses.Count}, begun={begun}, witnesses={witnesses.Count}, bank={bankApproachVerified}/{banksReady}, closedAgain={closedAgainObserved}/{closedViewsReady}, reopened={reopenedObserved}/{reopenedViewsReady}, lighting={lighting.Count}/{lightingPending}";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+(Resume?45:95);
            var parts=(value.Options.Smoke??"").Split('-');
            var kinds=parts.Where(p=>p is "hatch" or "window" or "entry").ToArray();
            if(kinds.Length!=1 || value.Options.ProbePeers is <1 or >2 || string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report))
                throw new InvalidOperationException("Проверка проёма требует явный вид, новый каталог и 1–2 участников.");
            kind=kinds[0];doorId=Prefix+(kind=="hatch"?"cLSBzRkMe7e7pefR":kind=="window"?"cK9ZcCyQv8c4oTfH":"oYIjQIPJl0C4IamY");
            if(Resume)
            {
                if(kind is not ("window" or "hatch") || !Host || value.Options.ProbePeers!=1)
                    throw new InvalidOperationException("Resume проёма поддерживает window/hatch host solo без fixture.");
                PrepareResume();value.CommandSource=()=>default;return;
            }
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка проёма не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Session.AttackReceived+=attack=>{if(attack.attacker==value.Session.LocalActorId && ownAttacks.Add(attack.id)) shots++;};
            value.Session.Doors.Finished+=reply=>{if(reply.accepted) acceptedInteractions++;else Finish(false,reply.reason,null);};
            value.Session.DoorDamage.Reported+=request=>{if(request.door==doorId) damageRequest=StateJson.Copy(request);};
            value.Session.DoorDamage.Finished+=reply=>
            {
                if(reply.door!=doorId) return;
                damageReplies++;
                if(!reply.accepted || !reply.destroyed || reply.health!=0 || reply.revision!=1 || damageReplies==1 && !reply.changed || damageReplies==2 && reply.changed)
                    Finish(false,"Ответ урона/повтора не совпал с единственным разрушением: "+reply.reason,null);
                if(damageReplies==2) duplicateDamageVerified=true;
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<OpeningProbeFixture>(message=>
                {
                    fixture=JsonUtility.FromJson<Fixture>(message.json);
                    baseline=fixture.players.Single(p=>p.id==value.Session.LocalActorId);capture="closed";
                });
                NetworkClient.RegisterHandler<OpeningProbeFinish>(message=>Finish(message.success,message.detail,message.snapshot));
                NetworkClient.RegisterHandler<OpeningProbeBegin>(message=>
                {
                    if(message.phase==1) begun=true;else if(message.phase==2) openViewsReady=true;
                    else if(message.phase==3) banksReady=true;else if(message.phase==4) closedViewsReady=true;else if(message.phase==5) reopenedViewsReady=true;
                });
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<OpeningProbeObserved>((connection,message)=>
            {
                try
                {
                    var expected=fixture?.players.SingleOrDefault(p=>p.id==message.actor);
                    var actual=value.Session.ServerWorld.actors.SingleOrDefault(a=>a.id==message.actor);
                    if(expected==null || actual==null || actual.owner!="connection:"+connection.connectionId || actual.generation!=message.generation ||
                        expected.generation!=message.generation || message.door!=doorId || !message.valid)
                        throw new InvalidOperationException("Неверное свидетельство проёма: "+message.detail);
                    if(message.phase==0)
                    {
                        closedWitnesses.Add(message.actor);
                        if(closedWitnesses.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new OpeningProbeBegin {phase=1});
                    }
                    else if(message.phase==1) witnesses.Add(message.actor);
                    else if(message.phase==2)
                    {
                        openWitnesses.Add(message.actor);
                        if(openWitnesses.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new OpeningProbeBegin {phase=2});
                    }
                    else if(kind=="hatch" && message.phase==3)
                    {
                        bankWitnesses.Add(message.actor);
                        if(bankWitnesses.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new OpeningProbeBegin {phase=3});
                    }
                    else if(kind=="hatch" && message.phase==4)
                    {
                        closedAgainWitnesses.Add(message.actor);
                        if(closedAgainWitnesses.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new OpeningProbeBegin {phase=4});
                    }
                    else if(kind=="hatch" && message.phase==5)
                    {
                        reopenedWitnesses.Add(message.actor);
                        if(reopenedWitnesses.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new OpeningProbeBegin {phase=5});
                    }
                    else throw new InvalidOperationException("Неизвестная фаза свидетельства проёма: "+message.phase);
                }
                catch(Exception error) {Finish(false,error.Message,null);}
            });
        }
        private PlayerCommand Command()
        {
            try
            {
                if(finished || !begun || !closedObserved || fixture==null || Actor==null || Actor.generation!=baseline.generation) return default;
                if(kind=="hatch") return HatchCommand();
                if(!Driver) return default;
                var command=new PlayerCommand {hasAim=true,aim=Door.InteractionBounds.center};
                if(kind=="window")
                {
                    var pistol=Actor.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
                    if(Actor.equippedWeaponItem!=pistol.id) {command.weaponItem=pistol.id;return command;}
                    if(!aimReady) {aimReady=true;return command;}
                    if(!fired) {fired=true;command.fire=true;}
                    return command;
                }
                if(!requestedOpen) {RequireSelectedDoor(command.aim);requestedOpen=true;command.interact=true;return command;}
                if(kind=="entry" && openedObserved && openViewsReady && !requestedClose)
                {
                    float destination=Door.Bounds.xMax+Anatomy.Movement(Actor).width*.5f+.15f;
                    if(Actor.x<destination) command.movement=Vector2.right;
                    else {traversalVerified=true;RequireSelectedDoor(command.aim);requestedClose=true;command.interact=true;}
                }
                return command;
            }
            catch(Exception error) {Finish(false,error.Message+": "+Progress,null);return default;}
        }
        private PlayerCommand HatchCommand()
        {
            var command=new PlayerCommand {hasAim=true,aim=Door.InteractionBounds.center};
            if(!bankApproachVerified)
            {
                float destination=Door.Bounds.xMax+.6f+(Driver?0:1.2f);
                if(Actor.x<destination) {command.movement=Vector2.right;return command;}
                var room=runtime.Geometry.Get(Level);var hull=Anatomy.Movement(Actor);
                // Ordinary actor motion is blocked by the actual room geometry, not by
                // the initial-fixture exclusion of prop/NPC bodies. Do not invent a new blocker here.
                if(!HasHatchBankSupport(room,hull,Actor.Position,Door.Bounds))
                    throw new InvalidOperationException("Обычный подход не вывел игрока на бетонную опору справа люка: "+HatchBankDetail(room,hull)+": "+Progress);
                bankApproachVerified=true;
            }
            if(!banksReady) return command;
            if(Driver && !requestedOpen) {RequireSelectedDoor(command.aim);requestedOpen=true;command.interact=true;return command;}
            if(Driver && openedObserved && openViewsReady && !requestedClose)
            {RequireSelectedDoor(command.aim);requestedClose=true;command.interact=true;return command;}
            if(Driver && closedAgainObserved && closedViewsReady && !requestedReopen)
            {RequireSelectedDoor(command.aim);requestedReopen=true;command.interact=true;return command;}
            if(reopenedViewsReady && !traversalVerified && Actor.x>Door.Bounds.center.x+(Driver?-.2f:.2f)) command.movement=Vector2.left;
            return command;
        }
        private static bool HasHatchBankSupport(RoomLayout room,MovementHull hull,Vector2 position,Rect hatch)=>
            Mathf.Abs(position.y-hatch.yMax)<=RoomSupport.ContactTolerance && position.x-hull.width*.5f>=hatch.xMax+.05f &&
            ActorTerrainMotion.IsClear(room,hull,position) && ActorTerrainMotion.Supported(room,hull,position);
        private string HatchBankDetail(RoomLayout room,MovementHull hull)
        {
            var position=Actor.Position;var hatch=Door.Bounds;var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
            string Describe(RoomSurface surface)=>$"{surface.texture}/{surface.sourceDoorId ?? "terrain"}@({surface.x:R},{surface.y:R},{surface.width:R},{surface.height:R})";
            var solids=RoomSurfaces.All(room).Where(surface=>!surface.oneWay && InteriorOverlap(body,new Rect(surface.x,surface.y,surface.width,surface.height))).Select(Describe);
            var supports=RoomSurfaces.All(room).Where(surface=>body.xMax>surface.x+.0001f && body.xMin<surface.x+surface.width-.0001f &&
                Mathf.Abs(position.y-RoomSupport.Top(surface,position.x,hull.width))<=RoomSupport.ContactTolerance).Select(Describe);
            var props=WorldProps.All(runtime.Session.World).Where(prop=>prop.hasPhysicalBody && prop.level==Level &&
                InteriorOverlap(body,new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height))).Select(prop=>prop.id+"/"+prop.definition);
            var actors=runtime.Session.World.actors.Where(actor=>!actor.player && actor.level==Level && InteriorOverlap(body,InventorySource.BodyBounds(actor))).Select(actor=>actor.id+"/"+actor.actorType);
            return $"position=({position.x:R},{position.y:R}), hull=({body.x:R},{body.y:R},{body.width:R},{body.height:R}), hatchRight={hatch.xMax:R}, hatchTop={hatch.yMax:R}, "+
                $"sameHeight={Mathf.Abs(position.y-hatch.yMax)<=RoomSupport.ContactTolerance}, clearRight={body.xMin>=hatch.xMax+.05f}, terrainClear={ActorTerrainMotion.IsClear(room,hull,position)}, "+
                $"terrainSupported={ActorTerrainMotion.Supported(room,hull,position)}, solids=[{string.Join(";",solids)}], supports=[{string.Join(";",supports)}], "+
                $"propOverlaps(nonblocking)=[{string.Join(";",props)}], npcOverlaps(nonblocking)=[{string.Join(";",actors)}]";
        }
        private void RequireSelectedDoor(Vector2 cursor)
        {
            var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,cursor);
            if(selected.kind!=InteractionKind.Door || selected.id!=doorId) throw new InvalidOperationException($"E выбрал {selected.kind}/{selected.id}, ожидался {doorId}.");
        }
        private void SendObservation(int phase)=>NetworkClient.Send(new OpeningProbeObserved
        {actor=Actor.id,door=doorId,generation=Actor.generation,phase=phase,valid=true,detail=Progress});
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут проёма: "+Progress);
                var session=runtime.Session;var actor=Actor;
                if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(Resume) {TickResume();return;}
                if(!traveled) {traveled=true;session.Travel.RequestFactory();}
                if(Host && !installed && !session.Travel.Pending && session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level) &&
                    session.World.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level)) Setup();
                if(fixture==null || baseline==null) return;
                if(!fixtureActivated && actor.generation!=baseline.generation) return;
                if(!fixtureActivated) {fixtureActivated=true;freshReadyAt=Time.unscaledTime;}
                if(actor.generation!=baseline.generation) throw new InvalidOperationException("Поколение игрока изменилось после начальной fixture: "+Progress);
                if(actor.down || actor.health<=0 || actor.owner!=baseline.owner || actor.level!=Level) throw new InvalidOperationException("Игрок потерял состояние во время проверки проёма");
                var door=Door;door.Validate();
                var room=runtime.Geometry.Get(Level);
                bool blocks=room.platforms.Any(s=>s.sourceDoorId==doorId);
                if(blocks!=door.Blocks) throw new InvalidOperationException("Геометрия не совпала с состоянием проёма: "+Progress);
                geometryVerified=true;
                if(kind=="hatch" && bankApproachVerified && !bankReported)
                {bankReported=true;SendObservation(3);}
                if(door.open && !openedObserved) {openedObserved=true;openedAt=Time.unscaledTime;capture="open";}
                if(kind=="hatch" && openedObserved && !door.open && door.revision>=2 && !closedAgainObserved)
                {closedAgainObserved=true;capture="closed-again";}
                if(kind=="hatch" && closedAgainObserved && door.open && door.revision>=3 && !reopenedObserved)
                {reopenedObserved=true;capture="reopened";}
                if(door.Destroyed && !destroyedObserved) {destroyedObserved=true;capture="destroyed";}
                bool ready=false;
                if(kind=="hatch" && reopenedViewsReady && actor.y<door.Bounds.yMin-.1f && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position))
                {traversalVerified=true;if(lastCapture=="reopened") capture="below";ready=lastCapture=="below" && lighting.Count==4 && lightingChangedVerified && lightingCloseRestored;}
                else if(kind=="entry" && openedObserved && !door.open && door.revision>=2)
                {
                    if(!Driver) traversalVerified=session.World.actors.Any(a=>a.player && a.connected && a.x>door.Bounds.xMax+Anatomy.Movement(a).width*.5f);
                    if(lastCapture=="open") capture="closed-again";ready=traversalVerified && lastCapture=="closed-again";
                }
                else if(kind=="window" && destroyedObserved && lastCapture=="destroyed")
                {
                    if(door.integrity.resolvedAttacks.Count!=1 || door.integrity.health!=0 || door.open || door.CanInteract || !fired && Driver)
                        throw new InvalidOperationException("Некорректное разрушение окна: "+Progress);
                    string receipt=door.integrity.resolvedAttacks[0];
                    var flight=session.World.projectiles.SingleOrDefault(p=>p.launch?.id==receipt);
                    if(flight?.launch.attacker!=fixture.driver || flight.launch.weapon!="pistol" ||
                        Driver && (damageRequest?.attack.id!=receipt || !ownAttacks.Contains(receipt) || shots!=1 || baseline.rounds-Actor.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null).weaponState.rounds!=1))
                        throw new InvalidOperationException("Разрушение окна не связано с одним выстрелом проверочного игрока: "+Progress);
                    if(Driver && !replaySent && damageReplies==1)
                    {
                        if(damageRequest==null) throw new InvalidOperationException("Нет исходного сетевого контакта окна для повтора");
                        replaySent=true;NetworkClient.Send(new DoorDamageRequest {json=StateJson.ToJson(damageRequest)});
                    }
                    ready=!Driver || duplicateDamageVerified;
                }
                if(ready && !diskProofStarted) {diskProofStarted=true;StartCoroutine(CheckDiskCopy());}
                if(ready && savedReloadVerified && !reported)
                {reported=true;NetworkClient.Send(new OpeningProbeObserved {actor=actor.id,door=doorId,generation=actor.generation,phase=1,valid=true,detail=Progress});}
                if(Host && !completed && witnesses.Count==runtime.Options.ProbePeers && (openedAt<0 || Time.unscaledTime-openedAt>.5f))
                    Complete();
            }
            catch(Exception error) {Finish(false,error.Message+": "+Progress,null);}
        }
        private void PrepareResume()
        {
            // Independent evidence files are copied/written by the wrapper BEFORE process launch.
            // This reads neither expected state from the loaded world nor a just-written save.
            string report=runtime.Options.Report;
            resumeExpected=StateJson.FromJson<ResumeExpected>(File.ReadAllText(report+".opening-resume-expected.json"));
            var expectedSnapshot=new RecoverySnapshot(File.ReadAllText(report+".opening-resume-world.json"));
            if(resumeExpected.schema!=1 || resumeExpected.world!=expectedSnapshot.WorldId ||
                resumeExpected.snapshot!=expectedSnapshot.Id || resumeExpected.door!=doorId)
                throw new InvalidOperationException("Независимое pre-load свидетельство окна не совпало с исходным миром.");
            var source=expectedSnapshot.CreateLiveWorld();var expected=source.doors.Single(d=>d.id==doorId);expected.Validate();
            if(kind=="window")
            {
                if(!expected.Destroyed || expected.open || expected.CanInteract || expected.integrity.health!=0 || expected.revision!=1 || expected.integrity.resolvedAttacks.Count!=1)
                    throw new InvalidOperationException("Pre-load свидетельство не содержит единственное сохранённое разрушение окна.");
            }
            else if(!expected.open || expected.Destroyed || !expected.CanInteract || expected.integrity.health!=expected.integrity.maxHealth ||
                expected.revision!=3 || expected.integrity.resolvedAttacks.Count!=0 ||
                string.IsNullOrEmpty(resumeExpected.foreground) || string.IsNullOrEmpty(resumeExpected.background) || string.IsNullOrEmpty(resumeExpected.profile))
                throw new InvalidOperationException("Pre-load свидетельство не содержит открытый люк после трёх E и две подтверждённые GPU-карты.");
            expectedDoorJson=StateJson.ToJson(expected);
            expectedResumePlayer=source.actors.Single(a=>a.player && a.character==runtime.Session.CharacterId);
            if(expectedResumePlayer.level!=Level) throw new InvalidOperationException("Исходный игрок сохранён вне завода; resume не делает переход/телепорт.");
            var selected=runtime.Session.Saves.SelectedSnapshot();
            if(selected==null || selected.Id!=resumeExpected.snapshot || selected.WorldId!=resumeExpected.world)
                throw new InvalidOperationException("Скопированное selected сохранение отличается от независимого pre-load expected.");
            sourceArchive=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",selected.WorldId,"Snapshots",selected.Id+".json");
            VerifySourceArchive();independentExpectedVerified=true;
        }
        private void VerifySourceArchive()
        {
            if(!File.Exists(sourceArchive) || RecoverySnapshot.Fingerprint(File.ReadAllText(sourceArchive))!=resumeExpected.snapshot)
                throw new InvalidOperationException("Исходный selected snapshot удалён или изменён в архиве resume копии.");
            sourceArchiveVerified=true;
        }
        private void TickResume()
        {
            var session=runtime.Session;var actor=Actor;
            if(session.ServerWorld==null) return;
            if(session.World.worldId!=resumeExpected.world || session.ServerWorld.worldId!=resumeExpected.world || actor.id!=expectedResumePlayer.id ||
                actor.level!=Level || !actor.connected || actor.down || actor.health<=0)
                throw new InvalidOperationException("Resume не вернул того же активного игрока в сохранённый завод без fixture.");
            if(resumeReadyAt>=0 && lastCapture!="reloaded") return;
            var canonical=session.ServerWorld.doors.Single(d=>d.id==doorId);
            if(StateJson.ToJson(Door)!=expectedDoorJson || StateJson.ToJson(canonical)!=expectedDoorJson)
                throw new InvalidOperationException("После production загрузки/установки изменились сохранённые HP/revision/receipt/данные проёма.");
            var room=runtime.Geometry.Get(Level);var bounds=Door.Bounds;
            if(RoomSurfaces.All(room).Any(surface=>surface.sourceDoorId==doorId) ||
                ShotObstacles.FirstContact(room,new Vector2(bounds.xMin+.001f,bounds.center.y),new Vector2(bounds.xMax-.001f,bounds.center.y)).HasValue)
                throw new InvalidOperationException("После resume физический проём снова перекрывает свой aperture.");
            geometryVerified=true;destroyedObserved=kind=="window";openedObserved=kind=="hatch";
            if(resumeReadyAt<0)
            {
                var saved=session.Saves.LoadWorld(false);
                if(saved?.worldId!=resumeExpected.world || StateJson.ToJson(saved.doors.Single(d=>d.id==doorId))!=expectedDoorJson)
                    throw new InvalidOperationException("Selected save после production Join потерял exact сохранённый проём.");
                VerifySourceArchive();savedReloadVerified=true;externalReloadVerified=true;resumeReadyAt=Time.unscaledTime;capture="reloaded";
            }
            if(lastCapture=="reloaded")
            {
                VerifySourceArchive();
                Finish(true,kind=="window"?
                    "Новый внешний процесс загрузил тот же selected мир без fixture/нового выстрела: exact окно HP0/revision1/receipt1, свободная геометрия, настоящий destroyed sprite полностью в кадре и исходный snapshot сохранён.":
                    "Новый внешний процесс загрузил открытый люк после трёх E без fixture/новых действий: exact сохранённый проём, свободная геометрия, обе GPU-карты совпали с независимым pre-load свидетельством и свежим расчётом; исходный snapshot сохранён.",resumeExpected.snapshot);
            }
        }
        private void Setup()
        {
            var session=runtime.Session;var world=session.ServerWorld;
            var declared=RoomDoorContent.Read(RoomAuthoring.Load(Level,world),Level);
            if(declared==null || !world.doors.Where(d=>d.level==Level).Select(d=>d.id).OrderBy(id=>id,StringComparer.Ordinal)
                .SequenceEqual(declared.doors.Select(d=>d.id).OrderBy(id=>id,StringComparer.Ordinal)))
                throw new InvalidOperationException($"Расстановка проёмов не совпала с текущим авторским заводом: actual={world.doors.Count(d=>d.level==Level)}, declared={declared?.doors?.Length}.");
            var door=world.doors.Single(d=>d.id==doorId);door.Validate();
            if(door.open || door.Destroyed || door.integrity==null || door.integrity.health!=door.integrity.maxHealth || door.integrity.resolvedAttacks.Count!=0)
                throw new InvalidOperationException("Исходный проём не соответствует новой расстановке: "+doorId);
            var room=runtime.Geometry.Get(Level);var connected=world.actors.Where(a=>a.player && a.connected).ToArray();
            string driver=connected.Single(p=>runtime.Options.ProbePeers==1 || p.id!=session.LocalActorId).id;
            var players=connected.OrderBy(a=>a.id==driver?0:1).ToArray();
            var records=new List<Baseline>();
            for(int index=0;index<players.Length;index++)
            {
                var player=players[index];Vector2 position=kind=="hatch"?new Vector2(door.Bounds.center.x+(index==0?-.2f:.2f),door.Bounds.yMax):FindPlacement(world,room,door,player,index==0,records);
                RequirePlacement(world,room,player,position);
                var pistol=player.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
                player.Position=position;player.Velocity=Vector2.zero;player.climbing=false;player.crouched=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
                records.Add(new Baseline {id=player.id,owner=player.owner,generation=player.generation,position=position,rounds=pistol.weaponState.rounds});
            }
            if(kind=="window") RequireShotClear(world,room,door,players[0]);
            fixture=new Fixture {kind=kind,door=doorId,driver=driver,
                players=records.ToArray(),initialDoor=StateJson.ToJson(door)};
            installed=true;session.CommitWorld();NetworkServer.SendToAll(new OpeningProbeFixture {json=JsonUtility.ToJson(fixture)});
            Debug.Log($"Изолированная проверка проёма {doorId}/{kind}: изменено только начальное положение игроков; далее обычные E, движение, гравитация и выстрел.");
        }
        private Vector2 FindPlacement(WorldState world,RoomLayout room,DoorState door,ActorState player,bool driver,IReadOnlyList<Baseline> placed)
        {
            var candidates=new List<Vector2>();var hull=Anatomy.Movement(player);
            foreach(var surface in RoomSurfaces.All(room))
                for(float distance=hull.width*.5f+.3f;distance<5;distance+=.2f)
                {
                    float x=door.Bounds.xMin-distance;
                    if(x+hull.width*.5f<=surface.x || x-hull.width*.5f>=surface.x+surface.width) continue;
                    candidates.Add(new Vector2(x,RoomSupport.Top(surface,x,hull.width)));
                }
            foreach(var position in candidates.Distinct().OrderBy(p=>Vector2.Distance(p,door.Bounds.center)))
            {
                if(!PlacementClear(world,room,player,position)) continue;
                if(placed.Any(p=>position.x>p.position.x-1.2f || Vector2.Distance(position,p.position)<1.2f)) continue;
                var sample=StateJson.Copy(player);sample.Position=position;sample.climbing=false;sample.crouched=false;sample.pose="idle";
                if(driver && kind=="window")
                {
                    var pistol=sample.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
                    if(!WeaponInventory.Select(sample,pistol.id,out string reason)) throw new InvalidOperationException(reason);
                    WeaponPose.Aim(sample,door.Bounds.center,room);
                    if(ShotObstacles.FirstContact(room,WeaponPose.RangedAnchor(sample),WeaponPose.Muzzle(sample)).HasValue) continue;
                    if(!ShotObstacles.FirstContact(room,WeaponPose.Muzzle(sample),door.Bounds.center,out var contact).HasValue || contact.sourceDoorId!=doorId) continue;
                }
                else if(driver)
                {
                    var target=InteractionSelection.Select(world,sample,runtime.Session.Rules,door.InteractionBounds.center);
                    if(target.kind!=InteractionKind.Door || target.id!=doorId || Mathf.Abs(position.y-door.y)>.01f) continue;
                }
                else if(Mathf.Abs(position.y-placed[0].position.y)>.01f) continue;
                return position;
            }
            throw new InvalidOperationException("Не найдена подтверждённая опора перед проёмом: "+doorId);
        }
        private static void RequireShotClear(WorldState world,RoomLayout room,DoorState door,ActorState player)
        {
            var sample=StateJson.Copy(player);var pistol=sample.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
            if(!WeaponInventory.Select(sample,pistol.id,out string reason)) throw new InvalidOperationException(reason);
            WeaponPose.Aim(sample,door.Bounds.center,room);
            var shot=new AttackEvent {id="opening-initial-preflight",attacker=sample.id,sourcePlayer=true,sourceOwner=sample.owner,level=Level,weapon="pistol"};
            var origin=WeaponPose.Muzzle(sample);var end=door.Bounds.center;
            shot.originX=origin.x;shot.originY=origin.y;shot.endX=end.x;shot.endY=end.y;
            if(AttackContacts.Capture(world.actors,shot,world.friendlyFire,true).Count!=0)
                throw new InvalidOperationException("Начальная линия выстрела в окно пересекает игрока/NPC");
        }
        private static bool PlacementClear(WorldState world,RoomLayout room,ActorState player,Vector2 position)
        {
            var hull=Anatomy.Movement(player);var bounds=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
            if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position)) return false;
            if(WorldProps.All(world).Any(p=>p.hasPhysicalBody && p.level==Level && InteriorOverlap(bounds,new Rect(p.x-p.width*.5f,p.y-p.anchorHeight,p.width,p.height)))) return false;
            return !world.actors.Any(a=>!a.player && a.level==Level && InteriorOverlap(bounds,InventorySource.BodyBounds(a)));
        }
        private static void RequirePlacement(WorldState world,RoomLayout room,ActorState player,Vector2 position)
        {if(!PlacementClear(world,room,player,position)) throw new InvalidOperationException($"Начальная опора проёма занята/не существует: actor={player.id}, position={position}.");}
        private static bool InteriorOverlap(Rect a,Rect b)=>a.xMax>b.xMin+.0001f && a.xMin<b.xMax-.0001f && a.yMax>b.yMin+.0001f && a.yMin<b.yMax-.0001f;
        private IEnumerator CheckDiskCopy()
        {
            // Explicit final-stage recovery check, never ordinary per-frame save I/O.
            for(int attempt=0;attempt<12 && !finished;attempt++)
            {
                bool matched=false;
                try
                {
                    var saved=runtime.Session.Saves.LoadWorld(false);
                    var persisted=saved?.doors.SingleOrDefault(d=>d.id==doorId);
                    matched=persisted!=null && StateJson.ToJson(persisted)==StateJson.ToJson(Door);
                }
                catch(Exception error) {Finish(false,"Проверка сохранённой копии проёма: "+error.Message,null);}
                if(finished) yield break;
                if(matched) {savedReloadVerified=true;yield break;}
                yield return new WaitForSecondsRealtime(.25f);
            }
            if(!finished) Finish(false,"В сохранённой копии участника не появился подтверждённый проём",null);
        }
        private void Complete()
        {
            var session=runtime.Session;var door=session.ServerWorld.doors.Single(d=>d.id==doorId);
            var saved=session.Saves.LoadWorld(false);var persisted=saved.doors.Single(d=>d.id==doorId);
            if(StateJson.ToJson(persisted)!=StateJson.ToJson(door)) throw new InvalidOperationException("Сохранение проёма отстаёт от подтверждённого действия");
            savedReloadVerified=true;
            // Use the canonical room definition, not the live composed collision cache.
            var layout=RoomDoorContent.Read(RoomAuthoring.Load(Level,session.ServerWorld),Level);
            string before=StateJson.ToJson(saved.doors);
            if(DoorContentInstaller.Apply(saved,layout) || StateJson.ToJson(saved.doors)!=before) throw new InvalidOperationException("Повтор установки воскресил/сбросил проём");
            reinstallVerified=true;snapshot=session.Saves.SelectedSnapshot().Id;completed=true;
            NetworkServer.SendToAll(new OpeningProbeFinish {success=true,detail="Обычные действия изменили авторский проём; реплики, геометрия и сохранённое состояние совпали.",snapshot=snapshot});
        }
        private void LateUpdate()
        {
            if(finished || capture==null || Actor==null || !Resume && (!fixtureActivated || fixture==null || baseline==null || Actor.generation!=baseline.generation)) return;
            try
            {
                var view=runtime.DoorVisuals.Find(doorId);
                if(view==null) throw new InvalidOperationException("Нет авторского изображения проёма: "+doorId);
                var expected=Door.Destroyed?view.Definition.Destroyed:Door.open?view.Definition.Opened:view.Definition.Closed;
                if(view.Artwork.sprite!=expected) throw new InvalidOperationException("Изображение проёма отстаёт от состояния: "+doorId);
                if(capture=="closed" && (Door.open || Door.Destroyed || StateJson.ToJson(Door)!=fixture.initialDoor) ||
                    capture is "open" or "reopened" && (!Door.open || Door.Destroyed) || capture=="closed-again" && (Door.open || Door.Destroyed) || capture=="destroyed" && !Door.Destroyed)
                    throw new InvalidOperationException("Кадр проёма не соответствует заявленной фазе: "+capture);
                if(Resume && (capture!="reloaded" || StateJson.ToJson(Door)!=expectedDoorJson))
                    throw new InvalidOperationException("Resume кадр потерял exact независимое состояние окна.");
                if(kind=="hatch" && capture!="below")
                {
                    var verified=lighting.FirstOrDefault(item=>item.phase==capture);
                    if(verified==null)
                    {
                        if(lightingPending==null) {lightingPending=capture;StartCoroutine(RunLighting(capture));}
                        return;
                    }
                    // GPU proof and the subsequent scene/frame must still belong to the same source.
                    RequireLightingSource(verified.source,verified.sourceRoom,verified.occlusionRevision,verified.world,verified.generation,
                        verified.doorRevision,verified.sourceForeground,verified.sourceBackground);
                    if(!ReferenceEquals(verified.source.Profile,verified.sourceProfile) || verified.source.Bounds!=verified.sourceBounds ||
                        RecoverySnapshot.Fingerprint(StateJson.ToJson(verified.source.Profile))!=verified.profile)
                        throw new InvalidOperationException("Перед кадром сменился проверенный профиль/прямоугольник света: "+capture);
                }
                if(kind=="window")
                {
                    var frame=WindowEvidence(view.Artwork,Camera.main);
                    if(!frame.fullyVisible)
                    {
                        // Give the existing camera its ordinary startup frames; never reposition it.
                        float startupAt=Resume?resumeReadyAt:freshReadyAt;
                        bool initialCapture=Resume?capture=="reloaded":capture=="closed" && !closedObserved && !begun;
                        if(initialCapture && startupAt>=0 && Time.unscaledTime-startupAt<CameraStartupSeconds) return;
                        File.WriteAllText(runtime.Options.Report+".opening-"+capture+".frame.json",StateJson.ToJson(frame,true));
                        throw new InvalidOperationException($"Окно вне полного кадра: viewport=({frame.minX},{frame.minY})..({frame.maxX},{frame.maxY}), depth={frame.minDepth}..{frame.maxDepth}, camera={frame.cameraPosition}, artwork={frame.artworkCenter}/{frame.artworkSize}.");
                    }
                    File.WriteAllText(runtime.Options.Report+".opening-"+capture+".frame.json",StateJson.ToJson(frame,true));
                    windowVisibleVerified=true;
                }
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".opening-"+capture+".png",runtime.Hud.GetComponent<Canvas>());
                if(kind=="hatch") NetworkSceneRecord.Save(runtime,LightingScenePhase(capture));
                lastCapture=capture;
                if(capture=="closed")
                {
                    closedObserved=true;
                    NetworkClient.Send(new OpeningProbeObserved {actor=Actor.id,door=doorId,generation=Actor.generation,phase=0,valid=true,detail=Progress});
                }
                else if(capture=="open")
                    SendObservation(2);
                else if(kind=="hatch" && capture=="closed-again") SendObservation(4);
                else if(kind=="hatch" && capture=="reopened") SendObservation(5);
                capture=null;captures++;
            }
            catch(Exception error) {Finish(false,error.Message,null);}
        }
        private static int LightingScenePhase(string phase)=>phase switch
        {"closed"=>70,"open"=>71,"closed-again"=>72,"reopened"=>73,"below"=>74,"reloaded"=>75,_=>throw new InvalidOperationException("Неизвестный кадр освещения люка: "+phase)};
        private IEnumerator RunLighting(string phase)
        {
            var iterator=VerifyLighting(phase);
            try
            {
                while(!finished)
                {
                    object next;
                    try {if(!iterator.MoveNext()) yield break;next=iterator.Current;}
                    catch(Exception error) {Finish(false,"Освещение люка: "+error.Message+": "+Progress,null);yield break;}
                    yield return next;
                }
            }
            finally {(iterator as IDisposable)?.Dispose();}
        }
        private IEnumerator VerifyLighting(string phase)
        {
            double requestedAt=Time.unscaledTimeAsDouble;
            RoomLighting current;
            RoomLayout room;
            long revision;
            while(true)
            {
                current=runtime.ActiveRoomLighting;room=runtime.Geometry.Get(Level);revision=runtime.Geometry.OcclusionRevision(Level);
                if(current!=null && !current.IsUpdating && current.AppliedOcclusionRevision==revision && ReferenceEquals(current.Room,room)) break;
                if(Time.unscaledTime>deadline || finished) throw new InvalidOperationException($"Нет актуальной пары света: phase={phase}, expectedRevision={revision}, applied={current?.AppliedOcclusionRevision}, updating={current?.IsUpdating}, sameRoom={current!=null && ReferenceEquals(current.Room,room)}.");
                yield return null;
            }
            if(!SystemInfo.supportsAsyncGPUReadback) throw new InvalidOperationException("GPU не поддерживает readback карт света; пиксельное свидетельство не получено.");
            string world=runtime.Session.World.worldId;int generation=Actor.generation;long doorRevision=Door.revision;
            var foreground=current.ForegroundMap;var background=current.BackgroundMap;var profile=current.Profile;var bounds=current.Bounds;
            RequireLightingSource(current,room,revision,world,generation,doorRevision,foreground,background);
            var observation=new LightingObservation {phase=phase,world=world,room=room.id,profile=RecoverySnapshot.Fingerprint(StateJson.ToJson(profile)),
                frame=Time.frameCount,generation=generation,scenePhase=LightingScenePhase(phase),graphics=SystemInfo.graphicsDeviceType.ToString(),
                doorRevision=doorRevision,occlusionRevision=revision,appliedRevision=current.AppliedOcclusionRevision,
                bounds=new LightBounds {x=bounds.x,y=bounds.y,width=bounds.width,height=bounds.height},requestedAt=requestedAt,
                initialBuildMilliseconds=current.InitialBuildMilliseconds,updateMilliseconds=current.LastUpdateMilliseconds,cpuMilliseconds=current.LastCpuMilliseconds,uploadMilliseconds=current.LastUploadMilliseconds,
                latencyMilliseconds=current.LastLatencyMilliseconds,candidatePixels=current.LastCandidatePixels,dirtyPixels=current.LastDirtyPixels,
                uniqueDecodedTextures=current.UniqueDecodedTextures,decodedPixels=current.DecodedPixels,playerPosition=Actor.Position,
                source=current,sourceRoom=room,sourceForeground=foreground,sourceBackground=background,sourceProfile=profile,sourceBounds=bounds};
            Texture2D expectedForeground=null,expectedBackground=null;
            try
            {
                // Deliberately expensive full rebuild is an independent phase check of the incremental cache.
                // It neither installs a profile nor binds shader globals or changes live geometry.
                var referenceTimer=System.Diagnostics.Stopwatch.StartNew();
                expectedForeground=RoomLightMap.Build(room,bounds,profile);
                expectedBackground=RoomLightMap.Build(room,bounds,profile.ForBackground());
                observation.referenceMilliseconds=referenceTimer.Elapsed.TotalMilliseconds;
                RequireLightingSource(current,room,revision,world,generation,doorRevision,foreground,background);
                var readbackTimer=System.Diagnostics.Stopwatch.StartNew();
                var actualFg=BeginLightReadback(foreground);
                var actualBg=BeginLightReadback(background);
                var expectedFg=BeginLightReadback(expectedForeground);
                var expectedBg=BeginLightReadback(expectedBackground);
                while(!actualFg.done || !actualBg.done || !expectedFg.done || !expectedBg.done)
                {
                    if(Time.unscaledTime>deadline || finished) throw new InvalidOperationException("Таймаут GPU readback света: "+phase);
                    yield return null;
                }
                if(actualFg.error!=null || actualBg.error!=null || expectedFg.error!=null || expectedBg.error!=null)
                    throw new InvalidOperationException($"GPU readback света завершился ошибкой: phase={phase}, actualFG={actualFg.error}, actualBG={actualBg.error}, expectedFG={expectedFg.error}, expectedBG={expectedBg.error}.");
                RequireLightingSource(current,room,revision,world,generation,doorRevision,foreground,background);
                if(!ReferenceEquals(current.Profile,profile) || RecoverySnapshot.Fingerprint(StateJson.ToJson(current.Profile))!=observation.profile || current.Bounds!=bounds)
                    throw new InvalidOperationException("Во время проверки изменился профиль/прямоугольник источника света: "+phase);
                byte[] fg=actualFg.pixels,bg=actualBg.pixels;
                byte[] expectedFgBytes=expectedFg.pixels,expectedBgBytes=expectedBg.pixels;
                observation.readbackMilliseconds=readbackTimer.Elapsed.TotalMilliseconds;
                observation.foreground=MapEvidence(foreground,expectedForeground,fg,expectedFgBytes,closedForeground,openForeground);
                observation.background=MapEvidence(background,expectedBackground,bg,expectedBgBytes,closedBackground,openBackground);
                if(phase=="reloaded")
                {
                    if(!Resume || observation.profile!=resumeExpected.profile ||
                        !string.Equals(observation.foreground.sha256,resumeExpected.foreground,StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(observation.background.sha256,resumeExpected.background,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Загруженный свет отличается от независимого GPU свидетельства сохранённого открытого люка.");
                }
                else if(phase=="closed") {closedForeground=fg;closedBackground=bg;}
                else
                {
                    int total=foreground.width*foreground.height;
                    if(observation.candidatePixels<=0 || observation.candidatePixels>=total || observation.dirtyPixels<=0 || observation.dirtyPixels>observation.candidatePixels)
                        throw new InvalidOperationException($"Изменение люка не дало ограниченный dirty patch: phase={phase}, candidate={observation.candidatePixels}, dirty={observation.dirtyPixels}, total={total}.");
                    if(phase=="open")
                    {
                        if(observation.foreground.changedPixels<=0 || observation.background.changedPixels<=0)
                            throw new InvalidOperationException("Открытие авторского люка не изменило пиксели обеих карт света.");
                        openForeground=fg;openBackground=bg;lightingChangedVerified=true;
                    }
                    else if(phase=="closed-again")
                    {
                        if(observation.foreground.closedMaxError>1 || observation.background.closedMaxError>1)
                            throw new InvalidOperationException("Закрытие люка не восстановило обе исходные карты света.");
                        lightingCloseRestored=true;
                    }
                    else if(phase=="reopened" && (observation.foreground.openMaxError>1 || observation.background.openMaxError>1))
                        throw new InvalidOperationException("Повторное открытие люка не восстановило обе открытые карты света.");
                }
                observation.sourceGuardVerified=observation.shaderBindingsVerified=true;observation.verifiedAt=Time.unscaledTimeAsDouble;
                WriteLightMap(phase,"foreground",foreground.width,foreground.height,fg);
                WriteLightMap(phase,"background",background.width,background.height,bg);
                lighting.Add(observation);lightingPending=null;
                File.WriteAllText(runtime.Options.Report+".opening-"+phase+".lighting.json",StateJson.ToJson(observation,true));
            }
            finally
            {
                if(expectedForeground!=null) Destroy(expectedForeground);
                if(expectedBackground!=null) Destroy(expectedBackground);
            }
        }
        private static LightReadback BeginLightReadback(Texture texture)
        {
            var result=new LightReadback();
            AsyncGPUReadback.Request(texture,0,TextureFormat.RGBA32,request=>
            {
                try
                {
                    if(request.hasError) result.error="GPU отказал в readback";
                    else result.pixels=request.GetData<byte>().ToArray();
                }
                catch(Exception error) {result.error=error.Message;}
                finally {result.done=true;}
            });
            return result;
        }
        private void RequireLightingSource(RoomLighting current,RoomLayout room,long revision,string world,int generation,long doorRevision,Texture foreground,Texture background)
        {
            if(current==null || runtime.ActiveRoomLighting!=current || runtime.Session.World?.worldId!=world || Actor?.generation!=generation ||
                Actor.level!=Level || Door?.revision!=doorRevision || !ReferenceEquals(current.Room,room) || !ReferenceEquals(runtime.Geometry.Get(Level),room) ||
                runtime.Geometry.OcclusionRevision(Level)!=revision || current.IsUpdating || current.AppliedOcclusionRevision!=revision ||
                foreground==null || background==null || current.ForegroundMap!=foreground || current.BackgroundMap!=background)
                throw new InvalidOperationException($"Источник света сменился/устарел: room={room.id}, world={world}, revision={revision}, applied={current?.AppliedOcclusionRevision}.");
            var bounds=current.Bounds;var shaderBounds=Shader.GetGlobalVector("_WorldLightBounds");
            if(Shader.GetGlobalTexture("_WorldLightMap")!=foreground || Shader.GetGlobalTexture("_BackgroundLightMap")!=background ||
                Shader.GetGlobalFloat("_WorldLightEnabled")!=1 || shaderBounds!=new Vector4(bounds.x,bounds.y,1/bounds.width,1/bounds.height))
                throw new InvalidOperationException("Actual shader binding не совпал с опубликованной парой карт/границ света: "+room.id);
        }
        private static LightingMapEvidence MapEvidence(Texture actual,Texture expected,byte[] pixels,byte[] reference,byte[] closed,byte[] opened)
        {
            if(actual.width!=expected.width || actual.height!=expected.height || pixels.Length!=actual.width*actual.height*4 || reference.Length!=pixels.Length)
                throw new InvalidOperationException("GPU размеры/readback света отличаются от fresh reference.");
            int maxError=MaximumError(pixels,reference);
            if(maxError>1) throw new InvalidOperationException($"GPU карта {actual.name} не совпала с fresh build: maxByteError={maxError}.");
            return new LightingMapEvidence {name=actual.name,width=actual.width,height=actual.height,bytes=pixels.Length,maxError=maxError,
                sha256=ByteHash(pixels),expectedSha256=ByteHash(reference),changedPixels=closed==null?0:ChangedPixels(pixels,closed),
                closedMaxError=closed==null?0:MaximumError(pixels,closed),openMaxError=opened==null?0:MaximumError(pixels,opened)};
        }
        private static int MaximumError(byte[] first,byte[] second)
        {
            if(first==null || second==null || first.Length!=second.Length) throw new InvalidOperationException("Не совпали размеры сравниваемых пикселей света.");
            int maximum=0;for(int index=0;index<first.Length;index++) maximum=Math.Max(maximum,Math.Abs(first[index]-second[index]));return maximum;
        }
        private static int ChangedPixels(byte[] first,byte[] second)
        {
            if(first==null || second==null || first.Length!=second.Length || first.Length%4!=0) throw new InvalidOperationException("Некорректный RGBA readback для сравнения света.");
            int changed=0;for(int index=0;index<first.Length;index+=4)
                if(first[index]!=second[index] || first[index+1]!=second[index+1] || first[index+2]!=second[index+2]) changed++;
            return changed;
        }
        private static string ByteHash(byte[] data)
        {using var hash=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(hash.ComputeHash(data)).Replace("-","");}
        private void WriteLightMap(string phase,string layer,int width,int height,byte[] pixels)
        {
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            try
            {texture.LoadRawTextureData(pixels);texture.Apply(false,false);File.WriteAllBytes(runtime.Options.Report+".opening-"+phase+".lighting-"+layer+".png",texture.EncodeToPNG());}
            finally {Destroy(texture);}
        }
        private WindowFrame WindowEvidence(SpriteRenderer artwork,Camera camera)
        {
            if(camera==null || artwork==null || artwork.sprite==null) throw new InvalidOperationException("Нет камеры или actual sprite окна для кадра.");
            var bounds=artwork.bounds;var physical=Door.Bounds;float plane=bounds.center.z;
            var artCorners=new[] {new Vector3(bounds.min.x,bounds.min.y,plane),new Vector3(bounds.max.x,bounds.min.y,plane),
                new Vector3(bounds.max.x,bounds.max.y,plane),new Vector3(bounds.min.x,bounds.max.y,plane)};
            var physicalCorners=new[] {new Vector3(physical.xMin,physical.yMin,plane),new Vector3(physical.xMax,physical.yMin,plane),
                new Vector3(physical.xMax,physical.yMax,plane),new Vector3(physical.xMin,physical.yMax,plane)};
            var artViewport=artCorners.Select(camera.WorldToViewportPoint).ToArray();var physicalViewport=physicalCorners.Select(camera.WorldToViewportPoint).ToArray();
            var all=artViewport.Concat(physicalViewport).ToArray();float depth=camera.WorldToViewportPoint(bounds.center).z;
            bool enabled=artwork.enabled && artwork.gameObject.activeInHierarchy && artwork.color.a>0 && (camera.cullingMask & (1<<artwork.gameObject.layer))!=0;
            return new WindowFrame {phase=capture,door=doorId,definition=Door.definition,sprite=artwork.sprite.name,width=Screen.width,height=Screen.height,
                rendererEnabled=enabled,fullyVisible=enabled && bounds.size.x>0 && bounds.size.y>0 && all.All(point=>
                    float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z) && point.x>=0 && point.x<=1 && point.y>=0 && point.y<=1 &&
                    point.z>=camera.nearClipPlane && point.z<=camera.farClipPlane),
                orthographic=camera.orthographic,orthographicSize=camera.orthographicSize,aspect=camera.aspect,nearClip=camera.nearClipPlane,farClip=camera.farClipPlane,
                cameraPosition=camera.transform.position,cameraForward=camera.transform.forward,cullingMask=camera.cullingMask,artworkLayer=artwork.gameObject.layer,
                artworkCenter=bounds.center,artworkSize=bounds.size,physicalMin=physical.min,physicalMax=physical.max,artworkViewport=artViewport,physicalViewport=physicalViewport,
                minX=all.Min(point=>point.x),maxX=all.Max(point=>point.x),minY=all.Min(point=>point.y),maxY=all.Max(point=>point.y),minDepth=all.Min(point=>point.z),maxDepth=all.Max(point=>point.z),
                cameraWorldCorners=new[] {camera.ViewportToWorldPoint(new Vector3(0,0,depth)),camera.ViewportToWorldPoint(new Vector3(1,0,depth)),
                    camera.ViewportToWorldPoint(new Vector3(1,1,depth)),camera.ViewportToWorldPoint(new Vector3(0,1,depth))}};
        }
        private void Finish(bool success,string detail,string hash)
        {
            if(finished) return;finished=true;snapshot=hash??snapshot;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail+": "+Progress,kind=kind,door=doorId,world=runtime.Session.World?.worldId,
                snapshot=snapshot,mode=Resume?"resume":"fresh",sourceSnapshot=resumeExpected?.snapshot,expectedDoorJson=expectedDoorJson,actualDoorJson=Door==null?null:StateJson.ToJson(Door),explicitInitialPlayerFixture=fixture!=null,closedObserved=closedObserved,openedObserved=openedObserved,destroyedObserved=destroyedObserved,
                geometryVerified=geometryVerified,traversalVerified=traversalVerified,savedReloadVerified=savedReloadVerified,reinstallVerified=reinstallVerified,
                shots=shots,acceptedInteractions=acceptedInteractions,captures=captures,health=Actor?.health??0,position=Actor?.Position??default,baseline=baseline,result=Door,
                rounds=Actor?.inventory.FirstOrDefault(i=>i.definition=="pistol" && i.weaponState!=null)?.weaponState.rounds??0,
                damageReplies=damageReplies,duplicateDamageVerified=duplicateDamageVerified,externalReloadVerified=externalReloadVerified,independentExpectedVerified=independentExpectedVerified,sourceArchiveVerified=sourceArchiveVerified,windowVisibleVerified=windowVisibleVerified,
                bankApproachVerified=bankApproachVerified,lightingVerified=lighting.Count==(Resume?1:4),lightingChangedVerified=lightingChangedVerified,lightingCloseRestored=lightingCloseRestored,lighting=lighting.ToArray()},true));
            exitCode=success?0:1;Invoke(nameof(Exit),.5f);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
    }
}
