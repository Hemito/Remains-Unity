using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Runtime
{
    public struct WarehouseFurnitureFixtureMessage : NetworkMessage {public string json;}
    public struct WarehouseFurniturePhaseMessage : NetworkMessage {public int phase;}
    public struct WarehouseFurnitureWitnessMessage : NetworkMessage {public string actor,claims;public int generation,phase;}
    public struct WarehouseFurnitureFinishMessage : NetworkMessage {public bool success;public string reason,json;}
    public struct WarehouseFurnitureDurableMessage : NetworkMessage {public string actor;public int generation;}
    public struct WarehouseFurnitureExitMessage : NetworkMessage { }

    // Initial player positions are explicit diagnostic fixtures. Authoring, inventories,
    // NPCs, HP and every subsequent input remain the ordinary production path.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryWarehouseFurnitureProbe : MonoBehaviour
    {
        private const string Level="factory:0",Prefix=Level+":original:plant:40:";
        private sealed class Spec
        {
            public readonly string key,code,definition;public readonly float x,y,width,height,mass;public readonly bool container,body;
            public Spec(string key,string code,string definition,float x,float y,float width,float height,float mass,bool container,bool body)
            {this.key=key;this.code=code;this.definition=definition;this.x=x;this.y=y;this.width=width;this.height=height;this.mass=mass;this.container=container;this.body=body;}
            public string Id=>Prefix+code;
        }
        private static readonly Spec[] specs={
            new("safe","detVw68Tuzey1MqU","safe",151.2f,10.4f,1.2f,1.2f,64.8f,true,true),
            new("locker-upper","EHzteZ0u3qpHCu8Q","locker",147.2f,13.6f,1.6f,2.38f,0,true,false),
            new("locker-entry","WLoJrSYbx2hxtRHc","locker",120.8f,7.2f,1.6f,2.38f,0,true,false),
            new("couch","Z9yOwJPvLO0MySmN","couch",147.2f,.8f,1.6f,.4f,25.6f,false,true)};
        [Serializable] public sealed class PlayerFixture
        {public string id,owner;public int generation,lifecycle,incomingRays,blockedRays;public float x,y,health;public bool exposed;}
        [Serializable] public sealed class Fixture
        {public string target,world,driver;public bool reload;public PlayerFixture[] players;public ItemStack[] items;public FurnitureResult initial;public ReloadEvidence reloadEvidence;}
        [Serializable] public sealed class ReloadPlayerTransition
        {public ActorState finished,persisted,connected;}
        [Serializable] public sealed class ReloadEvidence
        {public int schema=1,negativeChecks;public string world,persistedSnapshot;public ReloadPlayerTransition[] players;}
        [Serializable] public sealed class Claim
        {public string transaction,source,item,definition,recipient,canonicalItem;public int count,beforeCount,afterCount;public bool accepted,completed;}
        [Serializable] public sealed class FurnitureResult
        {public ContainerState safe,upper,entry;public PropState couch;public Claim[] claims;}
        [Serializable] public sealed class FinishEvidence
        {public string snapshot,world;public FurnitureResult result;public ActorState[] players;}
        [Serializable] public sealed class Frame
        {public int phase,nativeFrame,width=1920,height=1080;public string path,scene,state,source,sprite,texture;public bool visible,windowOpen,held;public Vector3 minimum,maximum;public Vector3[] viewport;public float actorX,actorY,propX,propY;}
        [Serializable] public sealed class Voice
        {public string source,definition,kind,clip;public int nativeId,samples,frame;public float volume,listenerVolume;public float? bankGain,expectedVolume;public bool playing,muted;}
        [Serializable] public sealed class CameraWait
        {public int phase,frames;public float seconds;public bool completed;public Vector3 cameraPosition,minimum,maximum;public Vector3[] viewport;}
        [Serializable] public sealed class Report
        {
            public int schema=1,participants,frameRate,phase,captures,openings,landings,shots,heals;
            public bool success,reload,explicitInitialPositionFixture,stockOverrides,originalRuntimeVerified,geometryVerified,
                claimsVerified,motionVerified,replicaVerified,durableVerified,characterVerified,reinstallVerified,cameraReady;
            public string detail,target,source,actor,world,snapshot,snapshotArchive,saveDirectory,characterDirectory,worldDirectory;
            public float health,x,y,movementDistance;public Fixture fixture;public FinishEvidence finish;public Frame[] frames;public Voice[] voices;public CameraWait[] cameraWaits;
            public string limitation="Четыре отдельные исходные позиции, не общий маршрут между ярусами. Только начальная позиция игроков — fixture. Верхний шкаф может быть открыт огню NPC; защита/лечение используют обычный стартовый запас. Замки/ловушки сейфа пока не реализованы: установлен явно выбранный unlocked/untrapped Unity вариант. Muted AudioSource доказывает ресурс/событие, не слышимый микс оригинала.";
        }
        private PrototypeRuntime runtime;private InteractionVisuals visuals;private Spec selected;private Fixture fixture;private PlayerFixture baseline;
        private Report reloadPrevious;private ReloadEvidence reloadEvidence;
        private FinishEvidence finish;private string archive;private int phase=-1,serverPhase=-1,captured=-1,reported=-1,commandFrame;
        private bool traveled,installed,active,done,subscriptionsDetached,edge,clicking,drained,finishPending,finishValidationStarted,durable,exitSent,reload;
        private float deadline,phaseAt,quietAt=-1,nextHeal,moveDistance;private string aimedThreat,notice;
        private readonly HashSet<string> witnesses=new(StringComparer.Ordinal),durablePeers=new(StringComparer.Ordinal),accepted=new(StringComparer.Ordinal);
        private readonly Dictionary<string,PendingGrant> reservations=new(StringComparer.Ordinal);
        private readonly List<Claim> claims=new();private readonly List<Frame> frames=new();private readonly List<Voice> voices=new();
        private readonly List<CameraWait> cameraWaits=new();private readonly Vector3[] framingCorners=new Vector3[4];private CameraWait cameraWait;private float cameraWaitAt;
        private readonly HashSet<int> voiceIds=new();private int shots,heals,openings,landings;
        private bool geometryVerified,claimsVerified,motionVerified,replicaVerified,characterVerified,reinstallVerified;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Driver=>fixture==null?null:runtime.Session.World.Find(fixture.driver);
        private PropState Target=>WorldProps.Find(runtime.Session.World,selected.Id);
        private ContainerState Container=>selected.container?runtime.Session.World.containers.Single(c=>c.id==selected.Id):null;
        private bool IsDriver=>fixture!=null && Actor?.id==fixture.driver;
        private string Progress=>$"target={selected?.key}, phase={phase}, actor={Actor?.id}/{Actor?.Position}, HP={Actor?.health}, life={Actor?.lifecycleSequence}, prop={Target?.Position}/{Target?.held}, pending={runtime.Session.World?.pendingGrants.Count}, claims={claims.Count}, captures={frames.Count}, cameraWait={cameraWait?.phase}/{cameraWait?.frames}/{cameraWait?.seconds}/{cameraWait?.completed}, camera={runtime.WorldCamera?.transform.position}, notice={notice}";

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+180;string smoke=value.Options.Smoke??"";
            reload=smoke.StartsWith("warehouse-furniture-reload-",StringComparison.Ordinal);
            string prefix=reload?"warehouse-furniture-reload-":"warehouse-furniture-";
            Require(smoke.StartsWith(prefix,StringComparison.Ordinal) && smoke.EndsWith("-frames-"+value.Options.Role,StringComparison.Ordinal),"Не задан точный opt-in режим мебели склада.");
            string key=smoke.Substring(prefix.Length,smoke.Length-prefix.Length-("-frames-"+value.Options.Role).Length);
            selected=specs.Single(s=>s.key==key);
            Require(value.Options.ProbePeers is 1 or 2 && value.Hud!=null && !string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report),"Проверка требует HUD, 1–2 участника и отдельные сохранения.");
            visuals=FindFirstObjectByType<InteractionVisuals>();Require(visuals!=null,"Нет настоящих представлений предметов.");
            AudioListener.volume=0; // Device output only. Each real voice still has its own positive gain.
            if(Host && !reload)
            {Require(value.Session.Saves.SelectedSnapshot()==null,"Fresh проверка не заменяет сохранённый мир.");value.Session.Saves.SaveWorld(NewGameWorld.Create());}
            if(reload)
            {
                Require(File.Exists(ReloadPath),"Нет явного свидетельства для загрузки копии: "+ReloadPath);
                if(Host) CaptureReloadBaseline(); // Before PrototypeRuntime.Start starts the session; never retain the live actor graph.
            }
            value.CommandSource=Command;value.Session.ClientReady+=RegisterClient;value.Session.ServerReady+=RegisterServer;
            value.Session.Changed+=ObserveReservations;value.Session.ClaimPrepared+=OnClaim;value.Session.AttackReceived+=OnAttack;value.Session.Notice+=OnNotice;
            value.Session.SelfHealingRequested+=OnHeal;value.ContainerAudio.Opened+=OnOpening;value.PropAudio.LandingPlayed+=OnLanding;
        }
        private string ReloadPath=>Path.Combine(runtime.Options.SaveDirectory,"warehouse-furniture-reload.json");
        private void CaptureReloadBaseline()
        {
            reloadPrevious=StateJson.FromJson<Report>(File.ReadAllText(ReloadPath));
            var source=runtime.Session.Saves.SelectedSnapshot();
            Require(source!=null && reloadPrevious?.success==true && reloadPrevious.target==selected.key &&
                reloadPrevious.world==source.WorldId && reloadPrevious.finish?.world==source.WorldId &&
                reloadPrevious.finish.players?.Length==runtime.Options.ProbePeers,"Чужое/неполное свидетельство persisted reload.");
            var persisted=source.CreateLiveWorld();
            var transitions=new List<ReloadPlayerTransition>();
            foreach(var finished in reloadPrevious.finish.players)
            {
                Require(transitions.All(p=>p.finished.id!=finished.id),"Повтор игрока в свидетельстве reload: "+finished.id);
                var body=persisted.actors.SingleOrDefault(a=>a.id==finished.id);
                VerifyReloadDisconnect(finished,body);
                transitions.Add(new ReloadPlayerTransition {finished=finished.Copy(),persisted=body.Copy()});
            }
            reloadEvidence=new ReloadEvidence {world=source.WorldId,persistedSnapshot=source.Id,players=transitions.ToArray(),
                negativeChecks=CheckReloadTransitionValidation()};
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<WarehouseFurnitureFixtureMessage>(m=>Guard(()=>
            {
                Require(fixture==null,"Повтор исходной fixture.");fixture=StateJson.FromJson<Fixture>(m.json);
                Require(fixture.world==runtime.Session.World.worldId && fixture.target==selected.key && fixture.reload==reload,"Fixture другой комнаты/мира/режима.");
                baseline=fixture.players.Single(p=>p.id==runtime.Session.LocalActorId);phase=0;phaseAt=Time.unscaledTime;
            }));
            NetworkClient.RegisterHandler<WarehouseFurniturePhaseMessage>(m=>Guard(()=>
            {Require(m.phase==phase+1,"Нарушен последовательный барьер мебели.");phase=m.phase;phaseAt=Time.unscaledTime;edge=false;quietAt=-1;commandFrame=0;}));
            NetworkClient.RegisterHandler<WarehouseFurnitureFinishMessage>(m=>Guard(()=>
            {if(!m.success){Finish(false,m.reason);return;}finish=StateJson.FromJson<FinishEvidence>(m.json);finishPending=true;}));
            NetworkClient.RegisterHandler<WarehouseFurnitureExitMessage>(_=>Guard(()=>
            {Require(durable,"Выход до собственного durable witness.");Finish(true,"Обычные действия, actual frames/voices и собственный immutable archive подтверждены.");}));
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<WarehouseFurnitureWitnessMessage>((connection,m)=>Guard(()=>
            {
                if(fixture==null || m.phase!=serverPhase) return;var actor=runtime.Session.ConnectedActor(connection);
                Require(actor!=null && actor.id==m.actor && actor.generation==m.generation && actor.level==Level &&
                    fixture.players.Any(p=>p.id==actor.id && p.owner==actor.owner && p.generation==actor.generation),"Барьер другого игрока/эпохи.");
                if(m.phase==2 && actor.id==fixture.driver && selected.container)
                {claims.Clear();claims.AddRange(StateJson.FromJson<Claim[]>(m.claims));ValidateClaims(runtime.Session.ServerWorld,claims);}
                witnesses.Add(actor.id);
            }));
            NetworkServer.RegisterHandler<WarehouseFurnitureDurableMessage>((connection,m)=>Guard(()=>
            {var actor=runtime.Session.ConnectedActor(connection);Require(actor!=null && actor.id==m.actor && actor.generation==m.generation,"Durable барьер другого игрока.");durablePeers.Add(actor.id);}));
        }
        private void OnNotice(string value){if(!done) notice=value;}
        private void OnHeal(string _){if(!done && active) heals++;}
        private void OnAttack(AttackEvent attack){if(!done && active && attack.attacker==runtime.Session.LocalActorId) shots++;}
        private void ObserveReservations()
        {
            if(done || fixture==null || !selected.container || runtime.Session.World==null) return;
            foreach(var pending in runtime.Session.World.pendingGrants)
                if(pending.containerId==selected.Id) reservations.TryAdd(pending.transaction,StateJson.Copy(pending));
        }
        private void OnClaim(string transaction,bool valid)
        {if(done || fixture==null || !IsDriver) return;if(!valid){Fail("Подбор выбранной мебели отклонён: "+transaction);return;}accepted.Add(transaction);}
        private void OnOpening(string source,string definition,AudioClip clip)
        {if(active && source==selected.Id) Guard(()=>{Require(runtime.ContainerAudio.TryGet(definition,out var expected,out float gain) && clip==expected,"Неверный кэш исходного звука открывания.");RecordVoice(source,definition,"open",clip,gain);openings++;});}
        private void OnLanding(string source,string definition,AudioClip clip)
        {if(active && !selected.container && phase==3 && source==selected.Id) Guard(()=>{Require(runtime.PropAudio.TryGetLandingClip(Target,out var expected,out _) && clip==expected,"Неверный кэш звука падения дивана.");RecordVoice(source,definition,"landing",clip);landings++;});}
        private void RecordVoice(string source,string definition,string kind,AudioClip clip,float? gain=null)
        {
            var matches=runtime.Audio.GetComponentsInChildren<AudioSource>().Where(v=>v.clip==clip && !voiceIds.Contains(v.GetInstanceID())).ToArray();
            Require(matches.Length==1,"Нет единственного нового actual AudioSource: "+source+"/"+kind);
            var voice=matches[0];Require(clip!=null && clip.samples>0 && voice.isPlaying && float.IsFinite(voice.volume) && voice.volume>0,"Пустой/тихий голос: "+source);
            float? expectedVolume=gain.HasValue?Mathf.Clamp01(runtime.Audio.Volume)*gain.Value*GameplayAudio.Attenuation(Vector2.Distance(Target.Position,Actor.BodyPosition)):null;
            Require(!expectedVolume.HasValue || Mathf.Abs(voice.volume-expectedVolume.Value)<.00001f,"Actual AudioSource потерял declared gain/attenuation: "+source);
            voiceIds.Add(voice.GetInstanceID());voices.Add(new Voice {source=source,definition=definition,kind=kind,clip=clip.name,nativeId=voice.GetInstanceID(),samples=clip.samples,frame=Time.frameCount,volume=voice.volume,listenerVolume=AudioListener.volume,bankGain=gain,expectedVolume=expectedVolume,playing=voice.isPlaying,muted=voice.mute});
        }
        private PlayerCommand Command()
        {
            if(done || !active || finishPending || !runtime.LevelPresentationReady || runtime.Session.Travel.Pending || runtime.Session.Pause.IsPaused(Level)) return default;
            try
            {
                var command=new PlayerCommand {hasAim=true,aim=Target.Center};commandFrame=Time.frameCount;
                if(reload) return command;
                if(selected.container)
                {
                    if(runtime.Hud.ContainerOpen)
                    {
                        var mouse=Mouse.current;if(mouse?.name=="SessionProbeMouse") {command.hasPointer=true;command.pointer=mouse.position.ReadValue();command.fire=mouse.leftButton.isPressed;}
                        if(phase==3 && !edge) {edge=true;command.interact=true;}
                        return command;
                    }
                    if((phase is 1 or 4) && !edge)
                    {
                        var target=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,command.aim);
                        Require(target.kind==InteractionKind.Container && target.id==selected.Id,"Обычный E выбрал другой источник: "+target.kind+"/"+target.id);
                        edge=true;command.interact=true;return command;
                    }
                    if(phase==0 || phase==3) return Defense(command);
                }
                else if(IsDriver)
                {
                    if(phase is 1 or 2)
                    {
                        command.telekinesis=true;command.aim=new Vector2(selected.x+(phase==2?.6f:0),selected.y+1.5f);
                        if(phase==1 && !Target.held)
                        {
                            command.aim=Target.Center;var nearest=TelekinesisTarget.Nearest(runtime.Session.World,Actor,command.aim);
                            Require(nearest.HasValue && nearest.Value.Id==selected.Id,"Q выбрал другой физический объект.");
                        }
                        float goal=fixture.players.Single(p=>p.id==fixture.driver).x+.45f;
                        if(phase==2 && goal-Actor.x>.035f) command.movement=Vector2.right*.2f;
                    }
                    else if(phase==4)
                    {float goal=fixture.players.Single(p=>p.id==fixture.driver).x;if(Actor.x-goal>.035f) command.movement=Vector2.left*.2f;}
                }
                return command;
            }
            catch(Exception error){Fail(error.Message);return default;}
        }
        private PlayerCommand Defense(PlayerCommand command)
        {
            // Only a real NPC targeting this player can trigger defensive input.
            if(Actor.health<60 && InventoryJournal.Count(Actor,"heal")>0 && Time.unscaledTime>=nextHeal)
            {nextHeal=Time.unscaledTime+1.5f;command.heal=true;}
            ActorState threat=null;float distance=float.PositiveInfinity;
            foreach(var actor in runtime.Session.World.actors)
                if(!actor.player && !actor.down && actor.health>0 && actor.level==Level && (actor.target==Actor.id || actor.attackBusy && actor.attackTarget==Actor.id))
                {float current=(actor.Position-Actor.Position).sqrMagnitude;if(current<distance){threat=actor;distance=current;}}
            if(threat==null){aimedThreat=null;if(quietAt<0) quietAt=Time.unscaledTime;return command;}
            var pistol=Actor.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
            command.weaponItem=Actor.equippedWeaponItem==pistol.id?null:pistol.id;command.aim=threat.BodyPosition+Vector2.up*.7f;
            command.reload=pistol.weaponState.rounds==0 && InventoryJournal.Count(Actor,"p10")>0;
            bool ally=false;var ray=command.aim-WeaponPose.Muzzle(Actor);
            foreach(var player in runtime.Session.World.actors) if(player.player && player.id!=Actor.id && player.connected && player.level==Level)
            {var point=player.BodyPosition+Vector2.up*.7f;float t=ray.sqrMagnitude==0?0:Mathf.Clamp01(Vector2.Dot(point-WeaponPose.Muzzle(Actor),ray)/ray.sqrMagnitude);ally|=Vector2.Distance(WeaponPose.Muzzle(Actor)+ray*t,point)<.8f;}
            command.fire=aimedThreat==threat.id && Actor.equippedWeaponItem==pistol.id && pistol.weaponState.rounds>0 && !ally;
            aimedThreat=threat.id;quietAt=-1;return command;
        }
        private void Update()=>Guard(Step);
        private void Step()
        {
            Require(Time.unscaledTime<=deadline,"Таймаут мебели: "+Progress);
            if(Actor==null || !NetworkClient.isConnected || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(!traveled){traveled=true;if(!reload) runtime.Session.Travel.RequestFactory();}
            if(Host && !installed && !runtime.Session.Travel.Pending && runtime.LevelPresentationReady &&
                runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level)) Setup();
            if(fixture==null || Actor.generation!=baseline.generation) return;
            Require(Actor.id==baseline.id && Actor.owner==baseline.owner && Actor.level==Level && Actor.connected && !Actor.down && Actor.health>0,"Игрок потерял действующий контекст: "+Progress);
            Require(Actor.lifecycleSequence>=baseline.lifecycle,"Откат жизненного цикла.");
            if(!runtime.LevelPresentationReady || runtime.IsLevelPreparing || runtime.Session.Travel.Pending) return;
            if(!active){if(!Supported(Actor)) return;active=true;phaseAt=Time.unscaledTime;geometryVerified=true;}
            if(finishPending)
            {
                if(!finishValidationStarted){finishValidationStarted=true;StartCoroutine(AwaitDurableArchive());}
                if(durable && Host && !exitSent && durablePeers.Count==runtime.Options.ProbePeers)
                {exitSent=true;NetworkServer.SendToAll(new WarehouseFurnitureExitMessage());}
                return;
            }
            if(phase==2 && selected.container && IsDriver && !clicking && !drained){clicking=true;StartCoroutine(Drain());}
            bool ready=Ready();
            if(ready && captured==phase && reported!=phase)
            {reported=phase;NetworkClient.Send(new WarehouseFurnitureWitnessMessage {actor=Actor.id,generation=Actor.generation,phase=phase,claims=selected.container && phase==2 && IsDriver?StateJson.ToJson(claims):null});}
            if(Host && witnesses.Count==runtime.Options.ProbePeers)
            {
                witnesses.Clear();
                if(reload || phase==4) CompleteServer();
                else {serverPhase=phase+1;NetworkServer.SendToAll(new WarehouseFurniturePhaseMessage {phase=serverPhase});}
            }
        }
        private bool Ready()
        {
            if(!active || commandFrame==0 && phase>0) return false;
            if(reload) return true;
            if(selected.container)
            {
                if(phase==0) return !runtime.Hud.ContainerOpen;
                if(phase==1 || phase==4) return runtime.Hud.ContainerOpen && runtime.Hud.ShownInventorySourceId==selected.Id && Container.appearance is {opened:true} && (phase==1 || Container.items.Count==0);
                if(phase==2) return Container.items.Count==0 && runtime.Session.World.pendingGrants.Count==0 && (!IsDriver || drained);
                return !runtime.Hud.ContainerOpen;
            }
            var prop=Target;var source=Driver;float start=fixture.players.Single(p=>p.id==fixture.driver).x;
            if(phase==0) return !prop.held && Mathf.Abs(prop.y-selected.y)<.002f && Mathf.Abs(prop.vx)+Mathf.Abs(prop.vy)<.01f;
            if(phase==1) return prop.held && prop.owner==source.owner && prop.y-selected.y>1 && Mathf.Abs(prop.vy)<.15f;
            if(phase==2)
            {moveDistance=source.x-start;return prop.held && prop.x-selected.x>.4f && moveDistance>.25f && Mathf.Abs(source.vx)<.01f && Mathf.Abs(prop.vx)+Mathf.Abs(prop.vy)<.15f;}
            bool released=!prop.held && Mathf.Abs(prop.y-selected.y)<.002f && Mathf.Abs(prop.vx)+Mathf.Abs(prop.vy)<.01f && prop.x-selected.x>.4f;
            if(phase==3) return released && landings>=1;
            return released && Mathf.Abs(source.x-start)<.09f && Mathf.Abs(source.vx)+Mathf.Abs(source.vy)<.01f;
        }
        private void Setup()
        {
            var world=runtime.Session.ServerWorld;Require(WorldRooms.Definition(world,Level)==NewGameWorld.FactoryDefinitionId,"Вместо обычного мира установлена legacy fixture.");
            ValidateDefinitions(world,!reload);var room=runtime.Geometry.Get(Level);var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            var result=CaptureResult(world);var baselines=new List<PlayerFixture>();Report previous=null;
            if(reload)
            {
                previous=reloadPrevious;
                Require(previous.success && previous.target==selected.key && previous.world==world.worldId,"Чужое свидетельство reload.");
                Require(reloadEvidence.world==world.worldId && players.Length==reloadEvidence.players.Length,"Изменился состав сохранённых тел при reload.");
                CompareResult(previous.finish.result,result);ValidateClaims(world,previous.finish.result.claims??Array.Empty<Claim>());
                claims.AddRange(previous.finish.result.claims??Array.Empty<Claim>());
            }
            foreach(var actor in players)
            {
                Vector2 position=actor.Position;
                if(!reload)
                {
                    bool driver=actor.id==runtime.Session.LocalActorId;
                    position=selected.key switch {"safe"=>new Vector2(driver?149.5f:149.3f,10.4f),"locker-upper"=>new Vector2(driver?147.8f:148f,13.6f),
                        "locker-entry"=>new Vector2(driver?119.5f:119.7f,7.2f),"couch"=>new Vector2(driver?145.55f:149.4f,.8f),_=>throw new InvalidOperationException("Неизвестная fixture.")};
                    CheckPlacement(world,room,actor,position);
                    actor.Position=position;actor.Velocity=Vector2.zero;actor.climbing=actor.crouched=false;actor.pose="idle";actor.poseTime=0;actor.generation++;actor.revision++;
                }
                else
                {
                    CheckPlacement(world,room,actor,position);
                    var transition=reloadEvidence.players.Single(p=>p.persisted.id==actor.id);
                    VerifyReloadConnect(transition.persisted,actor);transition.connected=actor.Copy();
                }
                CountCover(world,room,actor,out int rays,out int blocked);
                Require(selected.key=="locker-upper" || rays==blocked,"Исходная позиция потеряла actual solid incoming cover.");
                baselines.Add(new PlayerFixture {id=actor.id,owner=actor.owner,generation=actor.generation,lifecycle=actor.lifecycleSequence,x=position.x,y=position.y,health=actor.health,incomingRays=rays,blockedRays=blocked,exposed=rays!=blocked});
            }
            var target=WorldProps.Find(world,selected.Id);var items=selected.container?((ContainerState)target).items.Select(i=>i.Copy()).ToArray():Array.Empty<ItemStack>();
            if(!reload && selected.container)
            {
                var expected=selected.key=="safe"?new[]{("caps",12),("heal",1),("p10",12)}:new[]{("caps",3),("metal_comp",1)};
                Require(items.Length==expected.Length && expected.All(pair=>items.Count(i=>i.definition==pair.Item1 && i.count==pair.Item2)==1),"Неверный обычный initial loot новой мебели.");
            }
            installed=true;serverPhase=0;runtime.Session.CommitWorld();
            NetworkServer.SendToAll(new WarehouseFurnitureFixtureMessage {json=StateJson.ToJson(new Fixture {target=selected.key,world=world.worldId,driver=runtime.Session.LocalActorId,reload=reload,players=baselines.ToArray(),items=items,initial=result,reloadEvidence=reloadEvidence})});
            Debug.Log("Мебель склада: проверены actual floor/hull/props/NPC и incoming rays; fixture меняет только начальные позиции игроков. Верхний шкаф exposed, AI/HP/запасы не подменяются.");
        }
        private void CheckPlacement(WorldState world,RoomLayout room,ActorState actor,Vector2 position)
        {
            var hull=Anatomy.Movement(actor);var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
            Require(ActorTerrainMotion.IsClear(room,hull,position) && ActorTerrainMotion.Supported(room,hull,position),"Нет настоящей свободной опоры: "+actor.id+"/"+position);
            foreach(var prop in WorldProps.All(world)) if(prop.level==Level && prop.hasPhysicalBody)
                Require(!Interior(body,new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),"Fixture пересекает authored prop: "+prop.id);
            foreach(var npc in world.actors) if(!npc.player && npc.level==Level)
                Require(!Interior(body,InventorySource.BodyBounds(npc)),"Fixture пересекает настоящий NPC: "+npc.id);
            var sample=actor.Copy();sample.Position=position;
            if(selected.container){var target=InteractionSelection.Select(world,sample,runtime.Session.Rules,((InventorySource)(ContainerState)WorldProps.Find(world,selected.Id)).Bounds.center);Require(target.kind==InteractionKind.Container && target.id==selected.Id,"E недоступен из начальной позиции.");}
            else {var prop=WorldProps.Find(world,selected.Id);Require(runtime.Session.Rules.Telekinesis.Rejection(sample,prop,false)==null,"Диван недоступен обычному TK.");if(runtime.Session.Rules.Telekinesis is ITelekinesisSightRule sight) Require(sight.SightRejection(sample,prop,room)==null,"Нет настоящей TK линии видимости.");}
        }
        private static bool Interior(Rect a,Rect b)=>a.xMax>b.xMin+.0001f && a.xMin<b.xMax-.0001f && a.yMax>b.yMin+.0001f && a.yMin<b.yMax-.0001f;
        private static void CountCover(WorldState world,RoomLayout room,ActorState actor,out int rays,out int blocked)
        {
            rays=blocked=0;var body=InventorySource.BodyBounds(actor);var points=new[]{body.center,new Vector2(body.xMin+.02f,body.yMin+.02f),new Vector2(body.xMax-.02f,body.yMin+.02f),new Vector2(body.xMin+.02f,body.yMax-.02f),new Vector2(body.xMax-.02f,body.yMax-.02f)};
            foreach(var npc in world.actors) if(!npc.player && !npc.down && npc.health>0 && npc.level==Level && !string.IsNullOrEmpty(npc.weapon) && WeaponCatalog.Get(npc.weapon).projectileSpeed>0)
            {var sample=npc.Copy();NpcWeaponAim.Apply(sample,actor.BodyPosition+Vector2.up*.7f,room);var origin=WeaponPose.Muzzle(sample);foreach(var point in points){rays++;if(ShotObstacles.FirstContact(room,origin,point,out var cover).HasValue && !cover.oneWay && !cover.shotsPassThrough) blocked++;}}
        }
        private bool Supported(ActorState actor)=>!actor.climbing && Mathf.Abs(actor.vy)<.001f && ActorTerrainMotion.Supported(runtime.Geometry.Get(Level),Anatomy.Movement(actor),actor.Position);
        private IEnumerator Drain()
        {
            while(!done && Container.items.Count>0)
            {
                var item=Container.items[0].Copy();int before=InventoryJournal.Count(Actor,item.definition);int shotBaseline=shots;
                var pointer=UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0),followTarget:true);
                try {while(!done){bool next;try{next=pointer.MoveNext();}catch(Exception error){Fail(error.Message);yield break;}if(!next) break;yield return pointer.Current;}}
                finally{(pointer as IDisposable)?.Dispose();}
                PendingGrant receipt=null;
                while(!done)
                {
                    receipt=reservations.Values.SingleOrDefault(g=>g.item.id==item.id && g.containerId==selected.Id && g.recipient==Actor.id);
                    if(receipt!=null && accepted.Contains(receipt.transaction) && runtime.Session.World.completedTransactions.Contains(receipt.transaction) && runtime.Session.World.pendingGrants.Count==0 && !Container.items.Any(i=>i.id==item.id)) break;
                    yield return null;
                }
                if(done) yield break;
                int after=InventoryJournal.Count(Actor,item.definition);var stack=InventoryStacks.Find(Actor,item.id);
                if(after!=before+item.count || stack==null || shots!=shotBaseline){Fail("Whole-stack UI изменил количество/потратил патрон/потерял alias: "+item.id);yield break;}
                claims.Add(new Claim {transaction=receipt.transaction,source=selected.Id,item=item.id,definition=item.definition,count=item.count,recipient=Actor.id,canonicalItem=stack.id,beforeCount=before,afterCount=after,accepted=true,completed=true});
            }
            clicking=false;drained=true;claimsVerified=true;
        }
        private void LateUpdate()=>Guard(()=>
        {
            if(!active || finishPending || captured==phase || !runtime.LevelPresentationReady || !Ready()) return;
            // Room readiness is independent of camera follow. The initial fixture
            // changes the actor epoch, while production Reset keeps the old center.
            // Wait for actual framing before either strict artwork validator runs.
            var view=selected.container?visuals.FindContainer(selected.Id):visuals.FindProp(selected.Id);var camera=runtime.WorldCamera;
            Require(view!=null && camera!=null && camera.cullingMask!=0 && view.Artwork!=null && view.Artwork.sprite!=null,"Нет настоящей готовой камеры/рисунка мебели.");
            var renderer=view.Artwork;var bounds=renderer.bounds;
            if(!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.color.a<=0 || (camera.cullingMask&(1<<renderer.gameObject.layer))==0)
                throw new InvalidOperationException("Actual рисунок мебели скрыт/отключён: "+selected.Id);
            bool framed=ProjectFrame(camera,bounds,framingCorners);
            if(!framed)
            {
                if(cameraWait==null || cameraWait.phase!=phase)
                {
                    cameraWait=new CameraWait {phase=phase,viewport=new Vector3[4]};cameraWaits.Add(cameraWait);cameraWaitAt=Time.unscaledTime;
                    Debug.Log($"Ожидание штатного кадра мебели: source={selected.Id}, phase={phase}, bounds={bounds}, camera={camera.transform.position}. Камера продолжает обычное следование; границы проверки не расширены.");
                }
                cameraWait.frames++;cameraWait.seconds=Time.unscaledTime-cameraWaitAt;cameraWait.cameraPosition=camera.transform.position;
                cameraWait.minimum=bounds.min;cameraWait.maximum=bounds.max;Array.Copy(framingCorners,cameraWait.viewport,4);
                return; // Bounded by the unchanged global probe deadline.
            }
            if(cameraWait!=null && cameraWait.phase==phase)
            {
                cameraWait.completed=true;cameraWait.seconds=Time.unscaledTime-cameraWaitAt;cameraWait.cameraPosition=camera.transform.position;
                cameraWait.minimum=bounds.min;cameraWait.maximum=bounds.max;Array.Copy(framingCorners,cameraWait.viewport,4);
            }
            Capture(view,camera,bounds,framingCorners.ToArray());captured=phase;
        });
        private static bool ProjectFrame(Camera camera,Bounds bounds,Vector3[] corners)
        {
            float z=bounds.center.z;var minimum=bounds.min;var maximum=bounds.max;
            corners[0]=camera.WorldToViewportPoint(new Vector3(minimum.x,minimum.y,z));
            corners[1]=camera.WorldToViewportPoint(new Vector3(maximum.x,minimum.y,z));
            corners[2]=camera.WorldToViewportPoint(new Vector3(maximum.x,maximum.y,z));
            corners[3]=camera.WorldToViewportPoint(new Vector3(minimum.x,maximum.y,z));
            bool framed=true;
            foreach(var point in corners)
            {
                if(!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                    throw new InvalidOperationException("Неконечная проекция мебели в штатной камере: "+camera.name+"/"+bounds);
                framed&=point.x>=0 && point.x<=1 && point.y>=0 && point.y<=1 && point.z>=camera.nearClipPlane && point.z<=camera.farClipPlane;
            }
            return framed;
        }
        private void Capture(PropView view,Camera camera,Bounds bounds,Vector3[] corners)
        {
            if(selected.container) ContainerArtworkProbeEvidence.Save(runtime,Container,phase.ToString(),runtime.Options.Report+".furniture-"+phase+".art.json");
            var renderer=view.Artwork;
            Require(renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a>0 && (camera.cullingMask&(1<<renderer.gameObject.layer))!=0 && ProjectFrame(camera,bounds,corners),"Actual мебель обрезана/скрыта штатной камерой.");
            if(selected.container && phase is 1 or 2 or 4)
                Require(runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy && runtime.Hud.ShownInventorySourceId==selected.Id,"Нет actual UI выбранного источника.");
            string path=runtime.Options.Report+".furniture-"+phase+".png";NetworkFrameCapture.Save(camera,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);NetworkSceneRecord.Save(runtime,600+phase);
            frames.Add(new Frame {phase=phase,nativeFrame=Time.frameCount,path=path,scene=runtime.Options.Report+".phase-"+(600+phase)+".scene.json",state=reload?"reloaded":selected.container?new[]{"closed","open","empty","ui-closed","reopened"}[phase]:new[]{"grounded","held","moved","released","returned"}[phase],source=selected.Id,sprite=renderer.sprite.name,texture=renderer.sprite.texture.name,visible=true,windowOpen=runtime.Hud.ContainerOpen,held=Target.held,minimum=bounds.min,maximum=bounds.max,viewport=corners,actorX=Actor.x,actorY=Actor.y,propX=Target.x,propY=Target.y});
        }
        private static FurnitureResult CaptureResult(WorldState world)=>new() {safe=StateJson.Copy(world.containers.Single(c=>c.id==specs[0].Id)),upper=StateJson.Copy(world.containers.Single(c=>c.id==specs[1].Id)),entry=StateJson.Copy(world.containers.Single(c=>c.id==specs[2].Id)),couch=StateJson.Copy(world.props.Single(p=>p.id==specs[3].Id))};
        private static PropState[] Parts(FurnitureResult value)=>new PropState[]{value.safe,value.upper,value.entry,value.couch};
        private static void CompareResult(FurnitureResult expected,FurnitureResult actual)
        {
            var left=Parts(expected);var right=Parts(actual);
            for(int i=0;i<left.Length;i++)
            {
                var a=left[i];var b=right[i];Require(a.id==b.id && a.definition==b.definition && a.level==b.level && a.mass==b.mass && a.width==b.width && a.height==b.height && a.anchorHeight==b.anchorHeight && a.movable==b.movable && a.hasPhysicalBody==b.hasPhysicalBody && a.held==b.held && Vector2.Distance(a.Position,b.Position)<.0001f && Mathf.Abs(a.vx-b.vx)+Mathf.Abs(a.vy-b.vy)<.0001f,"Реплика/disk изменила физическое состояние мебели: "+a.id);
                if(a is ContainerState ca && b is ContainerState cb) Require(StateJson.ToJson(ca.items)==StateJson.ToJson(cb.items) && StateJson.ToJson(ca.itemAliases)==StateJson.ToJson(cb.itemAliases) && StateJson.ToJson(ca.appearance)==StateJson.ToJson(cb.appearance),"Реплика/disk изменила вещи/alias/рисунок мебели: "+a.id);
                Require(StateJson.ToJson(a.extensions)==StateJson.ToJson(b.extensions),"Потеря неизвестных полей мебели: "+a.id);
            }
        }
        private static void ValidateDefinitions(WorldState world,bool initial)
        {
            foreach(var spec in specs)
            {
                var prop=WorldProps.Find(world,spec.Id);Require(prop!=null && prop.definition==spec.definition && prop.level==Level && prop.hasPhysicalBody==spec.body && prop.movable==spec.body && prop.mass==spec.mass && prop.width==spec.width && prop.height==spec.height && prop.anchorHeight==0,"Разошёлся exact authored контракт мебели: "+spec.Id);
                Require((prop is ContainerState)==spec.container,"Мебель ошибочно заменена декором/контейнером.");
                if(initial || !spec.body) Require(Vector2.Distance(prop.Position,new Vector2(spec.x,spec.y))<.0001f,"Перемещён исходный marker мебели: "+spec.Id);
            }
        }
        private static void ValidateClaims(WorldState world,IEnumerable<Claim> evidence)
        {
            var unique=new HashSet<string>(StringComparer.Ordinal);
            foreach(var claim in evidence)
            {
                Require(claim.accepted && claim.completed && claim.count>0 && claim.afterCount==claim.beforeCount+claim.count && unique.Add(claim.transaction) && world.completedTransactions.Count(t=>t==claim.transaction)==1 && world.pendingGrants.All(g=>g.transaction!=claim.transaction),"Неполная/повторная durable выдача: "+claim.transaction);
                var recipient=world.Find(claim.recipient);Require(recipient!=null && InventoryStacks.ResolveId(recipient.inventoryAliases,claim.item)==claim.canonicalItem,"Потерян canonical recipient/alias: "+claim.item);
                Require(world.containers.Single(c=>c.id==claim.source).items.All(i=>i.id!=claim.item),"Воскресла исходная стопка.");
                foreach(var actor in world.actors) if(actor.player && actor.id!=claim.recipient) Require(!actor.inventory.Any(i=>i.id==claim.item) && !actor.inventoryAliases.ContainsKey(claim.item),"Выдача наложилась второму игроку.");
            }
        }
        private void CompleteServer()
        {
            var world=runtime.Session.ServerWorld;ValidateDefinitions(world,false);ValidateClaims(world,claims);
            if(selected.container && !reload) Require(claims.Count==fixture.items.Length && world.containers.Single(c=>c.id==selected.Id).items.Count==0,"Подобраны не все реальные стопки.");
            if(!selected.container && !reload) Require(world.props.Single(p=>p.id==selected.Id).x-selected.x>.4f,"Диван не перенесён обычным TK.");
            runtime.Session.CommitWorld();runtime.Session.FlushSaveWrites();var snapshot=runtime.Session.Saves.SelectedSnapshot();
            var result=CaptureResult(world);result.claims=claims.ToArray();var saved=snapshot.CreateLiveWorld();CompareResult(result,CaptureResult(saved));ValidateClaims(saved,claims);
            finish=new FinishEvidence {world=world.worldId,snapshot=snapshot.Id,result=result,players=world.actors.Where(a=>a.player && a.connected).Select(a=>a.Copy()).ToArray()};
            NetworkServer.SendToAll(new WarehouseFurnitureFinishMessage {success=true,json=StateJson.ToJson(finish)});
        }
        private IEnumerator AwaitDurableArchive()
        {
            Guard(()=>
            {
                Require(finish.world==fixture.world && finish.snapshot?.Length==64 && finish.snapshot.All(Uri.IsHexDigit),"Некорректный финальный снимок.");
                archive=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",fixture.world,"Snapshots",finish.snapshot+".json");
                runtime.Session.FlushSaveWrites();
            });
            if(done) yield break;
            // Poll only the explicit final archive milestone, at a bounded cadence.
            // No profile writes, JSON parsing or content reads occur while waiting.
            while(!done && !File.Exists(archive)) yield return new WaitForSecondsRealtime(.1f);
            if(!done) Guard(VerifyDurableArchive);
        }
        private void VerifyDurableArchive()
        {
            CompareResult(finish.result,CaptureResult(runtime.Session.World));ValidateClaims(runtime.Session.World,finish.result.claims);replicaVerified=true;
            var canonical=finish.players.Single(p=>p.id==Actor.id);ComparePlayer(canonical,Actor,"final replica");
            runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();
            var snapshot=new RecoverySnapshot(File.ReadAllText(archive));Require(snapshot.Id==finish.snapshot && snapshot.WorldId==fixture.world,"Собственный архив содержит другой immutable снимок.");
            var saved=snapshot.CreateLiveWorld();CompareResult(finish.result,CaptureResult(saved));ValidateClaims(saved,finish.result.claims);
            ComparePlayer(canonical,saved.Find(Actor.id),"immutable archive");
            ComparePlayer(Actor,runtime.Session.Saves.LoadCharacter(),"own character disk");characterVerified=true;
            string before=StateJson.ToJson(saved);Require(!RoomContentInstaller.Apply(saved,RoomContentBatch.Read(RoomAuthoring.Load(Level,saved),Level)) && StateJson.ToJson(saved)==before,"Reinstall наполнил/воскресил или заменил сохранённые данные.");reinstallVerified=true;
            Require(frames.Count==(reload?1:5) && (reload || (selected.container?openings==2:landings>=1)),"Нет полного actual frame/audio evidence.");
            motionVerified=reload || selected.container || moveDistance>.25f;claimsVerified=reload || !selected.container || finish.result.claims.Length==fixture.items.Length;
            durable=true;NetworkClient.Send(new WarehouseFurnitureDurableMessage {actor=Actor.id,generation=Actor.generation});
        }
        private static void VerifyReloadDisconnect(ActorState finished,ActorState persisted)
        {
            Require(finished!=null && finished.player && finished.connected && finished.level==Level && persisted!=null,
                "Нет завершённого игрока/сохранённого тела для Disconnect reload.");
            var expected=finished.Copy();ActorSessionRules.Disconnect(expected);
            ComparePlayer(expected,persisted,"reload finish → persisted Disconnect");CompareReloadContext(expected,persisted,"Disconnect");
        }
        private static void VerifyReloadConnect(ActorState persisted,ActorState connected)
        {
            Require(persisted!=null && persisted.player && !persisted.connected && persisted.level==Level && connected!=null && !string.IsNullOrEmpty(connected.owner),
                "Нет сохранённого тела/владельца для Connect reload.");
            var expected=persisted.Copy();ActorSessionRules.Connect(expected,connected.owner);
            ComparePlayer(expected,connected,"reload persisted → actual Connect");CompareReloadContext(expected,connected,"Connect");
        }
        private static void CompareReloadContext(ActorState expected,ActorState actual,string stage)
        {
            Require(expected.player==actual.player && expected.connected==actual.connected && expected.controlStateVersion==actual.controlStateVersion &&
                expected.pose==actual.pose && expected.level==actual.level && expected.x==actual.x && expected.y==actual.y &&
                expected.vx==actual.vx && expected.vy==actual.vy && expected.climbing==actual.climbing && expected.crouched==actual.crouched,
                $"Изменился контекст тела при reload/{stage}: actor={actual.id}, life={expected.lifecycleSequence}/{actual.lifecycleSequence}, connected={expected.connected}/{actual.connected}, pose={expected.pose}/{actual.pose}, level={expected.level}/{actual.level}, position={expected.Position}/{actual.Position}.");
        }
        // Focused opt-in checks exercise the same two validators; no live actor is mutated.
        public static int CheckReloadTransitionValidation()
        {
            var finished=StateJson.FromJson<ActorState>("{\"reloadUnknown\":{\"exact\":0.1234567890123456789}}");
            finished.id="player:reload-check";finished.character="reload-check";finished.player=finished.connected=true;
            finished.level=Level;finished.lifecycleSequence=7;finished.controlStateVersion=ActorSessionRules.ControlStateVersion;
            finished.x=4;finished.y=8;finished.inventory.Add(new ItemStack {id="reload-ammo",definition="p10",count=12});
            finished.inventory.Add(new ItemStack {id="reload-pistol",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=12}});
            finished.inventoryAliases.Add("reload-loot","reload-ammo");finished.resolvedAttacks.Add("reload-attack");
            var persisted=finished.Copy();ActorSessionRules.Disconnect(persisted);VerifyReloadDisconnect(finished,persisted);
            var connected=persisted.Copy();ActorSessionRules.Connect(connected,"connection:reload-check");VerifyReloadConnect(persisted,connected);
            Action<ActorState>[] mutations={a=>a.lifecycleSequence--,a=>a.lifecycleSequence++,a=>a.connected=!a.connected,
                a=>a.controlStateVersion=0,a=>a.pose="run",a=>a.level="hub",a=>a.x+=.01f,a=>a.health--,a=>a.down=!a.down,
                a=>a.damageSequence++,a=>a.inventory[0].count--,a=>a.inventory[1].weaponState.rounds--,
                a=>a.inventoryAliases.Clear(),a=>a.resolvedAttacks.Clear(),a=>a.extensions.Clear(),a=>a.character="another-character",
                a=>a.id="player:another-body",a=>a.climbing=true,a=>a.crouched=true};
            int rejected=0;
            foreach(var mutation in mutations)
            {
                var bad=persisted.Copy();mutation(bad);RejectReloadTransition(()=>VerifyReloadDisconnect(finished,bad));rejected++;
                bad=connected.Copy();mutation(bad);RejectReloadTransition(()=>VerifyReloadConnect(persisted,bad));rejected++;
            }
            return rejected;
        }
        private static void RejectReloadTransition(Action action)
        {
            bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}
            Require(rejected,"Проверка reload приняла повреждённый Disconnect/Connect переход.");
        }
        private static void ComparePlayer(ActorState expected,ActorState actual,string context)
        {
            Require(actual!=null && expected.id==actual.id && expected.character==actual.character && expected.health==actual.health && expected.down==actual.down &&
                expected.lifecycleSequence==actual.lifecycleSequence && expected.damageSequence==actual.damageSequence &&
                StateJson.ToJson(expected.inventory)==StateJson.ToJson(actual.inventory) && StateJson.ToJson(expected.inventoryAliases)==StateJson.ToJson(actual.inventoryAliases) &&
                StateJson.ToJson(expected.inventoryUses)==StateJson.ToJson(actual.inventoryUses) && StateJson.ToJson(expected.injuries)==StateJson.ToJson(actual.injuries) &&
                StateJson.ToJson(expected.resolvedAttacks)==StateJson.ToJson(actual.resolvedAttacks) && StateJson.ToJson(expected.resolvedExplosions)==StateJson.ToJson(actual.resolvedExplosions) &&
                StateJson.ToJson(expected.cosmeticEquipment)==StateJson.ToJson(actual.cosmeticEquipment) && StateJson.ToJson(expected.extensions)==StateJson.ToJson(actual.extensions),
                "Игрок потерял actual HP/lifecycle/ammo/inventory/receipts/unknown: "+context+"/"+expected.id);
        }
        private void Guard(Action action){if(done)return;try{action();}catch(Exception error){Fail(error.Message);}}
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        private void Fail(string reason){if(done)return;if(Host && NetworkServer.active)NetworkServer.SendToAll(new WarehouseFurnitureFinishMessage {reason=reason+": "+Progress});Finish(false,reason);}
        private void Finish(bool success,string reason)
        {
            if(done)return;done=true;DetachSubscriptions();
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=reason+": "+Progress,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,phase=phase,target=selected.key,source=selected.Id,actor=Actor?.id,world=runtime.Session.World?.worldId,snapshot=finish?.snapshot,snapshotArchive=archive,saveDirectory=runtime.Options.SaveDirectory,characterDirectory=runtime.Session.Saves.CharacterDirectoryPath,worldDirectory=runtime.Session.Saves.WorldDirectoryPath,reload=reload,explicitInitialPositionFixture=!reload && fixture!=null,stockOverrides=false,originalRuntimeVerified=false,geometryVerified=geometryVerified,claimsVerified=claimsVerified,motionVerified=motionVerified,replicaVerified=replicaVerified,durableVerified=durable,characterVerified=characterVerified,reinstallVerified=reinstallVerified,cameraReady=runtime.LevelPresentationReady,captures=frames.Count,openings=openings,landings=landings,shots=shots,heals=heals,health=Actor?.health??0,x=Actor?.x??0,y=Actor?.y??0,movementDistance=moveDistance,fixture=fixture,finish=finish,frames=frames.ToArray(),voices=voices.ToArray(),cameraWaits=cameraWaits.ToArray()},true));exitCode=success?0:1;Invoke(nameof(Exit),.75f);
        }
        private int exitCode;private void Exit()=>Application.Quit(exitCode);
        private void OnDestroy()=>DetachSubscriptions();
        private void DetachSubscriptions()
        {
            if(subscriptionsDetached || runtime?.Session==null)return;subscriptionsDetached=true;
            runtime.Session.ClientReady-=RegisterClient;runtime.Session.ServerReady-=RegisterServer;runtime.Session.Changed-=ObserveReservations;runtime.Session.ClaimPrepared-=OnClaim;runtime.Session.AttackReceived-=OnAttack;runtime.Session.Notice-=OnNotice;runtime.Session.SelfHealingRequested-=OnHeal;
            if(runtime.ContainerAudio!=null)runtime.ContainerAudio.Opened-=OnOpening;if(runtime.PropAudio!=null)runtime.PropAudio.LandingPlayed-=OnLanding;
        }
    }
}
