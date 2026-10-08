using System;
using System.Collections.Generic;
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
    public struct FactoryContinuationObserved : NetworkMessage
    {public string actor;public int generation,lifecycle,phase;public uint motionSequence;}
    public struct FactoryContinuationDecision : NetworkMessage {public int phase;public string json;}

    // Ordinary connected route: movement intents, finite own weapons, E and actual UI claims.
    internal sealed class FactoryContinuationRoute
    {
        private enum Stage {None,Approach,DestructionBarrier,Destruction,DestroyedReplica,ToCup,Loot,Return,Returned,Complete}
        private const string Level="factory:0";
        private readonly PrototypeRuntime runtime;
        private readonly Func<bool> beforeDestructionReady;
        private const float FiringX=163.7f;
        private readonly FactoryRouteContainerClaims loot;
        private readonly HashSet<string> ready=new(StringComparer.Ordinal),returned=new(StringComparer.Ordinal);
        private readonly Dictionary<int,string> decisions=new();
        private readonly Dictionary<string,AttackEvent> attacks=new(StringComparer.Ordinal);
        private readonly Dictionary<string,DoorDamageRequestData> contacts=new(StringComparer.Ordinal);
        private readonly Dictionary<string,DoorDamageReply> replies=new(StringComparer.Ordinal);
        private readonly Dictionary<string,(NetworkConnectionToClient connection,FactoryContinuationObserved message,double deadline)> pendingObservations=new(StringComparer.Ordinal);
        private Stage stage;
        private FactoryContinuationRouteMotion motion;
        private FactoryRouteMovement firingMovement;
        private RoomLayout room;
        private string actorId,owner,capture;
        private int generation,lifecycle,serverPhase;
        private int stairFrames;
        private float maxStairFrameSeconds;
        private bool readySent,returnedSent,destructionSent,seamCaptured;
        public FactoryContinuationRouteEvidence Evidence {get;private set;}
        public bool Started=>stage!=Stage.None;
        public bool Complete=>stage==Stage.Complete;
        public bool WaitingForHealing=>(stage==Stage.Approach || stage==Stage.Destruction && !Host) &&
            motion.AtDestructionFor(Actor) && !beforeDestructionReady();
        public bool Critical=>Started && !Complete &&
            (motion.Critical || stage is Stage.DestructionBarrier or Stage.Destruction or Stage.DestroyedReplica or Stage.Loot or Stage.Returned);
        public string Failure {get;private set;}
        public string Progress=>$"continuationPhase={stage}/{motion?.Current}, actor={Actor?.id}, position={Actor?.Position}, velocity={Actor?.Velocity}, HP={Actor?.health}, life={Actor?.lifecycleSequence}/{lifecycle}, septum={Door?.integrity?.health}/{Door?.revision}, ready={ready.Count}, returned={returned.Count}, {loot.Progress}";
        private ActorState Actor=>runtime.Session.LocalActor;
        private DoorState Door=>runtime.Session.World.doors.Single(d=>d.id==FactoryContinuationRouteMotion.SeptumId);
        private ContainerState Cup=>runtime.Session.World.containers.Single(c=>c.id==FactoryContinuationRouteProof.CupId);
        private bool Host=>runtime.Options.Role=="host";

        public FactoryContinuationRoute(PrototypeRuntime value,FactoryRouteContainerChannel channel,Func<bool> beforeDestructionReady)
        {
            runtime=value;
            this.beforeDestructionReady=beforeDestructionReady??throw new ArgumentNullException(nameof(beforeDestructionReady));
            loot=new FactoryRouteContainerClaims(value,channel,FactoryContinuationRouteProof.CupId,"Комната 27",FactoryContinuationRouteProof.ValidateCup,"continuation-cup");
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FactoryContinuationObserved>(Observe);
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FactoryContinuationDecision>(AcceptDecision);
            value.Session.AttackReceived+=attack=>
            {
                if(stage!=Stage.Destruction || !Host || attack.attacker!=actorId) return;
                if(attacks.Count>=128) {Failure="Комната 27: превышен предел наблюдаемых атак перегородки.";return;}
                attacks.TryAdd(attack.id,StateJson.Copy(attack));
            };
            value.Session.DoorDamage.Reported+=contact=>
            {
                if(stage!=Stage.Destruction || !Host || contact.door!=FactoryContinuationRouteMotion.SeptumId) return;
                if(contact.attack.attacker!=actorId || contacts.Count>=128 || !contacts.TryAdd(contact.attack.id,StateJson.Copy(contact)))
                    Failure="Комната 27: неожиданный или повторный контакт перегородки.";
            };
            value.Session.DoorDamage.Finished+=reply=>
            {
                if(stage!=Stage.Destruction || !Host || reply.door!=FactoryContinuationRouteMotion.SeptumId) return;
                if(!reply.accepted || !reply.changed || replies.Count>=128 || !replies.TryAdd(reply.attack,reply))
                    Failure="Комната 27: контакт перегородки не принят единственным действием: "+reply.reason;
            };
        }
        public void Begin()
        {
            if(Started) throw new InvalidOperationException("Маршрут комнаты 27 уже начат.");
            actorId=Actor.id;owner=Actor.owner;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;
            room=runtime.Geometry.Get(Level);motion=new FactoryContinuationRouteMotion(room,Actor);
            firingMovement=new FactoryRouteMovement(room,"Позиция стрелка комнаты 27");
            firingMovement.RequireSupportedClear(Actor,new Vector2(FiringX,4));
            FactoryContinuationRouteProof.ValidateCup(Cup);
            Evidence=new FactoryContinuationRouteEvidence {world=runtime.Session.World.worldId,initialDoor=StateJson.Copy(Door)};
            if(Door.Destroyed || !Door.Blocks || Door.integrity?.health!=30 || Door.integrity.resolvedAttacks.Count!=0)
                throw new InvalidOperationException("Комната 27: настоящая перегородка уже изменена до ordinary маршрута.");
            runtime.Hud.CloseContainer();stage=Stage.Approach;capture="continuation-entry";
        }
        public void SynchronizeLifecycle(int previous,int next)
        {
            if(!Started || Complete) return;
            if(lifecycle!=previous || next!=previous+1) {Failure="Комната 27: неверная подтверждённая эпоха самолечения.";return;}
            lifecycle=next;loot.SynchronizeLifecycle(previous,next);
        }
        public PlayerCommand Command()
        {
            if(!Started || Complete || Failure!=null || runtime.Session.Pause.IsPaused(Level)) return default;
            try
            {
                if(stage==Stage.Loot) return loot.Command();
                // Advance the shooter on the broad supported bank instead of
                // sending a waiting ally back toward the gap. Both return normally.
                if(stage==Stage.Destruction && !Host) return default;
                if(stage==Stage.DestroyedReplica) return FactoryRouteMovement.Move(Actor,163.2f);
                if(stage==Stage.Destruction && Host && !Door.Destroyed)
                {
                    if(!firingMovement.AtRest(Actor,FiringX,4))
                        return FactoryRouteMovement.Move(Actor,FiringX);
                    var pistol=Actor.inventory.FirstOrDefault(i=>i.definition=="pistol" && i.weaponState!=null && InventoryAvailability.Count(Actor,i)>0);
                    if(pistol==null) throw new InvalidOperationException("Комната 27: нет собственного пистолета для перегородки.");
                    var aim=Door.Bounds.center;
                    if(pistol.weaponState.rounds==0 && InventoryJournal.Count(Actor,WeaponCatalog.Get("pistol").ammo)==0 && !runtime.Combat.Reload.Active)
                        throw new InvalidOperationException("Комната 27: настоящие патроны закончились до разрушения перегородки; запас не подменяется.");
                    return new PlayerCommand {hasAim=true,aim=aim,weaponItem=Actor.equippedWeaponItem==pistol.id?null:pistol.id,
                        reload=pistol.weaponState.rounds==0,fire=Actor.equippedWeaponItem==pistol.id && pistol.weaponState.rounds>0 &&
                        FactoryRouteShotSafety.Clear(runtime.Session.World,Actor,room,aim,Door.id)};
                }
                return stage is Stage.Approach or Stage.ToCup or Stage.Return?motion.Command(Actor):default;
            }
            catch(Exception failure) {Failure=failure.Message+": "+Progress;return default;}
        }
        public void Tick()
        {
            if(!Started || Complete || Failure!=null) return;
            try
            {
                if(Actor==null || Actor.id!=actorId || Actor.owner!=owner || Actor.owner!=runtime.Session.LocalOwner ||
                    Actor.generation!=generation || Actor.lifecycleSequence!=lifecycle || Actor.level!=Level || !Actor.connected ||
                    runtime.Session.Travel.Pending || !ReferenceEquals(room,runtime.Geometry.Get(Level)))
                    throw new InvalidOperationException("Комната 27: изменились личность, эпоха, локация или источник геометрии.");
                if(runtime.Session.Pause.IsPaused(Level)) return;
                if(stage is Stage.Approach or Stage.ToCup or Stage.Return)
                {
                    if(motion.Current is FactoryContinuationRouteMotion.Phase.Stairs or FactoryContinuationRouteMotion.Phase.ReturnStairs)
                    {stairFrames++;maxStairFrameSeconds=Mathf.Max(maxStairFrameSeconds,Time.deltaTime);}
                    var before=motion.Current;motion.Tick(Actor,Time.deltaTime);
                    if(before!=motion.Current) Debug.Log("Обычный маршрут комнаты 27: "+Progress);
                    if(motion.OutwardSeam && !seamCaptured) {seamCaptured=true;capture="continuation-seam";}
                }
                if(stage==Stage.Approach && motion.AtDestructionFor(Actor) && beforeDestructionReady())
                {stage=Stage.DestructionBarrier;readySent=true;SendObserved(0);}
                if(stage==Stage.Destruction && Host && Door.Destroyed && !destructionSent)
                {
                    var ids=Door.integrity.resolvedAttacks;
                    if(ids.Any(id=>!attacks.ContainsKey(id) || !contacts.ContainsKey(id) || !replies.ContainsKey(id))) return;
                    Evidence.destroyedDoor=StateJson.Copy(Door);Evidence.attacks=ids.Select(id=>attacks[id]).ToArray();
                    Evidence.contacts=ids.Select(id=>contacts[id]).ToArray();Evidence.replies=ids.Select(id=>replies[id]).ToArray();
                    if(!FactoryContinuationRouteProof.Destruction(Evidence,out var reason)) throw new InvalidOperationException(reason);
                    if(StateJson.ToJson(Door)!=StateJson.ToJson(runtime.Session.ServerWorld.doors.Single(d=>d.id==Door.id))) return;
                    destructionSent=true;NetworkServer.SendToAll(new FactoryContinuationDecision {phase=1,json=StateJson.ToJson(Evidence)});
                }
                if(stage==Stage.DestroyedReplica && motion.AtDestructionFor(Actor) && StateJson.ToJson(Door)==StateJson.ToJson(Evidence.destroyedDoor))
                {
                    if(room.platforms.Any(p=>p.sourceDoorId==Door.id)) return;
                    Evidence.destructionVerified=true;Evidence.geometryBlocked=false;capture="continuation-septum-destroyed";
                    motion.ContinueAfterDestruction(Actor);stage=Stage.ToCup;
                }
                if(stage==Stage.ToCup && motion.AtCup) {loot.Begin();stage=Stage.Loot;}
                if(stage==Stage.Loot)
                {
                    loot.Tick();if(loot.Failure!=null) throw new InvalidOperationException(loot.Failure);
                    if(loot.Complete)
                    {
                        Evidence.loot=loot.Evidence;Evidence.appearance=StateJson.Copy(Cup.appearance);
                        bool confirms=FactoryRouteLootProof.Confirms(runtime.Session.World,FactoryContinuationRouteProof.CupId,loot.Evidence,loot.Claims.ToArray(),out var reason);
                        if(Cup.items.Count!=0 || Cup.appearance?.opened!=true || !confirms)
                            throw new InvalidOperationException("Комната 27: пустой открытый cup28 не подтверждён: "+reason);
                        Evidence.lootVerified=true;runtime.Hud.CloseContainer();motion.BeginReturn(Actor);stage=Stage.Return;
                    }
                }
                if(stage==Stage.Return && motion.Complete)
                {
                    RecordMotionEvidence();
                    if(!Evidence.motionVerified || Cup.items.Count!=0 || StateJson.ToJson(Door)!=StateJson.ToJson(Evidence.destroyedDoor))
                        throw new InvalidOperationException("Комната 27: возврат утратил реальный motion, loot или целостность.");
                    Evidence.returned=true;stage=Stage.Returned;returnedSent=true;capture="continuation-lower-return";SendObserved(2);
                }
                if(Host) ProcessObservations();
            }
            catch(Exception failure) {RecordMotionEvidence();Failure=failure.Message+": "+Progress;}
        }
        private void RecordMotionEvidence()
        {
            if(Evidence==null || motion==null) return;
            Evidence.stairFrames=stairFrames;Evidence.maxStairFrameSeconds=maxStairFrameSeconds;
            Evidence.observer=actorId;Evidence.motion=motion.Witnesses.ToArray();Evidence.middleClimbUp=motion.MiddleClimbUp;Evidence.middleClimbDown=motion.MiddleClimbDown;
            Evidence.rightAirborne=motion.RightAirborne;Evidence.rightLanding=motion.RightLanding;
            Evidence.leftAirborne=motion.LeftAirborne;Evidence.leftLanding=motion.LeftLanding;
            Evidence.ascendingSlopes=motion.AscendingSlopes;Evidence.descendingSlopes=motion.DescendingSlopes;
            Evidence.outwardSeam=motion.OutwardSeam;Evidence.returnSeam=motion.ReturnSeam;
            Evidence.motionVerified=motion.MiddleClimbUp && motion.MiddleClimbDown && motion.RightAirborne && motion.RightLanding &&
                motion.LeftAirborne && motion.LeftLanding && motion.AscendingSlopes && motion.DescendingSlopes && motion.OutwardSeam && motion.ReturnSeam;
        }
        public FactoryContinuationRouteEvidence ExportEvidence()
        {
            // Explicit report export also retains partial witnesses after an outer failure.
            RecordMotionEvidence();return FactoryContinuationRouteProof.Snapshot(Evidence);
        }
        private void SendObserved(int phase)
        {
            // Send a real ordinary pose and fence the observation by its sequence.
            // An equal semantic revision may reject ActorMessage, so it cannot flush motion.
            runtime.Session.PublishPose(Actor);
            NetworkClient.Send(new FactoryContinuationObserved
                {actor=actorId,generation=generation,lifecycle=lifecycle,phase=phase,motionSequence=Actor.motionSequence});
        }
        private void Observe(NetworkConnectionToClient connection,FactoryContinuationObserved message)
        {
            if(Failure!=null || message.phase!=serverPhase) return;
            try
            {
                if(pendingObservations.TryGetValue(message.actor,out var prior))
                {
                    if(prior.connection!=connection || prior.message.phase!=message.phase || prior.message.generation!=message.generation ||
                        prior.message.lifecycle!=message.lifecycle || prior.message.motionSequence!=message.motionSequence)
                        throw new InvalidOperationException("Комната 27: повтор наблюдения изменил соединение, эпоху или sequence.");
                    return;
                }
                pendingObservations.Add(message.actor,(connection,message,Time.realtimeSinceStartupAsDouble+15));
                ProcessObservations();
            }
            catch(Exception failure) {RecordMotionEvidence();Failure=failure.Message+": "+Progress;}
        }
        private void ProcessObservations()
        {
            // A faster peer can arrive before this driver starts its own leg.
            // Begin binds the actual room; no substitute geometry is used meanwhile.
            if(!Started) return;
            foreach(var pending in pendingObservations.Values.ToArray())
            {
                var message=pending.message;
                var actor=runtime.Session.ConnectedActor(pending.connection);
                var outcome=FactoryContinuationRouteProof.EvaluateObservation(actor,message.actor,message.generation,message.lifecycle,
                    message.phase,message.motionSequence,room,out var reason);
                if(outcome==FactoryContinuationObservationStatus.Pending)
                {
                    if(Time.realtimeSinceStartupAsDouble>pending.deadline)
                        throw new InvalidOperationException("Комната 27: за 15 секунд не получена подтверждённая поза: "+reason);
                    continue;
                }
                if(outcome!=FactoryContinuationObservationStatus.Ready)
                    throw new InvalidOperationException("Комната 27: барьер не совпал с настоящим поддержанным игроком/эпохой: "+reason);
                pendingObservations.Remove(message.actor);
                var peers=message.phase==0?ready:returned;peers.Add(actor.id);
                if(peers.Count!=runtime.Options.ProbePeers) continue;
                var cohort=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected && a.level==Level).ToArray();
                if(cohort.Length!=peers.Count || cohort.Any(a=>!peers.Contains(a.id))) throw new InvalidOperationException("Комната 27: состав партии изменился на барьере.");
                if(message.phase==0)
                {
                    Evidence.actor=runtime.Session.LocalActorId;Evidence.driverGeneration=Actor.generation;Evidence.driverLifecycle=Actor.lifecycleSequence;
                    serverPhase=2;NetworkServer.SendToAll(new FactoryContinuationDecision {phase=0,json=StateJson.ToJson(Evidence)});
                }
                else {serverPhase=3;NetworkServer.SendToAll(new FactoryContinuationDecision {phase=2});}
            }
        }
        private void AcceptDecision(FactoryContinuationDecision message)
        {
            if(Failure!=null) return;
            try
            {
                if(decisions.TryGetValue(message.phase,out var prior))
                {if(prior!=message.json) throw new InvalidOperationException("Комната 27: повтор этапа изменил свидетельство.");return;}
                if(message.phase==0)
                {
                    if(stage!=Stage.DestructionBarrier || !readySent) throw new InvalidOperationException("Комната 27: разрушение до фактического подхода партии.");
                    var agreed=StateJson.FromJson<FactoryContinuationRouteEvidence>(message.json);
                    if(agreed.world!=Evidence.world || StateJson.ToJson(agreed.initialDoor)!=StateJson.ToJson(Evidence.initialDoor))
                        throw new InvalidOperationException("Комната 27: исходная перегородка различается у участников.");
                    Evidence=agreed;stage=Stage.Destruction;
                }
                else if(message.phase==1)
                {
                    if(stage!=Stage.Destruction) throw new InvalidOperationException("Комната 27: результат разрушения вне реального этапа.");
                    var finished=StateJson.FromJson<FactoryContinuationRouteEvidence>(message.json);
                    bool confirms=FactoryContinuationRouteProof.Destruction(finished,out var reason);
                    if(finished.actor!=Evidence.actor || finished.world!=Evidence.world ||
                        StateJson.ToJson(finished.initialDoor)!=StateJson.ToJson(Evidence.initialDoor) ||
                        !confirms)
                        throw new InvalidOperationException("Комната 27: неподтверждённое разрушение: "+reason);
                    Evidence=finished;stage=Stage.DestroyedReplica;
                }
                else if(message.phase==2)
                {
                    if(stage!=Stage.Returned || !returnedSent || !Evidence.motionVerified || !Evidence.returned)
                        throw new InvalidOperationException("Комната 27: завершение до настоящего возврата.");
                    stage=Stage.Complete;
                }
                else throw new InvalidOperationException("Комната 27: неизвестный сетевой этап.");
                decisions.Add(message.phase,message.json);
            }
            catch(Exception failure) {Failure=failure.Message+": "+Progress;}
        }
        public void Capture()
        {
            loot.Capture();if(capture==null) return;
            if(runtime.Options.Smoke.Contains("-frames-"))
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                NetworkSceneRecord.Save(runtime,250+(int)stage);
            }
            capture=null;
        }
    }
}
