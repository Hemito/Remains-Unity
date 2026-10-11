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
        private const string Level="factory:0";
        private static readonly FurnitureActionSpec[] specs=FurnitureActionSpec.Warehouse();
        [Serializable] public sealed class PlayerFixture
        {public string id,owner;public int generation,lifecycle,incomingRays,blockedRays;public float x,y,health;public bool exposed;}
        [Serializable] public sealed class Fixture
        {public string target,world,driver;public bool reload;public PlayerFixture[] players;public ItemStack[] items;public FurnitureResult initial;public ReloadEvidence reloadEvidence;}
        [Serializable] public sealed class ReloadPlayerTransition
        {public ActorState canonicalFinished,finished,persisted,connected;}
        [Serializable] public sealed class ReloadEvidence
        {public int schema=1,negativeChecks;public string world,persistedSnapshot;public ReloadPlayerTransition[] players;}
        [Serializable] public sealed class Claim
        {public string transaction,source,item,definition,recipient,canonicalItem;public int count,beforeCount,afterCount;public bool accepted,completed;}
        [Serializable] public sealed class FurnitureResult
        {public ContainerState safe,upper,entry,cup,wallcab;public PropState couch;public Claim[] claims;}
        [Serializable] public sealed class FinishEvidence
        {public string snapshot,world;public FurnitureResult result;public ActorState[] players;}
        [Serializable] public sealed class Frame
        {public int phase,nativeFrame,width=1920,height=1080,generation;public string path,scene,state,source,sprite,texture,owner;public bool visible,windowOpen,held;public Vector3 minimum,maximum;public Vector3[] viewport;public float actorX,actorY,propX,propY,mana;}
        [Serializable] public sealed class Voice
        {public string source,definition,kind,clip;public int nativeId,samples,frame;public float volume,listenerVolume;public float? bankGain,expectedVolume;public bool playing,muted;}
        [Serializable] public sealed class CameraWait
        {public int phase,frames;public float seconds;public bool completed;public Vector3 cameraPosition,minimum,maximum;public Vector3[] viewport;}
        [Serializable] public sealed class ReceiptEvidence
        {public string world,snapshot,expectedProfileJson,persistedProfileJson;public long revision;public double capturedAt,persistedAt;public ActorState player;}
        [Serializable] public sealed class DamageWitness
        {public DamageResult result;public AttackEvent attack;public ActorState source,after;public int frame;public double at;}
        [Serializable] public sealed class PlayerTransition
        {public string context;public ActorState before,after;public DamageWitness[] damage;public double checkedAt;}
        [Serializable] public sealed class TelekinesisRejectionEvidence
        {public string actor,source,kind,rule,expected,actual,displayedNotice;public int beforeFrame,noticeFrame;public float maximumMass,manaBefore,manaAtNotice,minimumMana;public PropState before,after;public bool noticeVisible;public Vector3[] noticeViewport;}
        [Serializable] public sealed class Report
        {
            public int schema=1,participants,frameRate,phase,captures,openings,landings,shots,heals;
            public bool success,reload,explicitInitialPositionFixture,stockOverrides,originalRuntimeVerified,geometryVerified,
                claimsVerified,motionVerified,replicaVerified,durableVerified,characterVerified,reinstallVerified,cameraReady,telekinesisVerified,fixedRejectionVerified;
            public string detail,target,source,actor,world,snapshot,snapshotArchive,saveDirectory,characterDirectory,worldDirectory;
            public float health,x,y,movementDistance,approachDistance,liftDistance;public int qCommands;public string fixedRejection;public Fixture fixture;public FinishEvidence finish;public Frame[] frames;public Voice[] voices;public CameraWait[] cameraWaits;
            public ReceiptEvidence receipt;public ActorState finishReceivedPlayer,verifiedPlayer,terminalPlayer;public string verificationProfileJson;
            public PlayerTransition[] playerTransitions;
            public TelekinesisRejectionEvidence telekinesisRejection;
            public string limitation="Четыре отдельные исходные позиции, не общий маршрут между ярусами. Только начальная позиция игроков — fixture. Верхний шкаф может быть открыт огню NPC; защита/лечение используют обычный стартовый запас. Замки/ловушки сейфа пока не реализованы: установлен явно выбранный unlocked/untrapped Unity вариант. Muted AudioSource доказывает ресурс/событие, не слышимый микс оригинала.";
        }
        private PrototypeRuntime runtime;private InteractionVisuals visuals;private FurnitureActionSpec selected;private Fixture fixture;private PlayerFixture baseline;
        private Report reloadPrevious;private ReloadEvidence reloadEvidence;
        private FinishEvidence finish;private string archive;private int phase=-1,serverPhase=-1,captured=-1,reported=-1,commandFrame;
        private bool traveled,installed,active,done,subscriptionsDetached,edge,clicking,drained,finishPending,finishValidationStarted,durable,exitSent,reload;
        private float deadline,phaseAt,quietAt=-1,nextHeal,moveDistance,approachDistance,liftDistance;private string aimedThreat,notice,fixedRejection;
        private int qCommands;private bool fixedRejectionVerified,telekinesisVerified;
        private float maximumTkMass;private TelekinesisRejectionEvidence telekinesisRejection;
        private readonly HashSet<string> witnesses=new(StringComparer.Ordinal),durablePeers=new(StringComparer.Ordinal),accepted=new(StringComparer.Ordinal);
        private readonly Dictionary<string,PendingGrant> reservations=new(StringComparer.Ordinal);
        private readonly List<Claim> claims=new();private readonly List<Frame> frames=new();private readonly List<Voice> voices=new();
        private readonly List<CameraWait> cameraWaits=new();private readonly Vector3[] framingCorners=new Vector3[4];private CameraWait cameraWait;private float cameraWaitAt;
        private readonly HashSet<int> voiceIds=new();private int shots,heals,openings,landings;
        private bool geometryVerified,claimsVerified,motionVerified,replicaVerified,characterVerified,reinstallVerified;
        private readonly Dictionary<string,ReceiptEvidence> receipts=new(StringComparer.Ordinal);
        private readonly List<DamageWitness> ownerDamage=new();private readonly List<PlayerTransition> playerTransitions=new();
        private readonly Dictionary<string,(AttackEvent attack,ActorState source)> npcAttacks=new(StringComparer.Ordinal);
        private FurnitureResult finishReceivedResult;private ActorState finishReceivedPlayer,verifiedPlayer,terminalPlayer;private string verificationProfileJson;
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
            selected=FurnitureActionSpec.Find(key);
            if(selected.Continuation)maximumTkMass=TelekinesisSettings.Load().maximumMass;
            Require(value.Options.ProbePeers is 1 or 2 && value.Hud!=null && !string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report),"Проверка требует HUD, 1–2 участника и отдельные сохранения.");
            visuals=FindFirstObjectByType<InteractionVisuals>();Require(visuals!=null,"Нет настоящих представлений предметов.");
            AudioListener.volume=0; // Device output only. Each real voice still has its own positive gain.
            if(Host && !reload)
            {Require(value.Session.Saves.SelectedSnapshot()==null,"Fresh проверка не заменяет сохранённый мир.");value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());}
            if(reload)
            {
                Require(File.Exists(ReloadPath),"Нет явного свидетельства для загрузки копии: "+ReloadPath);
                if(Host) CaptureReloadBaseline(); // Before PrototypeRuntime.Start starts the session; never retain the live actor graph.
            }
            value.CommandSource=Command;value.Session.ClientReady+=RegisterClient;value.Session.ServerReady+=RegisterServer;
            value.Session.Changed+=ObserveReservations;value.Session.ClaimPrepared+=OnClaim;value.Session.AttackReceived+=OnAttack;value.Session.Notice+=OnNotice;
            value.Session.SelfHealingRequested+=OnHeal;value.ContainerAudio.Opened+=OnOpening;value.PropAudio.LandingPlayed+=OnLanding;
            if(selected.Continuation){value.Session.RecoveryReceiptPrepared+=OnReceiptPrepared;value.Session.RecoveryReceiptPersisted+=OnReceiptPersisted;value.Combat.DamageApplied+=OnOwnerDamage;}
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
            foreach(var canonical in reloadPrevious.finish.players)
            {
                ActorState finished=canonical;
                if(selected.Continuation)
                {
                    Require(canonical.id is "player:session-host" or "player:session-client-1","Чужое имя participant report reload.");
                    string path=Path.Combine(runtime.Options.SaveDirectory,"warehouse-furniture-reload-players",canonical.id.Substring("player:".Length)+"-report.json");
                    var previous=StateJson.FromJson<Report>(File.ReadAllText(path));ValidateCapturedProgress(previous,canonical,reloadPrevious);
                    finished=previous.terminalPlayer;
                }
                Require(transitions.All(p=>p.finished.id!=finished.id),"Повтор игрока в свидетельстве reload: "+finished.id);
                var body=persisted.actors.SingleOrDefault(a=>a.id==finished.id);
                VerifyReloadDisconnect(finished,body);
                transitions.Add(new ReloadPlayerTransition {canonicalFinished=selected.Continuation?canonical.Copy():null,finished=finished.Copy(),persisted=body.Copy()});
            }
            reloadEvidence=new ReloadEvidence {world=source.WorldId,persistedSnapshot=source.Id,players=transitions.ToArray(),
                negativeChecks=CheckReloadTransitionValidation()};
        }
        private void RegisterClient()
        {
            if(selected.Continuation)Require(runtime.Session.Rules.Telekinesis is MassLimitedTelekinesisRule,
                "Проверка default TK refusal требует actual MassLimitedTelekinesisRule.");
            NetworkClient.RegisterHandler<WarehouseFurnitureFixtureMessage>(m=>Guard(()=>
            {
                Require(fixture==null,"Повтор исходной fixture.");fixture=StateJson.FromJson<Fixture>(m.json);
                Require(fixture.world==runtime.Session.World.worldId && fixture.target==selected.key && fixture.reload==reload,"Fixture другой комнаты/мира/режима.");
                baseline=fixture.players.Single(p=>p.id==runtime.Session.LocalActorId);phase=0;phaseAt=Time.unscaledTime;
            }));
            NetworkClient.RegisterHandler<WarehouseFurniturePhaseMessage>(m=>Guard(()=>
            {Require(m.phase==phase+1,"Нарушен последовательный барьер мебели.");phase=m.phase;phaseAt=Time.unscaledTime;edge=false;quietAt=-1;commandFrame=0;}));
            NetworkClient.RegisterHandler<WarehouseFurnitureFinishMessage>(m=>Guard(()=>
            {
                if(!m.success){Finish(false,m.reason);return;}finish=StateJson.FromJson<FinishEvidence>(m.json);
                if(selected.Continuation)
                {finishReceivedPlayer=Actor.Copy();finishReceivedResult=CaptureResult(runtime.Session.World);CompareResult(finish.result,finishReceivedResult);
                    VerifyPlayerTransition(finish.players.Single(p=>p.id==Actor.id),finishReceivedPlayer,"canonical → Finish receive");}
                finishPending=true;
            }));
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
        private void OnNotice(string value)
        {
            if(done)return;notice=value;
            if(phase!=5 || telekinesisRejection==null || value!=telekinesisRejection.expected)return;
            Require(Actor.id==telekinesisRejection.actor && !Target.held && Actor.mana==telekinesisRejection.manaBefore,"Отказ TK изменил удержание/ману до actual Notice.");
            telekinesisRejection.actual=value;telekinesisRejection.noticeFrame=Time.frameCount;telekinesisRejection.manaAtNotice=Actor.mana;
            telekinesisRejection.after=StateJson.Copy(Target);
            if(selected.FixedRejection)fixedRejectionVerified=true;
        }
        private void OnHeal(string _){if(!done && active) heals++;}
        private void OnAttack(AttackEvent attack)=>Guard(()=>
        {
            if(done)return;if(active && attack.attacker==runtime.Session.LocalActorId)shots++;
            if(!selected.Continuation)return;var source=runtime.Session.World?.Find(attack.attacker);
            if(source==null || source.player)return;
            Require(npcAttacks.Count<512 || npcAttacks.ContainsKey(attack.id),"Переполнен ограниченный кэш actual NPC attacks мебели.");
            var observation=(StateJson.Copy(attack),source.Copy());npcAttacks[attack.id]=observation;
            foreach(var witness in ownerDamage)if(witness.result.attack==attack.id && witness.attack==null){witness.attack=observation.Item1;witness.source=observation.Item2;}
        });
        private void OnReceiptPrepared(RecoverySnapshot snapshot,ActorState player)=>Guard(()=>
        {
            Require(snapshot!=null && player!=null && player.id==runtime.Session.LocalActorId && receipts.Count<128 && !receipts.ContainsKey(snapshot.Id),"Нет уникального ограниченного receipt capture мебели.");
            receipts.Add(snapshot.Id,new ReceiptEvidence {world=snapshot.WorldId,snapshot=snapshot.Id,revision=snapshot.Revision,
                capturedAt=Time.realtimeSinceStartupAsDouble,player=player.Copy(),expectedProfileJson=ExpectedProfile(player)});
        });
        private void OnReceiptPersisted(RecoverySnapshot snapshot,string json)=>Guard(()=>
        {
            Require(snapshot!=null && receipts.TryGetValue(snapshot.Id,out var receipt) && receipt.world==snapshot.WorldId && receipt.revision==snapshot.Revision && receipt.persistedProfileJson==null && json!=null,"Receipt запись не связана с capture мебели.");
            var value=receipts[snapshot.Id];Require(value.expectedProfileJson==json,"Профиль completed FIFO операции не равен независимому frozen actor capture: "+snapshot.Id);
            value.persistedProfileJson=json;value.persistedAt=Time.realtimeSinceStartupAsDouble;
        });
        private static string ExpectedProfile(ActorState player)
        {var copy=player.Copy();ContentProvenance.Record(ref copy.weaponContent,ref copy.weaponContentHistory);return StateJson.ToJson(copy,true);}
        private void OnOwnerDamage(DamageResult result)=>Guard(()=>
        {
            if(result.actor!=Actor?.id)return;
            Require(ownerDamage.Count<128 && !ownerDamage.Any(d=>d.result.attack==result.attack) && result.level==Actor.level && result.generation==Actor.generation && float.IsFinite(result.amount) && result.amount>0,"Нет уникального actual owner DamageApplied мебели: "+result.attack);
            npcAttacks.TryGetValue(result.attack,out var origin);
            ownerDamage.Add(new DamageWitness {result=result,attack=origin.attack,source=origin.source,after=Actor.Copy(),frame=Time.frameCount,at=Time.realtimeSinceStartupAsDouble});
        });
        private void VerifyPlayerTransition(ActorState before,ActorState after,string context)
            =>playerTransitions.Add(CheckPlayerTransition(before,after,ownerDamage,context));
        private static PlayerTransition CheckPlayerTransition(ActorState before,ActorState after,IEnumerable<DamageWitness> observations,string context)
        {
            Require(before!=null && after!=null && before.id==after.id && before.character==after.character && before.owner==after.owner &&
                before.generation==after.generation && before.lifecycleSequence==after.lifecycleSequence && before.connected && after.connected &&
                before.level==Level && after.level==Level && !before.down && !after.down && before.health>0 && after.health>0,"Изменился действующий контекст frozen player transition: "+context);
            var expected=before.Copy();var changes=new List<DamageWitness>();
            Require(after.resolvedAttacks.Take(before.resolvedAttacks.Count).SequenceEqual(before.resolvedAttacks),"Удалён/переставлен исторический attack receipt: "+context);
            foreach(var id in after.resolvedAttacks.Skip(before.resolvedAttacks.Count))
            {
                var witness=observations.SingleOrDefault(d=>d.result.attack==id);
                Require(witness!=null && witness.attack!=null && witness.attack.id==id && witness.attack.level==after.level && !witness.attack.sourcePlayer &&
                    witness.source!=null && !witness.source.player && witness.source.id==witness.attack.attacker && witness.source.level==after.level &&
                    witness.result.actor==after.id && witness.result.level==after.level && witness.result.generation==after.generation &&
                    witness.after.damageSequence==expected.damageSequence+1 && witness.after.health==Mathf.Max(0,expected.health-witness.result.amount) && !witness.after.down,
                    "Поздний receipt не подтверждён настоящим owner damage/точным HP и sequence: "+context+"/"+id);
                expected.resolvedAttacks.Add(id);expected.health=witness.after.health;expected.damageSequence=witness.after.damageSequence;
                ComparePlayer(expected,witness.after,context+" / actual DamageApplied");changes.Add(witness);
            }
            ComparePlayer(expected,after,context);
            return new PlayerTransition {context=context,before=before.Copy(),after=after.Copy(),damage=changes.ToArray(),checkedAt=Time.realtimeSinceStartupAsDouble};
        }
        private static void ValidateCapturedProgress(Report previous,ActorState canonical,Report authority)
        {
            Require(previous?.success==true && previous.actor==canonical.id && previous.world==authority.world && previous.snapshot==authority.snapshot &&
                previous.target==authority.target && StateJson.ToJson(previous.finish)==StateJson.ToJson(authority.finish) && previous.receipt!=null &&
                previous.receipt.world==previous.world && previous.receipt.snapshot==previous.snapshot && previous.receipt.expectedProfileJson==previous.receipt.persistedProfileJson &&
                previous.verifiedPlayer!=null && previous.verificationProfileJson==ExpectedProfile(previous.verifiedPlayer) && previous.terminalPlayer!=null && previous.playerTransitions?.Length==4,"Нет независимого complete participant progress для reload: "+canonical.id);
            var before=new[]{canonical,canonical,previous.receipt.player,previous.verifiedPlayer};
            var after=new[]{previous.finishReceivedPlayer,previous.receipt.player,previous.verifiedPlayer,previous.terminalPlayer};
            for(int i=0;i<4;i++)
            {
                var transition=previous.playerTransitions[i];ComparePlayer(before[i],transition.before,"reload progress before");ComparePlayer(after[i],transition.after,"reload progress after");
                CheckPlayerTransition(before[i],after[i],transition.damage,"reload / "+transition.context);
            }
        }
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
        {if(active && selected.Transport && phase==selected.LandingPhase && source==selected.Id) Guard(()=>{Require(runtime.PropAudio.TryGetLandingClip(Target,out var expected,out _) && clip==expected,"Неверный кэш звука фактического падения: "+selected.Id);RecordVoice(source,definition,"landing",clip);landings++;});}
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
                if(selected.Continuation && phase>=5) return ContinuationAction(command);
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
                    if(phase==0 || phase==3)
                    {
                        command=Defense(command);
                        if(selected.Continuation && phase==0)
                        {float delta=selected.ApproachX(IsDriver)-Actor.x;if(Mathf.Abs(delta)>.012f)command.movement=Vector2.right*(Math.Sign(delta)*.15f);}
                        return command;
                    }
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
        private PlayerCommand ContinuationAction(PlayerCommand command)
        {
            // The ordinary E consumer first closes the reopened source. No UI
            // state is assigned, and no Q begins through a still-open grid.
            if(runtime.Hud.ContainerOpen)
            {if(!edge){edge=true;command.interact=true;}return command;}
            if(telekinesisRejection==null)
            {
                var nearest=TelekinesisTarget.Nearest(runtime.Session.World,Actor,command.aim);
                Require(nearest.HasValue && nearest.Value.Id==selected.Id,"Обычный Q выбрал иной объект вместо cup/wallcab.");
                string expected=runtime.Session.Rules.Telekinesis.Rejection(Actor,Target,false);
                Require(!string.IsNullOrEmpty(expected),"Правило разрешило TK вместо ожидаемого штатного отказа: "+selected.Id);
                Require(selected.FixedRejection?!Target.movable&&!Target.hasPhysicalBody:Target.movable&&Target.hasPhysicalBody&&Target.mass>maximumTkMass,
                    "Нет фактической причины default TK rejection: "+selected.Id);
                telekinesisRejection=new TelekinesisRejectionEvidence {actor=Actor.id,source=selected.Id,kind=selected.FixedRejection?"fixed":"too-heavy",
                    rule=runtime.Session.Rules.Telekinesis.GetType().FullName,expected=expected,before=StateJson.Copy(Target),beforeFrame=Time.frameCount,
                    maximumMass=maximumTkMass,manaBefore=Actor.mana,minimumMana=Actor.mana};
                if(selected.FixedRejection)fixedRejection=expected;
            }
            command.telekinesis=true;qCommands++;return command;
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
            command.weaponItem=Actor.equippedWeaponItem==pistol.id?null:pistol.id;
            command.aim=threat.BodyPosition+Vector2.up*(selected.Continuation?Anatomy.Movement(threat).height*.5f:.7f);
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
                if(reload || phase==selected.FinalPhase) CompleteServer();
                else {serverPhase=phase+1;NetworkServer.SendToAll(new WarehouseFurniturePhaseMessage {phase=serverPhase});}
            }
        }
        private bool Ready()
        {
            if(!active || commandFrame==0 && phase>0) return false;
            if(reload) return true;
            if(selected.container)
            {
                if(selected.Continuation)
                {
                    if(phase==0)
                    {approachDistance=Mathf.Abs(Actor.x-baseline.x);return !runtime.Hud.ContainerOpen && Mathf.Abs(Actor.x-selected.ApproachX(IsDriver))<.035f && approachDistance>.065f && quietAt>=0 && Time.unscaledTime-quietAt>.75f;}
                    if(phase==3)return !runtime.Hud.ContainerOpen && quietAt>=0 && Time.unscaledTime-quietAt>.75f;
                    if(phase>=5)return ContinuationReady();
                }
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
        private bool ContinuationReady()
        {
            if(runtime.Hud.ContainerOpen)return false;
            if(telekinesisRejection==null)return false;
            telekinesisRejection.minimumMana=Mathf.Min(telekinesisRejection.minimumMana,Actor.mana);
            Require(!Target.held && Vector2.Distance(Target.Position,telekinesisRejection.before.Position)<.0001f &&
                Target.generation==telekinesisRejection.before.generation && telekinesisRejection.minimumMana>=telekinesisRejection.manaBefore,
                "Обычный отказ TK изменил предмет/ману: "+selected.Id);
            return qCommands>=2 && telekinesisRejection.actual==telekinesisRejection.expected &&
                telekinesisRejection.noticeFrame>=telekinesisRejection.beforeFrame && Time.unscaledTime-phaseAt>.25f;
        }
        private void Setup()
        {
            var world=runtime.Session.ServerWorld;Require(WorldRooms.Definition(world,Level)==LegacyFactoryFixture.DefinitionId,"Нужен исторический мир со старым собранным заводом (LegacyFactoryFixture).");
            ValidateDefinitions(world,!reload);var room=runtime.Geometry.Get(Level);var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            if(selected.Continuation)
            {
                Require(runtime.Session.Rules.Telekinesis is MassLimitedTelekinesisRule,"Проверка default TK rejection требует текущий MassLimitedTelekinesisRule, без подмены mod правил.");
                Require(selected.FixedRejection || selected.mass>maximumTkMass,"Cup не превышает фактический предел TK: изменён acceptance context.");
            }
            var result=CaptureResult(world);var baselines=new List<PlayerFixture>();Report previous=null;
            // Validate EVERY initial player before changing any epoch/position.
            // Actual authored terrain, cargo, NPC and access remain the authority.
            if(!reload)foreach(var player in players)CheckPlacement(world,room,player,selected.Start(player.id==runtime.Session.LocalActorId));
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
                    position=selected.Start(driver);
                    actor.Position=position;actor.Velocity=Vector2.zero;actor.climbing=actor.crouched=false;actor.pose="idle";actor.poseTime=0;actor.generation++;actor.revision++;
                }
                else
                {
                    CheckPlacement(world,room,actor,position);
                    var transition=reloadEvidence.players.Single(p=>p.persisted.id==actor.id);
                    VerifyReloadConnect(transition.persisted,actor);transition.connected=actor.Copy();
                }
                CountCover(world,room,actor,out int rays,out int blocked);
                Require(selected.Continuation || selected.key=="locker-upper" || rays==blocked,"Исходная позиция потеряла actual solid incoming cover.");
                baselines.Add(new PlayerFixture {id=actor.id,owner=actor.owner,generation=actor.generation,lifecycle=actor.lifecycleSequence,x=position.x,y=position.y,health=actor.health,incomingRays=rays,blockedRays=blocked,exposed=rays!=blocked});
            }
            var target=WorldProps.Find(world,selected.Id);var items=selected.container?((ContainerState)target).items.Select(i=>i.Copy()).ToArray():Array.Empty<ItemStack>();
            if(!reload && selected.container)
            {
                var expected=selected.Loot();
                Require(items.Length==expected.Length && expected.All(pair=>items.Count(i=>i.definition==pair.Item1 && i.count==pair.Item2)==1),"Неверный обычный initial loot новой мебели.");
            }
            installed=true;serverPhase=0;runtime.Session.CommitWorld();
            NetworkServer.SendToAll(new WarehouseFurnitureFixtureMessage {json=StateJson.ToJson(new Fixture {target=selected.key,world=world.worldId,driver=runtime.Session.LocalActorId,reload=reload,players=baselines.ToArray(),items=items,initial=result,reloadEvidence=reloadEvidence})});
            Debug.Log("Действия мебели: проверены actual floor/hull/props/NPC и incoming rays; fixture меняет только начальные позиции игроков. AI/HP/запасы не подменяются; это отдельный action proof, не полный маршрут комнаты.");
        }
        private void CheckPlacement(WorldState world,RoomLayout room,ActorState actor,Vector2 position)
        {
            var hull=Anatomy.Movement(actor);var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
            bool clear=ActorTerrainMotion.IsClear(room,hull,position),supported=ActorTerrainMotion.Supported(room,hull,position);
            Require(clear && supported,$"Нет настоящей свободной опоры: actor={actor.id}, source={selected.Id}, position={position}, hull={body}, clear={clear}, supported={supported}, room={room.id}.");
            foreach(var prop in WorldProps.All(world)) if(prop.level==Level && prop.hasPhysicalBody)
                Require(!Interior(body,new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),"Fixture пересекает authored prop: "+prop.id);
            foreach(var npc in world.actors) if(!npc.player && npc.level==Level)
                Require(!Interior(body,InventorySource.BodyBounds(npc)),"Fixture пересекает настоящий NPC: "+npc.id);
            var sample=actor.Copy();sample.Position=position;
            if(selected.container){var target=InteractionSelection.Select(world,sample,runtime.Session.Rules,((InventorySource)(ContainerState)WorldProps.Find(world,selected.Id)).Bounds.center);Require(target.kind==InteractionKind.Container && target.id==selected.Id,"E недоступен из начальной позиции.");
                if(selected.Continuation)Require(runtime.Session.Rules.Telekinesis.Rejection(sample,WorldProps.Find(world,selected.Id),false)!=null,"Не подтверждён исходный штатный отказ TK cup/wallcab до fixture.");}
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
            if(selected.Continuation && !reload && phase==5)
            {
                // Explicit evidence capture only: do not turn UI hierarchy lookup
                // into the command/Update path or infer visibility from Notice alone.
                var matches=runtime.Hud.GetComponentsInChildren<UnityEngine.UI.Text>(false)
                    .Where(candidate=>candidate.enabled && candidate.color.a>0 && candidate.text==telekinesisRejection.expected).ToArray();
                Require(matches.Length==1,"Нет единственного actual visible HUD текста штатного отказа TK: "+selected.Id);
                var text=matches[0];Require(text.GetComponentsInParent<CanvasGroup>(false).All(group=>group.alpha>0),"HUD отказ TK скрыт прозрачной группой.");
                var canvas=text.GetComponentInParent<Canvas>();var points=new Vector3[4];text.rectTransform.GetWorldCorners(points);
                Camera uiCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
                Require(canvas.enabled && (canvas.renderMode==RenderMode.ScreenSpaceOverlay || uiCamera!=null),"Нет actual Canvas/camera HUD отказа TK.");
                for(int i=0;i<points.Length;i++)
                {
                    var pixel=RectTransformUtility.WorldToScreenPoint(uiCamera,points[i]);
                    points[i]=new Vector3(pixel.x/Screen.width,pixel.y/Screen.height,0);
                    Require(float.IsFinite(points[i].x) && float.IsFinite(points[i].y) && points[i].x>=0 && points[i].x<=1 && points[i].y>=0 && points[i].y<=1,
                        "HUD текст штатного отказа TK вне actual экрана: "+selected.Id);
                }
                telekinesisRejection.displayedNotice=text.text;telekinesisRejection.noticeViewport=points;telekinesisRejection.noticeVisible=true;
            }
            if(selected.container) ContainerArtworkProbeEvidence.Save(runtime,Container,phase.ToString(),runtime.Options.Report+".furniture-"+phase+".art.json");
            var renderer=view.Artwork;
            Require(renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a>0 && (camera.cullingMask&(1<<renderer.gameObject.layer))!=0 && ProjectFrame(camera,bounds,corners),"Actual мебель обрезана/скрыта штатной камерой.");
            if(selected.container && phase is 1 or 2 or 4)
                Require(runtime.Hud.ContainerWindowRect.gameObject.activeInHierarchy && runtime.Hud.ShownInventorySourceId==selected.Id,"Нет actual UI выбранного источника.");
            string path=runtime.Options.Report+".furniture-"+phase+".png";NetworkFrameCapture.Save(camera,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);NetworkSceneRecord.Save(runtime,600+phase);
            frames.Add(new Frame {phase=phase,nativeFrame=Time.frameCount,path=path,scene=runtime.Options.Report+".phase-"+(600+phase)+".scene.json",state=reload?"reloaded":selected.States[phase],source=selected.Id,sprite=renderer.sprite.name,texture=renderer.sprite.texture.name,visible=true,windowOpen=runtime.Hud.ContainerOpen,held=Target.held,owner=Target.owner,generation=Target.generation,minimum=bounds.min,maximum=bounds.max,viewport=corners,actorX=Actor.x,actorY=Actor.y,propX=Target.x,propY=Target.y,mana=Actor.mana});
        }
        private FurnitureResult CaptureResult(WorldState world)=>new() {safe=StateJson.Copy(world.containers.Single(c=>c.id==specs[0].Id)),upper=StateJson.Copy(world.containers.Single(c=>c.id==specs[1].Id)),entry=StateJson.Copy(world.containers.Single(c=>c.id==specs[2].Id)),couch=StateJson.Copy(world.props.Single(p=>p.id==specs[3].Id)),
            cup=selected.Continuation?StateJson.Copy(world.containers.Single(c=>c.id==FurnitureActionSpec.Find("cup").Id)):null,
            wallcab=selected.Continuation?StateJson.Copy(world.containers.Single(c=>c.id==FurnitureActionSpec.Find("wallcab").Id)):null};
        private static PropState[] Parts(FurnitureResult value)=>new PropState[]{value.safe,value.upper,value.entry,value.couch}.Concat(
            value.cup==null && value.wallcab==null?Array.Empty<PropState>():new PropState[]{value.cup,value.wallcab}).ToArray();
        private static void CompareResult(FurnitureResult expected,FurnitureResult actual)
        {
            var left=Parts(expected);var right=Parts(actual);Require(left.Length==right.Length && left.All(p=>p!=null) && right.All(p=>p!=null),"Разошёлся exact набор проверяемой мебели.");
            for(int i=0;i<left.Length;i++)
            {
                var a=left[i];var b=right[i];Require(a.id==b.id && a.definition==b.definition && a.level==b.level && a.mass==b.mass && a.width==b.width && a.height==b.height && a.anchorHeight==b.anchorHeight && a.movable==b.movable && a.hasPhysicalBody==b.hasPhysicalBody && a.held==b.held && Vector2.Distance(a.Position,b.Position)<.0001f && Mathf.Abs(a.vx-b.vx)+Mathf.Abs(a.vy-b.vy)<.0001f,"Реплика/disk изменила физическое состояние мебели: "+a.id);
                if(a is ContainerState ca && b is ContainerState cb) Require(StateJson.ToJson(ca.items)==StateJson.ToJson(cb.items) && StateJson.ToJson(ca.itemAliases)==StateJson.ToJson(cb.itemAliases) && StateJson.ToJson(ca.appearance)==StateJson.ToJson(cb.appearance),"Реплика/disk изменила вещи/alias/рисунок мебели: "+a.id);
                Require(StateJson.ToJson(a.extensions)==StateJson.ToJson(b.extensions),"Потеря неизвестных полей мебели: "+a.id);
            }
        }
        private void ValidateDefinitions(WorldState world,bool initial)
        {
            foreach(var spec in selected.Continuation?specs.Concat(FurnitureActionSpec.ContinuationSources()):specs)
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
            if(selected.Continuation && !reload)Require(telekinesisRejection?.actual==telekinesisRejection?.expected && telekinesisRejection!=null && !Target.held,
                "Нет штатного actual TK отказа cup/wallcab.");
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
                // Flush is a shutdown barrier: it deliberately discards callbacks.
                // Continuation evidence needs this exact receipt's normal FIFO Pump
                // completion, not merely an archive that already exists on disk.
                if(!selected.Continuation)runtime.Session.FlushSaveWrites();
            });
            if(done) yield break;
            // Poll only the explicit final archive milestone, at a bounded cadence.
            // No profile writes, JSON parsing or content reads occur while waiting.
            while(!done && (!File.Exists(archive) || selected.Continuation && (!receipts.TryGetValue(finish.snapshot,out var receipt) || receipt.persistedProfileJson==null))) yield return new WaitForSecondsRealtime(.1f);
            if(!done) Guard(VerifyDurableArchive);
        }
        private void VerifyDurableArchive()
        {
            CompareResult(finish.result,selected.Continuation?finishReceivedResult:CaptureResult(runtime.Session.World));ValidateClaims(runtime.Session.World,finish.result.claims);replicaVerified=true;
            var canonical=finish.players.Single(p=>p.id==Actor.id);
            if(selected.Continuation)
            {
                var receipt=receipts[finish.snapshot];Require(receipt.world==fixture.world && receipt.snapshot==finish.snapshot && receipt.persistedProfileJson!=null,"Чужой frozen final receipt.");
                VerifyPlayerTransition(canonical,receipt.player,"canonical → receipt capture");
                ComparePlayer(receipt.player,StateJson.FromJson<ActorState>(receipt.persistedProfileJson),"frozen receipt → exact completed FIFO profile");
                verifiedPlayer=Actor.Copy();VerifyPlayerTransition(receipt.player,verifiedPlayer,"receipt capture → verification capture");
            }
            else ComparePlayer(canonical,Actor,"final replica");
            runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();
            var snapshot=new RecoverySnapshot(File.ReadAllText(archive));Require(snapshot.Id==finish.snapshot && snapshot.WorldId==fixture.world,"Собственный архив содержит другой immutable снимок.");
            var saved=snapshot.CreateLiveWorld();CompareResult(finish.result,CaptureResult(saved));ValidateClaims(saved,finish.result.claims);
            ComparePlayer(canonical,saved.Find(Actor.id),"immutable archive");
            ComparePlayer(selected.Continuation?verifiedPlayer:Actor,runtime.Session.Saves.LoadCharacter(),"own character disk / same synchronous capture");
            if(selected.Continuation){verificationProfileJson=runtime.Session.Saves.ReadCharacterSource();Require(verificationProfileJson==ExpectedProfile(verifiedPlayer),"Последний captured profile не записан точно после Flush.");}characterVerified=true;
            string before=StateJson.ToJson(saved);Require(!RoomContentInstaller.Apply(saved,RoomContentBatch.Read(RoomAuthoring.Load(Level,saved),Level)) && StateJson.ToJson(saved)==before,"Reinstall наполнил/воскресил или заменил сохранённые данные.");reinstallVerified=true;
            Require(frames.Count==(reload?1:selected.States.Length) && (reload || (selected.container?openings==2:landings>=1)),"Нет полного actual frame/audio evidence.");
            if(selected.Continuation && !reload)Require(telekinesisRejection!=null && telekinesisRejection.actual==telekinesisRejection.expected && qCommands>=2 && !frames.Single(f=>f.phase==5).held && landings==0,
                "Нет actual Notice/неподвижного предмета после обычного отказа TK.");
            if(selected.FixedRejection && !reload)Require(fixedRejectionVerified && qCommands>=2,"Нет реального обычного Q rejection закреплённого шкафчика.");
            telekinesisVerified=!selected.Continuation;
            motionVerified=reload || selected.container && !selected.Transport || moveDistance>.25f;claimsVerified=reload || !selected.container || finish.result.claims.Length==fixture.items.Length;
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
            if(done)return;
            if(success && selected.Continuation){terminalPlayer=Actor.Copy();VerifyPlayerTransition(verifiedPlayer,terminalPlayer,"verification capture → terminal capture");}
            done=true;DetachSubscriptions();
            var report=new Report {success=success,detail=reason+": "+Progress,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,phase=phase,target=selected.key,source=selected.Id,actor=Actor?.id,world=runtime.Session.World?.worldId,snapshot=finish?.snapshot,snapshotArchive=archive,saveDirectory=runtime.Options.SaveDirectory,characterDirectory=runtime.Session.Saves.CharacterDirectoryPath,worldDirectory=runtime.Session.Saves.WorldDirectoryPath,reload=reload,explicitInitialPositionFixture=!reload && fixture!=null,stockOverrides=false,originalRuntimeVerified=false,geometryVerified=geometryVerified,claimsVerified=claimsVerified,motionVerified=motionVerified,replicaVerified=replicaVerified,durableVerified=durable,characterVerified=characterVerified,reinstallVerified=reinstallVerified,cameraReady=runtime.LevelPresentationReady,telekinesisVerified=telekinesisVerified,fixedRejectionVerified=fixedRejectionVerified,qCommands=qCommands,fixedRejection=fixedRejection,captures=frames.Count,openings=openings,landings=landings,shots=shots,heals=heals,health=Actor?.health??0,x=Actor?.x??0,y=Actor?.y??0,movementDistance=moveDistance,approachDistance=approachDistance,liftDistance=liftDistance,fixture=fixture,finish=finish,frames=frames.ToArray(),voices=voices.ToArray(),cameraWaits=cameraWaits.ToArray()};
            if(selected.Continuation)
            {receipts.TryGetValue(finish?.snapshot??"",out report.receipt);report.finishReceivedPlayer=finishReceivedPlayer;report.verifiedPlayer=verifiedPlayer;report.terminalPlayer=terminalPlayer;report.verificationProfileJson=verificationProfileJson;report.playerTransitions=playerTransitions.ToArray();report.telekinesisRejection=telekinesisRejection;
                report.limitation="Cup29/wallcab44: отдельные initial позиции на настоящем нижнем полу4, затем короткий ordinary подход/E/whole-stack UI/Q. Cup movable60.16кг превышает текущий TK предел30кг: подтверждён видимый отказ, без подъёма/landing. Wallcab закреплённый NoBody. Не полный маршрут комнаты27. NPC/HP/stock/terrain/лимит TK не подменяются. Profile доказан frozen capture/exact completed FIFO receipt; поздний actual damage записан отдельно. Reload сохраняет strict finished → Disconnect guard, последующий непоказанный урон не игнорируется. Muted voice не доказывает слышимый mix/original runtime.";}
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));exitCode=success?0:1;Invoke(nameof(Exit),.75f);
        }
        private int exitCode;private void Exit()=>Application.Quit(exitCode);
        private void OnDestroy()=>DetachSubscriptions();
        private void DetachSubscriptions()
        {
            if(subscriptionsDetached || runtime?.Session==null)return;subscriptionsDetached=true;
            runtime.Session.ClientReady-=RegisterClient;runtime.Session.ServerReady-=RegisterServer;runtime.Session.Changed-=ObserveReservations;runtime.Session.ClaimPrepared-=OnClaim;runtime.Session.AttackReceived-=OnAttack;runtime.Session.Notice-=OnNotice;runtime.Session.SelfHealingRequested-=OnHeal;
            if(runtime.ContainerAudio!=null)runtime.ContainerAudio.Opened-=OnOpening;if(runtime.PropAudio!=null)runtime.PropAudio.LandingPlayed-=OnLanding;
            runtime.Session.RecoveryReceiptPrepared-=OnReceiptPrepared;runtime.Session.RecoveryReceiptPersisted-=OnReceiptPersisted;
            if(runtime.Combat!=null)runtime.Combat.DamageApplied-=OnOwnerDamage;
        }
    }
}
