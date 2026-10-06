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
using Remains.Serialization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Runtime
{
    public struct OfficeRecoveryFixture : NetworkMessage {public string json;}
    public struct OfficeRecoveryPhase : NetworkMessage {public int phase,attempt;public string json;}
    public struct OfficeRecoveryObserved : NetworkMessage {public string actor,npc,owner,attack,localJson,reason;public int generation,phase,attempt,npcGeneration;public bool valid,paused,shotObserved;}

    // Separate fresh/reload processes. Only the explicitly reported initial player
    // fixture writes coordinates; every subsequent action uses production input/session APIs.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryOfficeRecoveryProbe : MonoBehaviour
    {
        public const string NpcId="factory:0:original:plant:53:ql47iXGSZW2VGGdF";
        public const string WitnessFile="office-recovery-witness.json";
        private const string Level="factory:0";
        private PrototypeRuntime runtime;
        private OfficeRecoveryWitness proof;
        private ActorState localPrepared,localBeforeDeparture;
        private RoomLayout fixtureRoom;
        private readonly HashSet<string> captures=new(StringComparer.Ordinal),claims=new(StringComparer.Ordinal),shots=new(StringComparer.Ordinal);
        private readonly List<AttackEvent> npcShots=new();
        private readonly OfficeRecoveryPreparationGate preparation=new();
        private readonly List<string> candidateRetries=new();
        private readonly HashSet<string> rejectedAttacks=new(StringComparer.Ordinal);
        private readonly HashSet<string> seenNpcShots=new(StringComparer.Ordinal);
        private readonly AttackEvent friendlyCorridor=new() {weapon="pistol"};
        private int phase,generation,shotsAfterHandoff,roundsAtOpen,candidateAttempt;
        private float deadline,quietAt=-1,nextHeal,transferAt;
        private string owner,ownPistol,capture,aimedThreat,loadedSnapshot,archivePath;
        private bool traveled,installed,reported,negativeReported,finished,disconnecting,stopped,opened,draining,saving,edge,reinstallVerified,loadedVerified,postTargetObserved;
        private bool Host=>runtime.Options.Role=="host";
        private bool Reload=>runtime.Options.Smoke.Contains("-reload-",StringComparison.Ordinal);
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Npc=>runtime.Session.World?.Find(NpcId);
        private string Progress=>$"phase={phase}, attempt={candidateAttempt}, confirmed={proof?.confirmedAttackId}, retries={candidateRetries.Count}, reload={Reload}, actor={Actor?.id}/{Actor?.Position}/HP{Actor?.health}/down{Actor?.down}/gen{Actor?.generation}/life{Actor?.lifecycleSequence}/owner{Actor?.owner}, npc={Npc?.Position}/HP{Npc?.health}/owner{Npc?.owner}/gen{Npc?.generation}/target{Npc?.target}/busy{Npc?.attackBusy}/attack{Npc?.attackId}/remaining{Npc?.attackRemaining}/cooldown{Npc?.attackCooldown}, paused={runtime.Session.Pause.IsPaused(Level)}, items={Npc?.inventory.Count}, claims={claims.Count}, postShots={shotsAfterHandoff}, UI={runtime.Hud.ContainerOpen}/{runtime.Hud.ShownInventorySourceId}";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+(Reload?45:180);
            if(string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report) ||
                (!Reload && value.Options.ProbePeers!=2) || Reload && (!Host || value.Options.ProbePeers!=1))
                throw new InvalidOperationException("Office recovery требует отдельный профиль: fresh pair или reload host solo.");
            if(Host)
            {
                var selected=value.Session.Saves.SelectedSnapshot();
                if(Reload)
                {
                    string path=Path.Combine(value.Session.Saves.WorldDirectoryPath,WitnessFile);
                    if(selected==null || !File.Exists(path)) throw new InvalidOperationException("Нет сохранения/свидетельства предыдущего office recovery; новый мир не создаётся.");
                    proof=StateJson.FromJson<OfficeRecoveryWitness>(File.ReadAllText(path));loadedSnapshot=selected.Id;
                    if(proof.sourceSnapshot!=loadedSnapshot) throw new InvalidOperationException("Выбран другой снимок вместо завершённого office recovery.");
                    Require(OfficeRecoveryEvidence.Loot(value.Session.Saves.LoadWorld(false),proof));
                    archivePath=Path.Combine(value.Session.Saves.WorldDirectoryPath,"Worlds",selected.WorldId,"Snapshots",selected.Id+".json");
                    if(!File.Exists(archivePath) || RecoverySnapshot.Fingerprint(File.ReadAllText(archivePath))!=selected.Id)
                        throw new InvalidOperationException("Исходный выбранный снимок не сохранён в неизменяемом архиве.");
                    loadedVerified=true;phase=20;
                }
                else
                {
                    if(selected!=null || value.Session.Saves.ReadCharacterSource()!=null)
                        throw new InvalidOperationException("Fresh office recovery не заменяет существующие мир/персонажа.");
                    value.Session.Saves.SaveWorld(NewGameWorld.Create());
                }
            }
            else if(value.Session.Saves.SelectedSnapshot()!=null || value.Session.Saves.ReadCharacterSource()!=null)
                throw new InvalidOperationException("Клиент office recovery требует отдельный новый профиль.");
            value.CommandSource=Command;
            value.Session.AttackReceived+=ObserveAttack;
            value.Session.ClaimPrepared+=(transaction,accepted)=>
            {
                if(phase!=8 || finished) return;
                if(!accepted) {Fail("UI claim тела отклонён: "+transaction);return;}
                claims.Add(transaction);
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<OfficeRecoveryFixture>(message=>
                {
                    proof=StateJson.FromJson<OfficeRecoveryWitness>(message.json);installed=true;phase=1;
                    CacheActor();fixtureRoom=runtime.Geometry.Get(Level);capture="fixture";
                });
                NetworkClient.RegisterHandler<OfficeRecoveryPhase>(ReceivePhase);
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<OfficeRecoveryObserved>(Observe);
        }
        private void ResetLocalPreparation()
        {
            localPrepared=null;localBeforeDeparture=null;reported=false;negativeReported=false;captures.Remove("prepared");
            if(capture=="prepared") capture=null;
        }
        private void ReceivePhase(OfficeRecoveryPhase message)
        {
            if(finished || message.attempt<candidateAttempt) return;
            try
            {
                // Duplicate phase messages cannot erase an already captured witness.
                if(message.attempt==candidateAttempt && (message.phase==phase || message.phase==3 && phase!=3)) return;
                if(message.phase==3 || message.phase==1)
                {
                    if(proof?.confirmedAttackId!=null) throw new InvalidOperationException("Сброс подготовки после consensus recovery.");
                    ResetLocalPreparation();
                }
                if(message.json!=null) proof=StateJson.FromJson<OfficeRecoveryWitness>(message.json);
                candidateAttempt=message.attempt;phase=message.phase;reported=false;
                if(phase==4)
                {
                    if(proof?.confirmedAttackId!=proof?.preparedNpc?.attackId || string.IsNullOrEmpty(proof?.confirmedAttackId) ||
                        proof.preparationAttempt!=candidateAttempt || localPrepared==null)
                        throw new InvalidOperationException("Departure без согласованного exact candidate/witness.");
                    Require(OfficeRecoveryEvidence.Preparation(proof.preparedNpc,Npc,runtime.Session.Pause.IsPaused(Level),ShotObserved(proof.confirmedAttackId)));
                    if(!Host && !disconnecting) StartCoroutine(SafeRoutine(Depart()));
                }
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private bool ShotObserved(string id)=>!string.IsNullOrEmpty(id) && (seenNpcShots.Contains(id) ||
            runtime.Session.World.projectiles.Any(p=>p.launch?.id==id));
        private void RetryPreparation(string reason)
        {
            if(!Host || proof?.confirmedAttackId!=null) throw new InvalidOperationException("Retry допустим только до consensus recovery.");
            string attack=proof.preparedNpc?.attackId;
            if(!string.IsNullOrEmpty(attack)) rejectedAttacks.Add(attack);
            candidateRetries.Add($"attempt={candidateAttempt}, attack={attack}, reason={reason}; {Progress}");
            Debug.LogWarning("Office recovery: кандидат отклонён до consensus; "+candidateRetries[^1]);
            preparation.Reset();proof.preparedNpc=null;ResetLocalPreparation();phase=1;
            runtime.Session.Pause.SetManual(false);
            NetworkServer.SendToAll(new OfficeRecoveryPhase {phase=1,attempt=candidateAttempt,json=StateJson.ToJson(proof)});
        }
        private void CacheActor()
        {
            var actor=Actor??throw new InvalidOperationException("Fixture доставлена до подтверждённого игрока.");
            generation=actor.generation;owner=actor.owner;
            ownPistol=actor.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null).id;
        }
        private static void Require(string reason) {if(reason!=null) throw new InvalidOperationException(reason);}
        private void ObserveAttack(AttackEvent attack)
        {
            if(finished || proof==null) return;
            if(attack.attacker==proof.hostId) shots.Add(attack.id);
            if(attack.attacker!=NpcId) return;
            // Only consensus establishes the exact forbidden ID; delayed duplicate
            // deliveries of that ID are still failures after confirmation.
            if(attack.id==proof.confirmedAttackId && !string.IsNullOrEmpty(proof.confirmedAttackId))
                {Fail("Отменённая атака появилась после подтверждённой подготовки: "+attack.id);return;}
            if(!seenNpcShots.Add(attack.id)) return;
            npcShots.Add(StateJson.Copy(attack));
            if(Host && phase>=6 && attack.sourceOwner==runtime.Session.LocalOwner) shotsAfterHandoff++;
        }
        private PlayerCommand Command()
        {
            try {return ChooseCommand();} catch(Exception error) {Fail(error.Message);return default;}
        }
        private PlayerCommand ChooseCommand()
        {
            if(finished || Reload || !Host || !installed || phase<6 || phase>8 || Actor is not { } actor ||
                runtime.Session.Pause.IsPaused(Level)) return default;
            var command=new PlayerCommand();
            if(runtime.Hud.ContainerOpen)
            {
                var mouse=Mouse.current;
                if(mouse?.name=="SessionProbeMouse")
                {command.hasPointer=true;command.pointer=mouse.position.ReadValue();command.fire=mouse.leftButton.isPressed;command.hasAim=true;command.aim=InventorySource.BodyBounds(Npc).center;}
                return command;
            }
            if(actor.health<70 && InventoryJournal.Count(actor,"heal")>0 && Time.unscaledTime>=nextHeal)
            {command.heal=true;nextHeal=Time.unscaledTime+1.5f;}
            if(shotsAfterHandoff==0) return command;
            // The chosen saved raider, then actual threats on this player only.
            var threat=Npc is {down:false,health:>0}?Npc:runtime.Session.World.actors.Where(a=>!a.player && !a.down && a.health>0 && a.level==Level &&
                (a.target==actor.id || a.attackBusy && a.attackTarget==actor.id)).OrderBy(a=>(a.Position-actor.Position).sqrMagnitude).FirstOrDefault();
            if(threat!=null)
            {
                var pistol=InventoryStacks.Find(actor,ownPistol)??throw new InvalidOperationException("Потерян собственный исходный пистолет.");
                command.weaponItem=actor.equippedWeaponItem==ownPistol?null:ownPistol;
                if(!Anatomy.TryRegion(threat,"head",out var aim,out _)) throw new InvalidOperationException("Нет actual head привязки: "+threat.id);
                command.hasAim=true;command.aim=aim;
                command.reload=pistol.weaponState.rounds==0 && InventoryJournal.Count(actor,"p10")>0;
                if(pistol.weaponState.rounds==0 && InventoryJournal.Count(actor,"p10")==0 && !runtime.Combat.Reload.Active)
                    throw new InvalidOperationException("Настоящий исходный запас патронов закончился до боя/лутового окна.");
                var muzzle=WeaponPose.Muzzle(actor);
                friendlyCorridor.originX=muzzle.x;friendlyCorridor.originY=muzzle.y;friendlyCorridor.endX=aim.x;friendlyCorridor.endY=aim.y;
                bool ally=runtime.Session.World.actors.Any(a=>a.player && a.id!=actor.id && a.level==Level && ActorPresence.IsPresent(a) && Anatomy.Contact(a,friendlyCorridor)!=null);
                command.fire=aimedThreat==threat.id && actor.equippedWeaponItem==ownPistol && pistol.weaponState.rounds>0 && !ally;
                aimedThreat=threat.id;quietAt=-1;return command;
            }
            aimedThreat=null;if(quietAt<0) quietAt=Time.unscaledTime;
            if(phase!=7 || runtime.Combat.Reload.Active || Time.unscaledTime-quietAt<.4f) return command;
            if(Mathf.Abs(actor.x-Npc.x)>.4f) {command.movement=Vector2.right*Mathf.Sign(Npc.x-actor.x);return command;}
            var source=InventorySource.Resolve(runtime.Session.World,InventorySource.BodyId(NpcId));
            command.hasAim=true;command.aim=source.Bounds.center;
            if(!edge)
            {
                var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,command.aim);
                if(target.kind!=InteractionKind.Container || target.id!=source.id)
                    throw new InvalidOperationException($"E тела выбрал {target.kind}/{target.id} вместо {source.id}.");
                edge=true;command.interact=true;
            }
            return command;
        }
        private void Update()
        {
            if(finished) return;
            try {Tick();} catch(Exception error) {Fail(error.Message);}
        }
        private void Tick()
        {
            if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут office recovery: "+Progress);
            if(disconnecting) return;
            var session=runtime.Session;var actor=Actor;
            if(actor==null) return;
            if(Reload) {TickReload();return;}
            if(!traveled && session.World.actors.Count(a=>a.player && a.connected)==2)
            {traveled=true;session.Travel.RequestFactory();}
            if(Host && !installed && session.ServerWorld.actors.Count(a=>a.player && a.connected && a.level==Level)==2 &&
                session.World.actors.Count(a=>a.player && a.connected && a.level==Level)==2 && !session.Travel.Pending) Setup();
            if(!installed) return;
            if(actor.owner!=owner || actor.owner!=session.LocalOwner || actor.generation!=generation || !actor.connected ||
                actor.level!=Level || actor.down || actor.health<=0 || session.Travel.Pending || !ReferenceEquals(fixtureRoom,runtime.Geometry.Get(Level)))
                throw new InvalidOperationException("Fixture потеряла активного собственного игрока/уровень/геометрию.");
            if(ShotObserved(proof.confirmedAttackId))
                throw new InvalidOperationException("В мире/AttackReceived появился снаряд отменённого confirmed attackID.");
            if(phase==3 && (!reported || !negativeReported))
            {
                var npc=Npc;bool paused=session.Pause.IsPaused(Level),shotObserved=ShotObserved(proof.preparedNpc.attackId);
                string reason=OfficeRecoveryEvidence.Preparation(proof.preparedNpc,npc,paused,shotObserved);
                if(reason==null && localPrepared==null)
                {localPrepared=npc.Copy();localBeforeDeparture=actor.Copy();capture="prepared";}
                if(reason!=null && !negativeReported || reason==null && !reported && captures.Contains("prepared"))
                {
                    reported=true;negativeReported=reason!=null;
                    if(reason!=null) candidateRetries.Add($"Отрицательный witness attempt={candidateAttempt}, attack={proof.preparedNpc.attackId}: {reason}; {Progress}");
                    NetworkClient.Send(new OfficeRecoveryObserved {actor=actor.id,generation=actor.generation,phase=3,attempt=candidateAttempt,npc=NpcId,
                        owner=proof.preparedNpc.owner,npcGeneration=proof.preparedNpc.generation,attack=proof.preparedNpc.attackId,
                        valid=reason==null,paused=paused,shotObserved=shotObserved,reason=reason,localJson=StateJson.ToJson(npc)});
                }
            }
            if(!Host) return;
            var canonical=session.ServerWorld.Find(NpcId);var departing=session.ServerWorld.Find(proof.departedId);
            if(phase==1 && !session.Pause.IsPaused(Level) && canonical.owner==departing.owner && canonical.target==departing.id &&
                canonical.attackBusy && canonical.attackTarget==departing.id && canonical.npcWeaponAction?.kind==NpcWeaponActions.Shot &&
                !rejectedAttacks.Contains(canonical.attackId) && !ShotObserved(canonical.attackId))
            {phase=2;session.Pause.SetManual(true);}
            if(phase==2 && session.Pause.IsServerPaused(Level) && session.Pause.IsPaused(Level))
            {
                // If confirmation arrived after the real shot, try the next real preparation.
                if(!canonical.attackBusy || canonical.owner!=departing.owner || canonical.attackTarget!=departing.id ||
                    canonical.npcWeaponAction?.kind!=NpcWeaponActions.Shot || ShotObserved(canonical.attackId))
                {RetryPreparation("Canonical подготовка закончилась до первого paused barrier.");return;}
                preparation.Begin(canonical,proof.hostId,proof.departedId);candidateAttempt=preparation.Attempt;
                proof.preparationAttempt=candidateAttempt;proof.preparedNpc=preparation.Candidate.Copy();proof.departedBefore=departing.Copy();
                ResetLocalPreparation();phase=3;
                NetworkServer.SendToAll(new OfficeRecoveryPhase {phase=3,attempt=candidateAttempt,json=StateJson.ToJson(proof)});
            }
            if(phase==4 && !departing.connected)
            {
                Require(OfficeRecoveryEvidence.Handoff(proof.preparedNpc,canonical,proof.departedBefore,departing,session.ServerWorld.Find(proof.hostId)));
                if(!session.Pause.IsServerPaused(Level) || !session.Pause.IsPaused(Level)) throw new InvalidOperationException("Пауза хоста потеряна при отключении клиента.");
                if(Npc.owner!=session.LocalOwner || Npc.generation!=canonical.generation || Npc.attackBusy) return;
                proof.recoveredNpc=canonical.Copy();proof.departedAfter=departing.Copy();phase=5;capture="handoff";transferAt=Time.unscaledTime;
            }
            if(phase==5 && captures.Contains("handoff") && Time.unscaledTime-transferAt>.4f)
            {
                if(!session.Pause.IsPaused(Level) || !session.Pause.IsServerPaused(Level) || Mathf.Abs(canonical.attackCooldown-proof.recoveredNpc.attackCooldown)>.001f)
                    throw new InvalidOperationException("Подтверждённая handoff пауза изменила cooldown или была снята до наблюдения.");
                Require(OfficeRecoveryEvidence.Handoff(proof.preparedNpc,canonical,proof.departedBefore,departing,session.ServerWorld.Find(proof.hostId)));
                phase=6;session.Pause.SetManual(false);
            }
            if(phase==6 && Npc.owner==session.LocalOwner && Npc.attackBusy && Npc.attackTarget==actor.id) postTargetObserved=true;
            if(phase==6 && Npc.down)
            {
                if(Npc.health>0 || shotsAfterHandoff==0 || !postTargetObserved || !Npc.resolvedAttacks.Any(shots.Contains))
                    throw new InvalidOperationException("Смерть не подтверждена настоящими выстрелами хоста после новой атаки нового владельца.");
                proof.deadNpc=Npc.Copy();phase=7;capture="defeated";
            }
            if(phase==7 && runtime.Hud.ContainerOpen)
            {
                if(runtime.Hud.ShownInventorySourceId!=InventorySource.BodyId(NpcId)) throw new InvalidOperationException("Открыт инвентарь другого тела.");
                opened=true;phase=8;roundsAtOpen=InventoryStacks.Find(actor,ownPistol).weaponState.rounds;
                proof.deadNpc=Npc.Copy();proof.corpseItems=Npc.inventory.Select(i=>i.Copy()).ToArray();proof.hostBeforeLoot=actor.Copy();capture="inventory";
                if(proof.corpseItems.Length==0) throw new InvalidOperationException("Настоящее тело уже пусто до первого UI claim.");
            }
            if(phase==8)
            {
                if(!runtime.Hud.ContainerOpen || runtime.Hud.ShownInventorySourceId!=InventorySource.BodyId(NpcId))
                    throw new InvalidOperationException("Окно тела потеряно во время выдачи.");
                if(InventoryStacks.Find(actor,ownPistol).weaponState.rounds!=roundsAtOpen)
                    throw new InvalidOperationException("ЛКМ окна тела выстрелил или изменил свой магазин.");
                if(!draining && captures.Contains("inventory")) {draining=true;StartCoroutine(SafeRoutine(Drain()));}
                if(draining && Npc.inventory.Count==0 && session.World.pendingGrants.Count==0 && claims.Count==proof.corpseItems.Length &&
                    claims.All(session.World.completedTransactions.Contains) && !saving)
                {
                    proof.claims=claims.OrderBy(id=>id,StringComparer.Ordinal).ToArray();proof.departedSaved=session.ServerWorld.Find(proof.departedId).Copy();
                    Require(OfficeRecoveryEvidence.Loot(session.World,proof));phase=9;saving=true;capture="empty";StartCoroutine(SafeRoutine(SaveAndStop()));
                }
            }
        }
        private void Observe(NetworkConnectionToClient connection,OfficeRecoveryObserved message)
        {
            if(phase!=3 || proof?.preparedNpc==null || message.attempt!=candidateAttempt) return;
            try
            {
                var actor=runtime.Session.ConnectedActor(connection);var candidate=proof.preparedNpc;
                if(actor==null || actor.id!=message.actor || actor.generation!=message.generation || actor.level!=Level || !actor.connected ||
                    (actor.id!=proof.hostId && actor.id!=proof.departedId) || message.phase!=3 || message.npc!=NpcId ||
                    message.owner!=candidate.owner || message.npcGeneration!=candidate.generation || message.attack!=candidate.attackId)
                    throw new InvalidOperationException("Наблюдение подготовки не соответствует участнику/exact candidate.");
                var observed=StateJson.FromJson<ActorState>(message.localJson);
                var result=preparation.Observe(message.attempt,actor.id,observed,message.paused,message.shotObserved,out string reason);
                if(!message.valid || result==OfficeRecoveryPreparationOutcome.Retry)
                {RetryPreparation($"peer={actor.id}: {reason??message.reason??"Отрицательное наблюдение кандидата."}");return;}
                if(result!=OfficeRecoveryPreparationOutcome.Ready) return;
                var canonical=runtime.Session.ServerWorld.Find(NpcId);
                string error=preparation.Confirm(canonical,runtime.Session.Pause.IsServerPaused(Level) && runtime.Session.Pause.IsPaused(Level),ShotObserved(candidate.attackId));
                if(error!=null) {RetryPreparation("Canonical recheck: "+error);return;}
                proof.confirmedAttackId=preparation.ConfirmedAttackId;phase=4;
                NetworkServer.SendToAll(new OfficeRecoveryPhase {phase=4,attempt=candidateAttempt,json=StateJson.ToJson(proof)});
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void Setup()
        {
            var world=runtime.Session.ServerWorld;var npc=world.Find(NpcId);var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            if(WorldRooms.Definition(world,Level)!=NewGameWorld.FactoryDefinitionId || players.Length!=2 ||
                npc is not {actorType:"raider",down:false,health:140} || Vector2.Distance(npc.Position,new Vector2(104.8f,11.2f))>.001f || npc.inventory.Count!=3)
                throw new InvalidOperationException("Не получены текущая saved office geometry и настоящий исходный raider64.");
            var host=players.Single(a=>a.character=="session-host");var client=players.Single(a=>a.character=="session-client-1");
            var room=runtime.Geometry.Get(Level);
            foreach(var player in players)
            {
                var position=new Vector2(player.id==host.id?106:103.8f,11.2f);var hull=Anatomy.Movement(player);
                var rect=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
                if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position))
                    throw new InvalidOperationException("Initial fixture не имеет свободной реальной опоры: "+position);
                foreach(var prop in WorldProps.All(world)) if(prop.hasPhysicalBody && prop.level==Level && Overlap(rect,new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)))
                    throw new InvalidOperationException($"Initial fixture пересекает physical prop: actor={player.id}, prop={prop.id}, position={position}.");
                foreach(var other in world.actors) if(!other.player && other.level==Level && Overlap(rect,InventorySource.BodyBounds(other)))
                    throw new InvalidOperationException($"Initial fixture пересекает исходного NPC: actor={player.id}, npc={other.id}.");
                // This is the sole allowed fixture mutation. HP, inventory, NPCs and props are untouched.
                player.Position=position;player.Velocity=Vector2.zero;player.climbing=false;player.crouched=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
            }
            proof=new OfficeRecoveryWitness {worldId=world.worldId,npcId=NpcId,hostId=host.id,departedId=client.id,initialNpc=npc.Copy(),departedBefore=client.Copy()};
            installed=true;runtime.Session.CommitWorld();CacheActor();fixtureRoom=room;phase=1;capture="fixture";
            NetworkServer.SendToAll(new OfficeRecoveryFixture {json=StateJson.ToJson(proof)});
        }
        private static bool Overlap(Rect a,Rect b)=>a.xMax>b.xMin+.0001f && a.xMin<b.xMax-.0001f && a.yMax>b.yMin+.0001f && a.yMin<b.yMax-.0001f;
        private IEnumerator Depart()
        {
            disconnecting=true;
            if(!captures.Contains("prepared") || localPrepared?.owner!=runtime.Session.LocalOwner || localPrepared.attackId!=proof.confirmedAttackId ||
                proof.preparationAttempt!=candidateAttempt)
                {Fail("Клиент не подтвердил собственную подготовку до StopClient.");yield break;}
            Require(OfficeRecoveryEvidence.Preparation(proof.preparedNpc,Npc,runtime.Session.Pause.IsPaused(Level),ShotObserved(proof.confirmedAttackId)));
            runtime.Session.StopClient();
            float until=Time.unscaledTime+5;
            while(NetworkClient.active && Time.unscaledTime<until) yield return null;
            if(NetworkClient.active) {Fail("StopClient не завершил транспорт за5с.");yield break;}
            stopped=true;Finish(true,"Клиент штатно завершил transport после собственного paused attack witness; результат последующей передачи проверяет хост.");
        }
        private IEnumerator SafeRoutine(IEnumerator routine)
        {
            try
            {
                while(!finished)
                {
                    bool next;try {next=routine.MoveNext();} catch(Exception error) {Fail(error.Message);yield break;}
                    if(!next) yield break;yield return routine.Current;
                }
            }
            finally {(routine as IDisposable)?.Dispose();}
        }
        private IEnumerator SafeClick(RectTransform target)
        {
            var click=UiPointerProbe.Click(target,followTarget:true);
            try
            {
                while(!finished)
                {
                    bool next;try {next=click.MoveNext();} catch(Exception error) {Fail(error.Message);yield break;}
                    if(!next) yield break;yield return click.Current;
                }
            }
            finally {(click as IDisposable)?.Dispose();}
        }
        private IEnumerator Drain()
        {
            while(!finished && phase==8 && Npc.inventory.Count>0)
            {
                string item=Npc.inventory[0].id;yield return SafeClick(runtime.Hud.ContainerSlotRect(0));
                while(!finished && (Npc.inventory.Any(i=>i.id==item) || runtime.Session.World.pendingGrants.Count>0)) yield return null;
            }
        }
        private IEnumerator SaveAndStop()
        {
            while(!finished && !captures.Contains("empty")) yield return null;
            if(finished) yield break;
            // Freeze through the real pause API before the independent canonical witness.
            runtime.Session.Pause.SetManual(true);
            while(!finished && (!runtime.Session.Pause.IsServerPaused(Level) || !runtime.Session.Pause.IsPaused(Level))) yield return null;
            if(finished) yield break;
            proof.departedSaved=runtime.Session.ServerWorld.Find(proof.departedId).Copy();
            runtime.Session.CommitWorld();runtime.Session.SaveLocalCharacter();
            var committed=runtime.Session.Saves.LoadWorld(false);
            Require(OfficeRecoveryEvidence.Loot(committed,proof));
            reinstallVerified=RoomContentProbePersistence.UnchangedOnReinstall(runtime.Session.Saves.LoadWorld(false),Level);
            Require(reinstallVerified?null:"Production reinstall восстановил смерть или лут.");
            capture="saved";while(!finished && !captures.Contains("saved")) yield return null;
            if(finished) yield break;
            // The frozen core must remain the same before the second live witness.
            Require(OfficeRecoveryEvidence.Body(proof.departedSaved,runtime.Session.ServerWorld.Find(proof.departedId)));
            // A second live expectation precedes the shutdown save; never derive it from disk.
            proof.departedSaved=runtime.Session.ServerWorld.Find(proof.departedId).Copy();
            runtime.Session.StopHost();
            while(NetworkServer.active || NetworkClient.active) yield return null;
            var saved=runtime.Session.Saves.LoadWorld(false);Require(OfficeRecoveryEvidence.Loot(saved,proof));
            proof.hostSaved=saved.Find(proof.hostId).Copy();
            proof.sourceSnapshot=runtime.Session.Saves.SelectedSnapshot().Id;
            File.WriteAllText(Path.Combine(runtime.Session.Saves.WorldDirectoryPath,WitnessFile),StateJson.ToJson(proof,true));
            stopped=true;Finish(true,"Офис: обе paused реплики, штатное отключение владельца, отмена exact attackID без расхода, новое поколение хоста и новая настоящая атака; обычный бой/E/UI, пустое тело и полный unique loot сохранены перед внешним restart.");
        }
        private void TickReload()
        {
            if(Actor.level!=Level || Actor.id!=proof.hostId || !Actor.connected || Actor.down || Actor.health<=0)
                throw new InvalidOperationException("Внешний restart не вернул того же живого игрока в сохранённый офис.");
            if(!reinstallVerified)
            {
                Require(OfficeRecoveryEvidence.Loot(runtime.Session.World,proof));
                if(!File.Exists(archivePath) || RecoverySnapshot.Fingerprint(File.ReadAllText(archivePath))!=loadedSnapshot)
                    throw new InvalidOperationException("Внешний restart изменил/удалил исходный selected snapshot.");
                reinstallVerified=RoomContentProbePersistence.UnchangedOnReinstall(runtime.Session.World.Copy(),Level);
                Require(reinstallVerified?null:"Повторная production установка после внешнего restart изменила офисный контент.");
                capture="reloaded";
            }
            if(captures.Contains("reloaded"))
            {
                runtime.Session.SaveLocalCharacter();runtime.Session.StopHost();stopped=true;
                Finish(true,"Новый внешний процесс загрузил тот же canonical WorldId: сохранены смерть/пустое тело, exact item IDs/магазин/количества/транзакции и disconnected тело; source snapshot сохранён, повторная установка не пополнила контент. Fixture не выполнялась.");
            }
        }
        private void LateUpdate()
        {
            if(capture==null || finished) return;
            try
            {
                // Do not capture a rejected preconsensus candidate as prepared proof.
                if(capture=="prepared" && phase==3 && OfficeRecoveryEvidence.Preparation(proof.preparedNpc,Npc,
                    runtime.Session.Pause.IsPaused(Level),ShotObserved(proof.preparedNpc.attackId))!=null)
                {capture=null;return;}
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".office-recovery-"+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                NetworkSceneRecord.Save(runtime,1000+phase);captures.Add(capture);capture=null;
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void Fail(string reason)=>Finish(false,reason+"; "+Progress);
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            if(!success && Actor!=null && runtime.Session.World!=null && Camera.main!=null)
            {
                try {NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".office-recovery-failure.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);NetworkSceneRecord.Save(runtime,1099);}
                catch(Exception error) {detail+=" Failure capture: "+error.Message;}
            }
            finished=true;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,mode=Reload?"reload":Host?"host":"departing-client",phase=phase,
                explicitInitialFixture=!Reload,gracefulStopped=stopped,preparedObserved=localPrepared!=null,handoffVerified=proof?.recoveredNpc!=null,
                opened=opened,emptyVerified=proof?.corpseItems!=null && proof.claims!=null,savedVerified=proof?.sourceSnapshot!=null,reinstallVerified=reinstallVerified,
                externalReloadVerified=loadedVerified && Reload,postTargetObserved=postTargetObserved,sourceSnapshot=proof?.sourceSnapshot,worldId=proof?.worldId,npc=NpcId,
                captures=captures.OrderBy(c=>c,StringComparer.Ordinal).ToArray(),localPrepared=localPrepared,localBeforeDeparture=localBeforeDeparture,
                candidateAttempt=candidateAttempt,candidateRetries=candidateRetries.ToArray(),confirmedAttackId=proof?.confirmedAttackId,witness=proof,npcShots=npcShots.ToArray(),postHandoffShots=shotsAfterHandoff,ownShotIds=shots.ToArray(),progress=Progress},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(.5f);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report
        {
            public bool success,explicitInitialFixture,gracefulStopped,preparedObserved,handoffVerified,opened,emptyVerified,savedVerified,reinstallVerified,externalReloadVerified,postTargetObserved;
            public string detail,mode,sourceSnapshot,worldId,npc,progress,confirmedAttackId;public int phase,postHandoffShots,candidateAttempt;
            public string[] captures,ownShotIds,candidateRetries;public ActorState localPrepared,localBeforeDeparture;public OfficeRecoveryWitness witness;public AttackEvent[] npcShots;
        }
    }
}
