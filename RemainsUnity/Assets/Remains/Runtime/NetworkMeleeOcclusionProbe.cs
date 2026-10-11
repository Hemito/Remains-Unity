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
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct MeleeOcclusionFixture : NetworkMessage {public string json;}
    public struct MeleeOcclusionPhase : NetworkMessage {public int phase;}
    public struct MeleeOcclusionWitness : NetworkMessage {public int phase,generation;public string actor,attack,detail;public bool valid;}
    public struct MeleeOcclusionFinish : NetworkMessage {public bool success;public string detail;}

    // Explicit initial positions and, for pair only, starting source HP. Incoming damage and knockdown remain ordinary.
    // The authored door, props, terrain, target health and hostile NPCs remain unchanged.
    // Solo moves the existing hub metal dummy; pair uses the other player's own damage receiver.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkMeleeOcclusionProbe : MonoBehaviour
    {
        // Source warehouse40 record27 is installed as office-door1/HP60. Office53's foMR door remains legacy.
        private const string Level="factory:0",DoorId=Level+":original:plant:40:tvFoyf3FQMrgHqCu";
        private const string DummyId="training:original:WEqCysOP6BwxoT0g";
        private const float PairSourceStartingHealth=1000;
        [Serializable] private sealed class Participant
        {public string id,owner,inventory;public int generation,lifecycle;public Vector2 position;}
        [Serializable] private sealed class Fixture
        {
            public string world,source,target,sourceHealthGuardReason;public DoorState door;public ActorState sourceBefore,targetBefore;
            public Participant[] players;public int untouchedHostileNpcs,attackDirection;
            public bool hostileNpcsUnmodified,sourceHealthGuard,sourceMaximumDefined;
            public float originalHealth,preparedHealth;public float? originalMaxHealth,preparedMaxHealth;
        }
        [Serializable] private sealed class Sample
        {public int phase,ordinal;public float receivedAt,sourceRemaining;public AttackEvent attack;}
        [Serializable] private sealed class Voice
        {public int id,phase;public string clip;public float volume;public bool muted,playing;}
        [Serializable] private sealed class Frame
        {public int phase,width,height;public string label;public Vector3 cameraPosition;public float cameraSize;public Rect doorBounds;}
        [Serializable] private sealed class SwingTiming
        {
            public int phase,sourceFrameCount,firstFrame,lastFrame;
            public float startedAt,endedAt,elapsed,unscaledElapsed,deltaTimeMin,deltaTimeMax,unscaledDeltaTimeMin,unscaledDeltaTimeMax;
        }
        [Serializable] private sealed class ZeroDamageContact
        {
            public float observedAt,incomingDamage,resolvedDamage;public int phase,projectileGeneration,targetGeneration;
            public string target,targetType,projectileOwner;public Vector2 position;public AttackEvent attack,launch;
        }
        [Serializable] private sealed class Report
        {
            public bool success,explicitInitialFixture,soloExistingDummyMoved,hostileNpcsUnmodified,closedBlocked,openHit,
                damagedDoorOnce,dedup,anchorSamplesValid,ownerDecision,mutedDeviceOutput;
            public string detail,context,observer,world,door,source,target,closedAttack,openAttack,targetOwner;
            public int closedSamples,lateClosedSamples,openSamples,capturedFrames,ownerDamageEvents,doorDamageReplies,acceptedInteractions;
            public int targetDamageBefore,targetDamageAfter,targetReceiptsBefore,targetReceiptsAfter;
            public int scopedOpenReceipts,scopedClosedReceipts,rawTargetDamageSequence;
            public float doorHealthBefore,doorHealthAfter,targetHealthBefore,targetHealthAfter,targetRestoreHealthAfterHit,rawTargetHealth;
            public float sourceHealthAfter;public bool sourceDown;public int sourceGeneration,sourceLifecycle,sourceDamageBefore,sourceDamageAfter,sourceOwnerDamageEvents;
            public string[] sourceReceipts;public DamageResult[] sourceDamage;
            public string[] scopedTargetReceipts,rawTargetReceipts;public ZeroDamageContact[] zeroDamageContacts;
            public DamageResult[] rawDamage;
            public long doorRevision;public Fixture initial;public Sample[] samples;public Voice[] voices;public Frame[] frames;public DamageResult[] damage;public SwingTiming[] sourceSwingTiming;
        }
        private PrototypeRuntime runtime;private Fixture fixture;private Participant participant;private RoomLayout room;
        private int phase,serverPhase,closedSamples,lateClosedSamples,openSamples,surfaceBaseline,actorBaseline,doorDamageReplies,acceptedInteractions;
        private bool traveled,installed,active,finished,reported,fired,openRequested,closedBlocked,openHit,damagedDoorOnce,dedup,ownerDecision;
        private float deadline,phaseAt,lastSampleAt=-1;private string closedAttack,openAttack,capture;
        private readonly List<Sample> samples=new();private readonly List<Voice> voices=new();private readonly List<Frame> frames=new();
        private readonly List<DamageResult> ownerDamage=new();private readonly HashSet<int> knownVoices=new();
        private readonly List<DamageResult> sourceDamage=new();
        private readonly List<SwingTiming> swingTiming=new();private SwingTiming timing;
        private readonly Dictionary<int,HashSet<string>> witnesses=new();
        private readonly HashSet<string> surfaceReceipts=new(StringComparer.Ordinal),targetReceipts=new(StringComparer.Ordinal);
        private readonly Dictionary<string,ZeroDamageContact> zeroDamageContacts=new(StringComparer.Ordinal);
        private AttackEvent firstDoorContact;
        private ActorState observedTarget,canonicalTargetProof;private DamageResult[] observedDamage;
        private bool Host=>runtime.Options.Role=="host";
        private bool Solo=>runtime.Options.ProbePeers==1;
        private bool Source=>fixture!=null && runtime.Session.LocalActorId==fixture.source;
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Target=>fixture==null?null:runtime.Session.World?.Find(fixture.target);
        private DoorState Door=>runtime.Session.World?.doors.SingleOrDefault(d=>d.id==DoorId);
        private float NearFaceX=>fixture.door.x+(fixture.attackDirection<0?fixture.door.width:0);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+100;AudioListener.volume=0;
            if(value.Options.ProbePeers is <1 or >2 || string.IsNullOrEmpty(value.Options.SaveDirectory) ||
                string.IsNullOrEmpty(value.Options.Report) || !value.Options.Smoke.Contains("-frames-"))
                throw new InvalidOperationException("Melee occlusion требует 1–2 участников, -frames-, отдельные сохранения и отчёт.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Melee occlusion не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            value.CommandSource=Command;
            value.Session.AttackReceived+=Attack;
            value.Combat.SurfaceContactObserved+=Surface;
            value.Combat.ContactObserved+=Contact;
            value.Combat.DamageApplied+=Damage;
            value.Session.DoorDamage.Finished+=DoorReply;
            value.Session.Doors.Finished+=DoorInteraction;
            value.Session.Notice+=Notice;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<MeleeOcclusionFixture>(message=>
                {
                    fixture=StateJson.FromJson<Fixture>(message.json);
                    Require(fixture.attackDirection is 1 or -1,"Не указана сторона actual melee fixture.");
                    participant=fixture.players.Single(p=>p.id==value.Session.LocalActorId);Begin(1);
                });
                NetworkClient.RegisterHandler<MeleeOcclusionPhase>(message=>Begin(message.phase));
                NetworkClient.RegisterHandler<MeleeOcclusionFinish>(message=>Finish(message.success,message.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<MeleeOcclusionWitness>((connection,message)=>
            {
                try
                {
                    var expected=fixture?.players.SingleOrDefault(p=>p.id==message.actor);
                    var actual=value.Session.ConnectedActor(connection);
                    Require(message.valid && message.phase is >=1 and <=4 && expected!=null && actual?.id==expected.id &&
                        actual.generation==message.generation && actual.generation==expected.generation && actual.level==Level,
                        "Неверный observer/barrier: "+message.detail);
                    if(message.phase==2) Require(message.attack==closedAttack,"Разошлись ID закрытого удара.");
                    if(message.phase==4) Require(message.attack==openAttack,"Разошлись ID открытого удара.");
                    if(message.phase<serverPhase && witnesses.TryGetValue(message.phase,out var previous) && previous.Contains(message.actor)) return;
                    Require(message.phase==serverPhase,"Свидетельство относится к неверной текущей фазе.");
                    if(!witnesses.TryGetValue(message.phase,out var set)) witnesses.Add(message.phase,set=new(StringComparer.Ordinal));
                    set.Add(message.actor);if(set.Count!=value.Options.ProbePeers) return;
                    if(message.phase==4)
                    {
                        var target=canonicalTargetProof;var door=value.Session.ServerWorld.doors.Single(d=>d.id==DoorId);
                        Require(door.open && door.revision==fixture.door.revision+2 && door.integrity.resolvedAttacks.SequenceEqual(new[]{closedAttack}) &&
                            MatchesTargetReceipts(target,fixture.targetBefore,closedAttack,openAttack,zeroDamageContacts.Keys,out _),
                            "Canonical мир не подтвердил единственное попадание на момент завершённого наблюдения после открытия.");
                        NetworkServer.SendToAll(new MeleeOcclusionFinish {success=true,detail="Обычное копьё: поздние сегменты остановлены исходной закрытой дверью, один урон двери; E открыл её, второй удар принят единственным владельцем цели ровно один раз."});
                    }
                    else {serverPhase=message.phase+1;NetworkServer.SendToAll(new MeleeOcclusionPhase {phase=serverPhase});}
                }
                catch(Exception error) {Fail(error.Message);}
            });
        }
        private void Begin(int next)
        {
            Require(fixture!=null && next==phase+1,"Нарушен порядок фаз melee occlusion: "+next);
            phase=next;phaseAt=Time.unscaledTime;lastSampleAt=-1;reported=fired=false;
            if(next==1) capture="closed";
            if(next==3) openRequested=false;
            surfaceBaseline=runtime.MeleeAudio.SurfacePlayed;actorBaseline=runtime.MeleeAudio.ActorPlayed;
            timing=null;
            foreach(var source in runtime.Audio.GetComponentsInChildren<AudioSource>()) knownVoices.Add(source.GetInstanceID());
        }
        private PlayerCommand Command()
        {
            if(finished || !active || fixture==null || !Source || Actor==null) return default;
            SampleSwingTiming();
            try
            {
            var command=new PlayerCommand {weapon="spear",hasAim=true,aim=Target.Position+Vector2.up*.8f};
            if(phase==3)
            {
                command.aim=Door.InteractionBounds.center;
                if(!openRequested)
                {
                    var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,command.aim);
                    Require(selected.kind==InteractionKind.Door && selected.id==DoorId,$"Cursor E выбрал {selected.kind}/{selected.id}, ожидалась исходная дверь.");
                    openRequested=true;command.interact=true;
                }
            }
            else if(phase is 2 or 4 && !fired && Actor.weapon=="spear" && !runtime.Combat.IsMeleeActive && runtime.Combat.ShotCooldownRemaining<=0)
            {fired=true;command.fire=true;}
            return command;
            }
            catch(Exception error) {Fail(error.Message);return default;}
        }
        private void SampleSwingTiming()
        {
            if(phase is not (2 or 4)) return;
            if(!runtime.Combat.IsMeleeActive)
            {if(timing!=null && timing.endedAt==0) timing.endedAt=Time.unscaledTime;return;}
            if(timing==null)
            {
                timing=new SwingTiming {phase=phase,firstFrame=Time.frameCount,startedAt=Time.unscaledTime,
                    deltaTimeMin=float.MaxValue,unscaledDeltaTimeMin=float.MaxValue};swingTiming.Add(timing);
            }
            if(timing.sourceFrameCount>0 && timing.lastFrame==Time.frameCount) return;
            timing.sourceFrameCount++;timing.lastFrame=Time.frameCount;
            timing.elapsed+=Time.deltaTime;timing.unscaledElapsed+=Time.unscaledDeltaTime;
            timing.deltaTimeMin=Mathf.Min(timing.deltaTimeMin,Time.deltaTime);timing.deltaTimeMax=Mathf.Max(timing.deltaTimeMax,Time.deltaTime);
            timing.unscaledDeltaTimeMin=Mathf.Min(timing.unscaledDeltaTimeMin,Time.unscaledDeltaTime);timing.unscaledDeltaTimeMax=Mathf.Max(timing.unscaledDeltaTimeMax,Time.unscaledDeltaTime);
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                Require(Time.unscaledTime<=deadline,"Таймаут melee occlusion.");
                var session=runtime.Session;var actor=Actor;
                if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(!traveled) {traveled=true;session.Travel.RequestFactory();}
                if(Host && !installed && !session.Travel.Pending && session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level) &&
                    session.World.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level)) Setup();
                if(fixture==null || participant==null) return;
                if(!active && (actor.generation!=participant.generation || Target?.generation!=fixture.targetBefore.generation)) return;
                if(!active) {active=true;room=runtime.Geometry.Get(Level);phaseAt=Time.unscaledTime;}
                Require(session.World.worldId==fixture.world && actor.generation==participant.generation && actor.lifecycleSequence==participant.lifecycle &&
                    actor.owner==participant.owner && actor.level==Level && !actor.down && actor.health>0 && !session.Pause.IsPaused(Level),"Изменился мир/владелец/управление наблюдателя.");
                Require(StateJson.ToJson(actor.inventory)==participant.inventory,"Проверка копья изменила исходный инвентарь наблюдателя.");
                Require(Target!=null && Target.generation==fixture.targetBefore.generation && Target.owner==fixture.targetBefore.owner && Target.level==Level,
                    "Цель потеряла fixture identity/owner/level.");
                Require(Vector2.Distance(actor.Position,participant.position)<.05f && Vector2.Distance(Target.Position,fixture.targetBefore.Position)<.05f,
                    "Обычная симуляция сдвинула тестового игрока/цель; proof не подменяется новым placement.");
                Require(room.platforms.Any(s=>s.sourceDoorId==DoorId)==Door.Blocks,"Derived geometry не совпала с текущей дверью.");
                // A witness fixes the completed quiet observation; later unrelated world activity is raw diagnostics.
                if(reported) return;
                if(phase is 1 or 2 or 3)
                    Require(Target.damageSequence==fixture.targetBefore.damageSequence && Target.health==fixture.targetBefore.health &&
                        Target.resolvedAttacks.SequenceEqual(fixture.targetBefore.resolvedAttacks),"Закрытая дверь не защитила цель / произошло стороннее повреждение.");
                if(phase==1)
                {
                    Require(StateJson.ToJson(Door)==StateJson.ToJson(fixture.door),"Исходная дверь изменилась до fire.");
                    if(Time.unscaledTime-phaseAt>2 && SourcePrepared() && InCamera()) RequestCapture("closed");
                    if(HasFrame("closed") && SourcePrepared()) Observe();
                }
                else if(phase==2 && closedSamples>=3 && lateClosedSamples>=2 && lastSampleAt>=0 && Time.unscaledTime-lastSampleAt>.8f && !runtime.Combat.IsMeleeActive)
                {
                    Require(firstDoorContact!=null && !Door.open && !Door.Destroyed && Door.revision==fixture.door.revision+1 &&
                        Door.integrity.resolvedAttacks.SequenceEqual(new[]{closedAttack}),"Дверь не сохранила единственный ID/revision закрытого удара.");
                    float expected=runtime.Session.Rules.DoorDamage.Damage(fixture.door,firstDoorContact,WeaponCatalog.Get("spear"));
                    Require(expected==6 && Door.integrity.health==fixture.door.integrity.health-expected,"Урон двери не равен явным 6 единицам текущего content rule.");
                    Require(runtime.MeleeAudio.SurfacePlayed-surfaceBaseline==1 && runtime.MeleeAudio.ActorPlayed==actorBaseline &&
                        surfaceReceipts.Count==1 && targetReceipts.Count==0,"Закрытый удар потерял/повторил аудиоконтакт либо озвучил цель за дверью.");
                    Require(!Source || doorDamageReplies==1,"Source не получил один durable ответ урона двери.");
                    closedBlocked=damagedDoorOnce=true;RequestCapture("blocked");if(HasFrame("blocked")) Observe();
                }
                else if(phase==3 && Door.open)
                {
                    Require(Door.revision==fixture.door.revision+2 && Door.integrity.health==fixture.door.integrity.health-6 &&
                        Door.integrity.resolvedAttacks.SequenceEqual(new[]{closedAttack}),"Обычное E изменило целостность или повторило открытие.");
                    RequestCapture("open");if(HasFrame("open") && SourcePrepared() && (!Source || acceptedInteractions==1)) Observe();
                }
                else if(phase==4 && openSamples>=3 && lastSampleAt>=0 && Time.unscaledTime-lastSampleAt>.8f && !runtime.Combat.IsMeleeActive)
                {
                    if(Target.damageSequence==fixture.targetBefore.damageSequence) return;
                    Require(MatchesTargetReceipts(Target,fixture.targetBefore,closedAttack,openAttack,zeroDamageContacts.Keys,out string receiptReason),receiptReason);
                    float restore=ActorTypes.Get(Target.actorType).restoreHealthAfterHit;
                    Require(restore>0?Target.health==restore:Target.health<fixture.targetBefore.health,"Нет действительного результата урона/политики мишени.");
                    bool owner=Target.owner==session.LocalOwner;
                    Require(owner?ownerDamage.Count==1 && ownerDamage[0].attack==openAttack && ownerDamage[0].amount>0:ownerDamage.Count==0,
                        "Урон цели решил другой наблюдатель / owner получил повторный DamageApplied.");
                    Require(runtime.MeleeAudio.ActorPlayed-actorBaseline==1 && runtime.MeleeAudio.SurfacePlayed==surfaceBaseline && targetReceipts.Count==1,
                        "Открытый удар потерял/повторил реальный голос цели.");
                    Require(Door.revision==fixture.door.revision+2 && Door.integrity.health==fixture.door.integrity.health-6 &&
                        Door.integrity.resolvedAttacks.SequenceEqual(new[]{closedAttack}),"Второй удар повредил открытую дверь.");
                    ownerDecision=dedup=openHit=true;RequestCapture("hit");if(HasFrame("hit")) Observe();
                }
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private bool SourcePrepared()
        {
            var source=runtime.Session.World.Find(fixture.source);
            return source?.weapon=="spear" && !source.down && (!Source || !runtime.Combat.IsMeleeActive && runtime.Combat.ShotCooldownRemaining<=0);
        }
        private void Setup()
        {
            var session=runtime.Session;var world=session.ServerWorld;var door=world.doors.Single(d=>d.id==DoorId);door.Validate();
            Require(WorldRooms.Definition(world,Level)==LegacyFactoryFixture.DefinitionId && door.definition=="office-door1" && !door.open &&
                !door.Destroyed && door.integrity is {health:60,maxHealth:60} && door.revision==0 && door.integrity.resolvedAttacks.Count==0,
                "Нужен настоящий новый CLOSED office-door1, без подмены состояния.");
            Require(world.friendlyFire && world.friendlyFireDecision==FriendlyFireDecision.Victim,"Pair proof требует штатный friendly fire/victim policy.");
            var players=world.actors.Where(a=>a.player && a.connected).OrderBy(a=>a.id==session.LocalActorId?0:1).ToArray();
            var source=players.Single(a=>a.id==session.LocalActorId);
            var target=Solo?world.Find(DummyId):players.Single(a=>a.id!=source.id);
            Require(target!=null && (!Solo || target.actorType=="training-armored-dummy" && !target.player && !ActorTypes.Get(target.actorType).combatAi),"Нет существующей металлической мишени / второго игрока.");
            Require(source.inventory.Count(i=>i.definition=="spear" && i.count==1 && i.weaponState!=null)==1,"Нет настоящего стартового копья; inventory не пополняется.");
            var geometry=runtime.Geometry.Get(Level);
            var hostile=world.actors.Where(a=>!a.player && ActorTypes.Get(a.actorType).combatAi).ToArray();
            var hostileBefore=hostile.Select(a=>StateJson.ToJson(a)).ToArray();
            // Pair's victim stands behind the real closed door relative to the unchanged warehouse raiders.
            // The source stays between those raiders and the victim after E; no NPC or projectile is disabled.
            int direction=Solo?1:-1;float nearFace=door.x+(direction<0?door.width:0);
            var sourcePosition=new Vector2(nearFace-direction*.51f,door.y);
            var targetPosition=new Vector2(nearFace+direction*1.4f,door.y);
            RequirePlacement(world,geometry,source,sourcePosition);RequirePlacement(world,geometry,target,targetPosition);
            var originalSource=StateJson.Copy(source);var originalTarget=StateJson.Copy(target);
            // Pair mirror shields the victim, but native build4 exposed ten real raider hits on the source.
            // This isolates the short sweep measurement from source KO; it is not a normal-route survival test.
            if(!Solo) source.health=PairSourceStartingHealth;
            Require(ValidateInitialSourceHealth(originalSource,source,originalTarget,target,runtime.Options.ProbePeers,out string healthReason),healthReason);
            foreach(var placement in new[]{(actor:source,position:sourcePosition),(actor:target,position:targetPosition)})
            {
                placement.actor.level=Level;placement.actor.Position=placement.position;placement.actor.Velocity=Vector2.zero;
                placement.actor.climbing=false;placement.actor.crouched=false;placement.actor.pose="idle";placement.actor.poseTime=0;
                placement.actor.generation++;placement.actor.revision++;
            }
            for(int i=0;i<hostile.Length;i++) Require(StateJson.ToJson(hostile[i])==hostileBefore[i],"Initial fixture изменила настоящего hostile NPC: "+hostile[i].id);
            var before=StateJson.Copy(door);int hostileCount=hostile.Length;
            installed=true;session.CommitWorld();
            fixture=new Fixture {world=world.worldId,source=source.id,target=target.id,door=before,sourceBefore=StateJson.Copy(source),targetBefore=StateJson.Copy(target),untouchedHostileNpcs=hostileCount,hostileNpcsUnmodified=true,attackDirection=direction,
                originalHealth=originalSource.health,preparedHealth=source.health,sourceHealthGuard=!Solo,sourceMaximumDefined=false,
                originalMaxHealth=null,preparedMaxHealth=null,
                sourceHealthGuardReason=Solo?"Solo: начальное здоровье не изменено.":"Только pair: начальные HP атакующего 100→1000 для измерения двух ударов у настоящей двери. Normal100 mirror завершился KO от десяти настоящих попаданий; входящий урон и KO не отключены. ActorState не содержит максимума здоровья; он не придуман этой fixture.",
                players=players.Select(a=>new Participant {id=a.id,owner=a.owner,generation=a.generation,lifecycle=a.lifecycleSequence,
                    position=a.Position,inventory=StateJson.ToJson(a.inventory)}).ToArray()};
            serverPhase=1;NetworkServer.SendToAll(new MeleeOcclusionFixture {json=StateJson.ToJson(fixture)});
            Debug.Log($"Melee fixture: door={DoorId} bounds={door.Bounds}, source={source.id}@{source.Position} HP={originalSource.health}→{source.health}, target={target.id}@{target.Position} HP={target.health}, direction={direction}, nearFace={nearFace}, soloExistingDummy={Solo}, hostileNPC={hostileCount} без изменений; после setup только обычные fire/E. {fixture.sourceHealthGuardReason}");
        }
        // Probe-only initial preparation. Exact normalized equality prevents hidden inventory/control/target alterations.
        internal static bool ValidateInitialSourceHealth(ActorState sourceBefore,ActorState sourcePrepared,ActorState targetBefore,
            ActorState targetPrepared,int peers,out string reason)
        {
            reason="Некорректный initial source HP guard: разрешено лишь 100→1000 для атакующего в pair; цель и прочие поля не меняются.";
            if(peers is <1 or >2 || sourceBefore==null || sourcePrepared==null || targetBefore==null || targetPrepared==null ||
                sourceBefore.id==targetBefore.id || !sourceBefore.player || !sourceBefore.connected || sourceBefore.down ||
                !float.IsFinite(sourceBefore.health) || sourceBefore.health<=0 ||
                StateJson.ToJson(targetBefore)!=StateJson.ToJson(targetPrepared)) return false;
            float expected=peers==2?PairSourceStartingHealth:sourceBefore.health;
            if(sourcePrepared.health!=expected || (peers==2 && (sourceBefore.health!=100 || !targetBefore.player ||
                !targetBefore.connected || targetBefore.down || targetBefore.health!=100))) return false;
            var normalized=StateJson.Copy(sourcePrepared);normalized.health=sourceBefore.health;
            return StateJson.ToJson(normalized)==StateJson.ToJson(sourceBefore);
        }
        private static void RequirePlacement(WorldState world,RoomLayout geometry,ActorState actor,Vector2 position)
        {
            var hull=Anatomy.Movement(actor);var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
            Require(ActorTerrainMotion.IsClear(geometry,hull,position) && ActorTerrainMotion.Supported(geometry,hull,position),
                $"Fixture без реальной свободной опоры: actor={actor.id}, feet={position}, hull={body}.");
            foreach(var prop in WorldProps.All(world))
                if(prop.level==Level && prop.hasPhysicalBody && body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)))
                    throw new InvalidOperationException($"Fixture пересекает настоящее физическое содержимое: actor={actor.id}, prop={prop.id}/{prop.definition}, feet={position}.");
        }
        private void Attack(AttackEvent attack)
        {
            if(finished || fixture==null || attack.attacker!=fixture.source || attack.weapon!="spear") return;
            try
            {
                Require(phase is 2 or 4 && attack.level==Level && attack.sourceOwner==fixture.players.Single(p=>p.id==fixture.source).owner &&
                    MeleeTerrainOcclusion.Validate(attack,out _),"Неожиданный melee event/неfinite sampled anchors.");
                Require(attack.friendlyFireDecision==FriendlyFireDecision.Victim,"Обычный удар не сохранил victim policy.");
                if(Source) SampleSwingTiming();
                string existing=phase==2?closedAttack:openAttack;
                Require(existing==null || existing==attack.id,"Более одного fire/attackID в фазе.");
                if(phase==2)
                {
                    closedAttack=attack.id;closedSamples++;
                    Require((attack.Origin.x-NearFaceX)*fixture.attackDirection<=.00001f &&
                        (attack.End.x-NearFaceX)*fixture.attackDirection<=.00001f,
                        "Поздний кончик копья прошёл на дальнюю сторону закрытой двери.");
                    if(Vector2.Distance(attack.Origin,attack.End)<.00005f &&
                        Mathf.Abs(attack.End.x-(NearFaceX-fixture.attackDirection*attack.radius))<.0001f) lateClosedSamples++;
                }
                else {openAttack=attack.id;openSamples++;}
                lastSampleAt=Time.unscaledTime;
                samples.Add(new Sample {phase=phase,ordinal=phase==2?closedSamples:openSamples,receivedAt=lastSampleAt,
                    sourceRemaining=Source?runtime.Combat.MeleeRemaining:-1,attack=StateJson.Copy(attack)});
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void Surface(AttackEvent attack,string texture,Vector2 position)
        {
            if(finished || fixture==null || attack.attacker!=fixture.source || attack.weapon!="spear") return;
            try
            {
                Require(phase==2 && texture==fixture.door.surfaceTexture && Mathf.Abs(position.x-NearFaceX)<.001f,
                    "Контакт копья принадлежит другой поверхности / открытому проёму.");
                firstDoorContact??=StateJson.Copy(attack);
                if(surfaceReceipts.Add(attack.id)) CaptureVoices("hit-wood");
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void Contact(AttackEvent attack,ActorState target)
        {
            if(finished || fixture==null) return;
            if(attack.attacker!=fixture.source || attack.weapon!="spear")
            {
                if(target.id==fixture.target && !reported) ObserveZeroDamageContact(attack,target);
                return;
            }
            try
            {
                Require(phase==4 && target.id==fixture.target,"Копьё коснулось цели за закрытой дверью / постороннего actor.");
                if(targetReceipts.Add(attack.id)) CaptureVoices(Solo?"hit-metal":"hit-blade");
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void ObserveZeroDamageContact(AttackEvent attack,ActorState target)
        {
            try
            {
                // A receipt is acceptance, not necessarily positive damage. Only an actual matched NPC flight may be excluded.
                var flight=runtime.Combat.Projectiles.Flights.SingleOrDefault(f=>f.state.launch.id==attack.id);
                var attacker=runtime.Session.World.Find(attack.attacker);
                Require(phase==4 && Solo && target.id==fixture.target && !target.player && attack.level==Level &&
                    attack.attacker!=fixture.source && !attack.sourcePlayer && attacker!=null && !attacker.player &&
                    attacker.level==Level && ActorTypes.Get(attacker.actorType).combatAi && flight!=null &&
                    flight.state.generation>0 && flight.state.owner!=null && flight.state.launch.attacker==attack.attacker &&
                    flight.state.launch.level==attack.level && flight.state.launch.weapon==attack.weapon && !flight.state.launch.sourcePlayer &&
                    StateJson.ToJson(flight.segment)==StateJson.ToJson(attack),
                    "Посторонний receipt не подтверждён настоящим NPC projectile contact: "+attack.id);
                float incoming=runtime.Session.Rules.Damage.Calculate(attack.weapon,attack.speed);
                float resolved=new TypeHitProtectionRule().Resolve(target,attack,incoming);
                Require(float.IsFinite(incoming) && incoming>=0 && resolved==0,
                    $"Посторонняя атака не поглощена полностью: attack={attack.id}, incoming={incoming}, resolved={resolved}.");
                if(!zeroDamageContacts.ContainsKey(attack.id)) zeroDamageContacts.Add(attack.id,new ZeroDamageContact {
                    observedAt=Time.unscaledTime,phase=phase,target=target.id,targetType=target.actorType,targetGeneration=target.generation,
                    projectileOwner=flight.state.owner,projectileGeneration=flight.state.generation,position=target.Position,
                    attack=StateJson.Copy(attack),launch=StateJson.Copy(flight.state.launch),incomingDamage=incoming,resolvedDamage=resolved});
            }
            catch(Exception error) {Fail(error.Message);}
        }
        // Probe evidence contract, not a gameplay damage rule. Unknown/duplicate/closed receipts and any second damage fail.
        internal static bool MatchesTargetReceipts(ActorState actual,ActorState before,string closed,string open,
            IReadOnlyCollection<string> observedZeroDamage,out string reason)
        {
            reason="Открытый удар не принят ровно один раз.";
            if(actual?.resolvedAttacks==null || before?.resolvedAttacks==null || string.IsNullOrEmpty(open) ||
                string.IsNullOrEmpty(closed) || open==closed || observedZeroDamage==null ||
                actual.id!=before.id || actual.level!=before.level || actual.owner!=before.owner ||
                actual.generation!=before.generation || actual.lifecycleSequence!=before.lifecycleSequence ||
                before.resolvedAttacks.Contains(open) || before.resolvedAttacks.Contains(closed) ||
                actual.damageSequence!=before.damageSequence+1 || actual.resolvedAttacks.Count<before.resolvedAttacks.Count+1) return false;
            for(int i=0;i<before.resolvedAttacks.Count;i++)
                if(actual.resolvedAttacks[i]!=before.resolvedAttacks[i]) return false;
            int openCount=0;
            for(int i=0;i<actual.resolvedAttacks.Count;i++)
            {
                string id=actual.resolvedAttacks[i];
                if(id==closed) {reason="Цель сохранила ID закрытого удара: "+closed;return false;}
                if(id==open) openCount++;
                for(int earlier=0;earlier<i;earlier++)
                    if(actual.resolvedAttacks[earlier]==id) {reason="Повторный receipt цели: "+id;return false;}
                if(i>=before.resolvedAttacks.Count && id!=open && !observedZeroDamage.Contains(id))
                {reason="Неподтверждённый дополнительный receipt цели: "+id;return false;}
            }
            return openCount==1;
        }
        private void CaptureVoices(string clip)
        {
            var added=runtime.Audio.GetComponentsInChildren<AudioSource>().Where(s=>knownVoices.Add(s.GetInstanceID())).ToArray();
            Require(added.Count(s=>s.clip?.name==clip && s.volume>0)==1,"Контакт не создал один настоящий голос "+clip);
            foreach(var source in added) voices.Add(new Voice {phase=phase,id=source.GetInstanceID(),clip=source.clip?.name,volume=source.volume,muted=source.mute,playing=source.isPlaying});
        }
        private void Damage(DamageResult result)
        {
            if(fixture!=null && result.actor==fixture.source) {sourceDamage.Add(result);return;}
            if(fixture==null || result.actor!=fixture.target) return;
            if(phase!=4) {Fail("Цель получила DamageApplied до обычного открытия двери.");return;}
            ownerDamage.Add(result);
        }
        private void DoorReply(DoorDamageReply reply)
        {
            if(fixture==null || reply.door!=DoorId) return;
            doorDamageReplies++;
            if(!Source || phase!=2 || !reply.accepted || !reply.changed || reply.revision!=fixture.door.revision+1 || reply.health!=54 || reply.destroyed)
                Fail("Неверный первый durable ответ урона исходной двери: "+reply.reason);
        }
        private void DoorInteraction(DoorReply reply)
        {
            if(finished || fixture==null) return;
            if(!Source || phase!=3 || !openRequested || !reply.accepted || ++acceptedInteractions!=1)
                Fail("Обычное E не получило единственное подтверждение открытия: "+reply.reason);
        }
        private bool InCamera()
        {
            var camera=Camera.main;if(camera==null) return false;
            foreach(var point in new[]{Door.Bounds.min,Door.Bounds.max,Actor.Position,Target.Position+Vector2.up*Anatomy.Movement(Target).height})
            {var v=camera.WorldToViewportPoint(point);if(v.z<=0 || v.x<0 || v.x>1 || v.y<0 || v.y>1) return false;}
            return true;
        }
        private bool HasFrame(string label)=>frames.Any(f=>f.label==label);
        private void RequestCapture(string label) {if(!HasFrame(label)) capture=label;}
        private void LateUpdate()
        {
            if(finished || !active || capture==null || !InCamera()) return;
            try
            {
                if(capture=="closed" && (Time.unscaledTime-phaseAt<2 || !SourcePrepared())) return;
                string label=capture;capture=null;
                NetworkSceneRecord.Save(runtime,phase);
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".melee-"+label+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                frames.Add(new Frame {phase=phase,label=label,width=1920,height=1080,cameraPosition=Camera.main.transform.position,cameraSize=Camera.main.orthographicSize,doorBounds=Door.Bounds});
                File.WriteAllText(runtime.Options.Report+".melee-"+label+".evidence.json",StateJson.ToJson(BuildReport(false,"Наблюдение фазы"),true));
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void Observe()
        {
            if(reported) return;
            if(phase==4)
            {
                if(Host)
                {
                    var canonical=runtime.Session.ServerWorld.Find(fixture.target);
                    if(canonical.damageSequence==fixture.targetBefore.damageSequence) return;
                    Require(MatchesTargetReceipts(canonical,fixture.targetBefore,closedAttack,openAttack,zeroDamageContacts.Keys,out string reason),reason);
                    canonicalTargetProof=StateJson.Copy(canonical);
                }
                observedTarget=StateJson.Copy(Target);observedDamage=ownerDamage.ToArray();
            }
            reported=true;
            NetworkClient.Send(new MeleeOcclusionWitness {phase=phase,actor=Actor.id,generation=Actor.generation,attack=phase==2?closedAttack:phase==4?openAttack:null,valid=true,detail=Context()});
        }
        private string Context()=>$"phase={phase}/{serverPhase}, world={runtime.Session.World?.worldId}, player={Actor?.id}@{Actor?.Position} HP={Actor?.health} down={Actor?.down} gen={Actor?.generation} life={Actor?.lifecycleSequence}, sourceHP={runtime.Session.World?.Find(fixture?.source)?.health} down={runtime.Session.World?.Find(fixture?.source)?.down} gen={runtime.Session.World?.Find(fixture?.source)?.generation} damageSeq={runtime.Session.World?.Find(fixture?.source)?.damageSequence}, target={Target?.id}@{Target?.Position} HP={Target?.health} down={Target?.down} gen={Target?.generation} owner={Target?.owner}, doorHP={Door?.integrity?.health}, doorRev={Door?.revision}, open={Door?.open}, samples={closedSamples}/{lateClosedSamples}/{openSamples}, IDs={closedAttack}/{openAttack}, ownerDamage={ownerDamage.Count}, ready={reported}, capture={capture}, frames={frames.Count}, deadline={deadline}";
        private void Notice(string text)
        {
            // SessionDoors emits its success notice before Finished. It is not a command rejection.
            if(Source && phase==3 && openRequested && text=="Дверь открыта.") return;
            if(active && !finished) Fail("Обычная команда отклонена: "+text);
        }
        private void Fail(string reason)
        {
            if(finished) return;
            string detail=reason+" "+Context();
            if(Host && NetworkServer.active) NetworkServer.SendToAll(new MeleeOcclusionFinish {success=false,detail=detail});
            else if(NetworkClient.isConnected) NetworkClient.Send(new MeleeOcclusionWitness {phase=phase,actor=Actor?.id,generation=Actor?.generation??-1,valid=false,detail=detail});
            Finish(false,detail);
        }
        private Report BuildReport(bool success,string detail)=>new()
        {
            success=success,detail=detail,context=Context(),observer=runtime.Session.LocalActorId,world=fixture?.world,door=DoorId,source=fixture?.source,target=fixture?.target,
            explicitInitialFixture=installed || active,soloExistingDummyMoved=Solo && fixture!=null,hostileNpcsUnmodified=fixture?.hostileNpcsUnmodified==true,mutedDeviceOutput=AudioListener.volume==0,
            closedBlocked=closedBlocked,openHit=openHit,damagedDoorOnce=damagedDoorOnce,dedup=dedup,ownerDecision=ownerDecision,
            anchorSamplesValid=samples.Count>0 && samples.All(s=>MeleeTerrainOcclusion.Validate(s.attack,out _)),closedAttack=closedAttack,openAttack=openAttack,
            closedSamples=closedSamples,lateClosedSamples=lateClosedSamples,openSamples=openSamples,capturedFrames=frames.Count,ownerDamageEvents=observedDamage?.Length??ownerDamage.Count,doorDamageReplies=doorDamageReplies,acceptedInteractions=acceptedInteractions,
            targetOwner=Target?.owner,targetDamageBefore=fixture?.targetBefore.damageSequence??-1,targetDamageAfter=(observedTarget??Target)?.damageSequence??-1,
            targetReceiptsBefore=fixture?.targetBefore.resolvedAttacks.Count??-1,targetReceiptsAfter=(observedTarget??Target)?.resolvedAttacks.Count??-1,
            scopedOpenReceipts=(observedTarget??Target)?.resolvedAttacks.Count(id=>id==openAttack)??0,
            scopedClosedReceipts=(observedTarget??Target)?.resolvedAttacks.Count(id=>id==closedAttack)??0,
            scopedTargetReceipts=(observedTarget??Target)?.resolvedAttacks.ToArray(),rawTargetReceipts=Target?.resolvedAttacks.ToArray(),
            rawTargetDamageSequence=Target?.damageSequence??-1,rawTargetHealth=Target?.health??-1,
            targetHealthBefore=fixture?.targetBefore.health??-1,targetHealthAfter=(observedTarget??Target)?.health??-1,
            targetRestoreHealthAfterHit=fixture==null?0:ActorTypes.Get(fixture.targetBefore.actorType).restoreHealthAfterHit,
            sourceHealthAfter=runtime.Session.World?.Find(fixture?.source)?.health??-1,sourceDown=runtime.Session.World?.Find(fixture?.source)?.down??false,
            sourceGeneration=runtime.Session.World?.Find(fixture?.source)?.generation??-1,sourceLifecycle=runtime.Session.World?.Find(fixture?.source)?.lifecycleSequence??-1,
            sourceDamageBefore=fixture?.sourceBefore.damageSequence??-1,sourceDamageAfter=runtime.Session.World?.Find(fixture?.source)?.damageSequence??-1,
            sourceOwnerDamageEvents=sourceDamage.Count,sourceDamage=sourceDamage.ToArray(),sourceReceipts=runtime.Session.World?.Find(fixture?.source)?.resolvedAttacks.ToArray(),
            zeroDamageContacts=zeroDamageContacts.Values.ToArray(),rawDamage=ownerDamage.ToArray(),doorHealthBefore=fixture?.door.integrity.health??-1,
            doorHealthAfter=Door?.integrity?.health??-1,doorRevision=Door?.revision??-1,initial=fixture,samples=samples.ToArray(),voices=voices.ToArray(),frames=frames.ToArray(),damage=observedDamage??ownerDamage.ToArray(),sourceSwingTiming=swingTiming.ToArray()
        };
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            if(success && (!closedBlocked || !openHit || !dedup || !ownerDecision || frames.Count!=4 || closedSamples<3 || lateClosedSamples<2 || openSamples<3))
            {success=false;detail="Неполный локальный набор доказательств: "+Context();}
            if(success && Source && (swingTiming.Count!=2 || swingTiming.Any(s=>s.sourceFrameCount==0 || s.endedAt==0)))
            {success=false;detail="Нет реального timing обоих source swings: "+Context();}
            finished=true;File.WriteAllText(runtime.Options.Report,StateJson.ToJson(BuildReport(success,detail),true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1.5f);Application.Quit(success?0:2);}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
        private void OnDestroy()
        {
            if(runtime==null) return;
            runtime.Session.AttackReceived-=Attack;runtime.Combat.SurfaceContactObserved-=Surface;runtime.Combat.ContactObserved-=Contact;
            runtime.Combat.DamageApplied-=Damage;runtime.Session.DoorDamage.Finished-=DoorReply;runtime.Session.Doors.Finished-=DoorInteraction;runtime.Session.Notice-=Notice;
        }
    }
}
