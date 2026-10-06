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
using UnityEngine;
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
        {public int schema;public string world,snapshot,door;}
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
        }
        private PrototypeRuntime runtime;private Fixture fixture;private Baseline baseline;
        private bool traveled,installed,finished,completed,begun,openViewsReady,requestedOpen,requestedClose,fired,reported,aimReady;
        private bool closedObserved,openedObserved,destroyedObserved,geometryVerified,traversalVerified,savedReloadVerified,reinstallVerified;
        private float deadline,openedAt=-1;private int shots,acceptedInteractions,captures;
        private string kind,doorId,capture,lastCapture,snapshot;
        private DoorDamageRequestData damageRequest;private bool replaySent,duplicateDamageVerified;private int damageReplies;
        private bool diskProofStarted,fixtureActivated;
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
        private string Progress=>$"kind={kind}, door={doorId}, position={Actor?.Position}, HP={Actor?.health}, generation={Actor?.generation}/{baseline?.generation}, open={Door?.open}, destroyed={Door?.Destroyed}, shots={shots}, E={acceptedInteractions}, lastCapture={lastCapture}, pendingCapture={capture}, fixtureActive={fixtureActivated}, closed={closedObserved}, closedWitnesses={closedWitnesses.Count}, begun={begun}, witnesses={witnesses.Count}";
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
                if(kind!="window" || !Host || value.Options.ProbePeers!=1)
                    throw new InvalidOperationException("Resume проёма поддерживает только window host solo без fixture.");
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
                NetworkClient.RegisterHandler<OpeningProbeBegin>(message=>{if(message.phase==1) begun=true;else if(message.phase==2) openViewsReady=true;});
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
                    else throw new InvalidOperationException("Неизвестная фаза свидетельства проёма: "+message.phase);
                }
                catch(Exception error) {Finish(false,error.Message,null);}
            });
        }
        private PlayerCommand Command()
        {
            try
            {
                if(finished || !begun || !closedObserved || fixture==null || Actor==null || Actor.generation!=baseline.generation || !Driver) return default;
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
        private void RequireSelectedDoor(Vector2 cursor)
        {
            var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,cursor);
            if(selected.kind!=InteractionKind.Door || selected.id!=doorId) throw new InvalidOperationException($"E выбрал {selected.kind}/{selected.id}, ожидался {doorId}.");
        }
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
                if(door.open && !openedObserved) {openedObserved=true;openedAt=Time.unscaledTime;capture="open";}
                if(door.Destroyed && !destroyedObserved) {destroyedObserved=true;capture="destroyed";}
                bool ready=false;
                if(kind=="hatch" && openedObserved && actor.y<door.Bounds.yMin-.1f && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position))
                {traversalVerified=true;if(lastCapture=="open") capture="below";ready=lastCapture=="below";}
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
            if(!expected.Destroyed || expected.open || expected.CanInteract || expected.integrity.health!=0 || expected.revision!=1 || expected.integrity.resolvedAttacks.Count!=1)
                throw new InvalidOperationException("Pre-load свидетельство не содержит единственное сохранённое разрушение окна.");
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
                throw new InvalidOperationException("После production загрузки/установки изменились сохранённые HP/revision/receipt/данные окна.");
            var room=runtime.Geometry.Get(Level);var bounds=Door.Bounds;
            if(RoomSurfaces.All(room).Any(surface=>surface.sourceDoorId==doorId) ||
                ShotObstacles.FirstContact(room,new Vector2(bounds.xMin+.001f,bounds.center.y),new Vector2(bounds.xMax-.001f,bounds.center.y)).HasValue)
                throw new InvalidOperationException("После resume физический проём окна снова перекрывает свой aperture.");
            geometryVerified=true;destroyedObserved=true;
            if(resumeReadyAt<0)
            {
                var saved=session.Saves.LoadWorld(false);
                if(saved?.worldId!=resumeExpected.world || StateJson.ToJson(saved.doors.Single(d=>d.id==doorId))!=expectedDoorJson)
                    throw new InvalidOperationException("Selected save после production Join потерял exact сохранённое окно.");
                VerifySourceArchive();savedReloadVerified=true;externalReloadVerified=true;resumeReadyAt=Time.unscaledTime;capture="reloaded";
            }
            if(lastCapture=="reloaded")
            {
                VerifySourceArchive();
                Finish(true,"Новый внешний процесс загрузил тот же selected мир без fixture/нового выстрела: exact окно HP0/revision1/receipt1, свободная геометрия, настоящий destroyed sprite полностью в кадре и исходный snapshot сохранён.",resumeExpected.snapshot);
            }
        }
        private void Setup()
        {
            var session=runtime.Session;var world=session.ServerWorld;
            if(world.doors.Count(d=>d.level==Level)!=15) throw new InvalidOperationException("В заводе нет 15 ожидаемых проёмов");
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
                    capture=="open" && (!Door.open || Door.Destroyed) || capture=="closed-again" && (Door.open || Door.Destroyed) || capture=="destroyed" && !Door.Destroyed)
                    throw new InvalidOperationException("Кадр проёма не соответствует заявленной фазе: "+capture);
                if(Resume && (capture!="reloaded" || StateJson.ToJson(Door)!=expectedDoorJson))
                    throw new InvalidOperationException("Resume кадр потерял exact независимое состояние окна.");
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
                lastCapture=capture;
                if(capture=="closed")
                {
                    closedObserved=true;
                    NetworkClient.Send(new OpeningProbeObserved {actor=Actor.id,door=doorId,generation=Actor.generation,phase=0,valid=true,detail=Progress});
                }
                else if(capture=="open")
                    NetworkClient.Send(new OpeningProbeObserved {actor=Actor.id,door=doorId,generation=Actor.generation,phase=2,valid=true,detail=Progress});
                capture=null;captures++;
            }
            catch(Exception error) {Finish(false,error.Message,null);}
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
                damageReplies=damageReplies,duplicateDamageVerified=duplicateDamageVerified,externalReloadVerified=externalReloadVerified,independentExpectedVerified=independentExpectedVerified,sourceArchiveVerified=sourceArchiveVerified,windowVisibleVerified=windowVisibleVerified},true));
            exitCode=success?0:1;Invoke(nameof(Exit),.5f);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
    }
}
