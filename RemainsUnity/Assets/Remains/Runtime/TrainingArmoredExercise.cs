using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct TrainingBaseReadyMessage : NetworkMessage {}
    public struct TrainingArmoredBeginMessage : NetworkMessage {public string source;}
    public struct TrainingArmoredReadyMessage : NetworkMessage {public int phase;public string attack;public int ownerDamageApplications;}
    public struct TrainingArmoredPhaseMessage : NetworkMessage {public int phase,totalOwnerDamageApplications;}

    [Serializable] public sealed class TrainingArmoredEvidence
    {
        public string target,source,pistolAttack,spearAttack,pistolItem,level="hub";
        public bool ordinaryRoute=true,initialFixture=false,reached,absorbed,movingSpearHit,restored500,exactlyOnce,complete;
        public float targetX,targetY,finalTargetX,finalTargetY,ladderX,ladderBottom,ladderTop,routeDistance;
        public float initialHealth,healthAfterPistol,healthAfterSpear,firstContactSpeed,incomingDamage,expectedDamage,appliedDamage,maximumAnchorDisplacement;
        public int routeWalkSamples,routeClimbSamples,initialReceipts,pistolReceipts,spearReceipts;
        public int initialDamageSequence,pistolDamageSequence,spearDamageSequence;
        public int pistolLaunches,pistolContacts,pistolSounds,spearSamples,movingSpearSamples,spearContacts,spearSounds,positiveFeedbacks,ownerDamageApplications,totalOwnerDamageApplications;
        public int pistolMagazineBefore,pistolMagazineAfter,framesCaptured;
    }

    // An input-only continuation of the existing training run. No actor/object state is assigned here.
    public sealed class TrainingArmoredExercise : IDisposable
    {
        public const string TargetId="training:original:uDSH7FxhgwoTe4Ab";
        private readonly PrototypeRuntime runtime;
        private readonly Action<string> fail;
        private readonly TrainingPlacement placement;
        private readonly TypeHitProtectionRule protection=new();
        private readonly HashSet<int> baseReady=new(),barrier=new();
        private readonly List<int> capturePhases=new();
        private RoomLayout room;
        private Vector2 targetPosition,previousPosition;
        private string world,actor,owner,barrierAttack;
        private int generation,lifecycle,phase=-1,serverPhase=-1,routeStage;
        private int serverOwnerApplications;
        private bool startSent,readySent,fireIssued,capturePending;
        private float phaseAt,lastSampleAt,receiptAt=-1,arrivalAt=-1;
        private int projectileAudioBefore,meleeAudioBefore;
        private bool firstSpearContact;
        private readonly HashSet<int> voiceIds=new();
        private AudioClip metalClip;
        public TrainingArmoredEvidence Evidence {get;}=new();
        public bool Started=>phase>=0;
        public bool Complete=>Evidence.complete;
        private bool Source=>runtime.Session.LocalActorId==Evidence.source;
        public string Context=>$"armoredPhase={phase}, route={routeStage}, actor={runtime.Session.LocalActor?.id}@{runtime.Session.LocalActor?.Position}, target={Evidence.target}, pistol={Evidence.pistolAttack}, spear={Evidence.spearAttack}, samples={Evidence.spearSamples}, receipts={runtime.Session.World?.Find(TargetId)?.resolvedAttacks.Count}";

        public TrainingArmoredExercise(PrototypeRuntime runtime,TrainingPlacement placement,Action<string> fail)
        {
            this.runtime=runtime;this.placement=placement;this.fail=fail;
            runtime.Session.ServerReady+=RegisterServer;
            runtime.Session.ClientReady+=RegisterClient;
            runtime.Session.AttackReceived+=Attack;
            runtime.Combat.Projectiles.ContactObserved+=ProjectileContact;
            runtime.Combat.ContactObserved+=MeleeContact;
            runtime.Combat.DamageApplied+=OwnerDamage;
            runtime.DamageFeedback.Received+=Feedback;
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<TrainingBaseReadyMessage>((connection,_)=>baseReady.Add(connection.connectionId));
            NetworkServer.RegisterHandler<TrainingArmoredReadyMessage>((connection,message)=>
            {
                try
                {
                    Require(message.phase==serverPhase,"Барьер броневой мишени относится к другой фазе.");
                    if(serverPhase>0)
                    {
                        Require(!string.IsNullOrEmpty(message.attack),"Барьер без actual attackID.");
                        if(barrierAttack==null) barrierAttack=message.attack;
                        Require(barrierAttack==message.attack,"Клиенты наблюдают разные атаки броневой мишени.");
                    }
                    Require(message.ownerDamageApplications is >=0 and <=1,"Некорректный owner damage witness.");
                    if(!barrier.Add(connection.connectionId)) return;
                    serverOwnerApplications+=message.ownerDamageApplications;
                    if(barrier.Count!=runtime.Options.ProbePeers) return;
                    if(serverPhase==2) Require(serverOwnerApplications==1,"Урон не применён ровно одним настоящим владельцем мишени.");
                    barrier.Clear();barrierAttack=null;serverPhase++;
                    NetworkServer.SendToAll(new TrainingArmoredPhaseMessage {phase=serverPhase,totalOwnerDamageApplications=serverOwnerApplications});
                    serverOwnerApplications=0;
                }
                catch(Exception error) {fail(error.Message+" "+Context);}
            });
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<TrainingArmoredBeginMessage>(message=>Guard(()=>Begin(message.source)));
            NetworkClient.RegisterHandler<TrainingArmoredPhaseMessage>(message=>Guard(()=>SetPhase(message.phase,message.totalOwnerDamageApplications)));
        }
        public bool BasePeersReady=>baseReady.Count==runtime.Options.ProbePeers;
        public void BeginForAll()
        {
            if(startSent) return;
            Require(NetworkServer.active && BasePeersReady,"Верхний этап начат до готовности всех участников прежнего обучения.");
            startSent=true;serverPhase=0;
            NetworkServer.SendToAll(new TrainingArmoredBeginMessage {source=runtime.Session.LocalActorId});
        }
        private void Begin(string source)
        {
            Require(!Started,"Повторное начало броневого этапа.");
            var self=runtime.Session.LocalActor;var target=runtime.Session.World.Find(TargetId);
            Require(self!=null && self.level=="hub" && target!=null && target.level=="hub" && target.actorType=="training-armored-dummy" &&
                !target.player && !target.down && target.health==500 && target.resolvedAttacks.Count==0 && target.damageSequence==0 &&
                placement.id==target.id && placement.type==target.actorType && Mathf.Abs(target.x-placement.x)<.00001f && Mathf.Abs(target.y-placement.y)<.00001f,
                "Нужна настоящая нетронутая верхняя броневая мишень uDSH; fixture и refill не применяются.");
            Require(runtime.Session.World.Find(source) is {player:true,connected:true},"Нет настоящего источника верхнего упражнения.");
            var definition=ActorTypes.Get(target.actorType);
            Require(definition.hitProtection==20 && definition.restoreHealthAfterHit==500 && !definition.combatAi,"Изменился заявленный контракт броневой мишени.");
            room=runtime.Geometry.Get("hub");targetPosition=target.Position;
            var candidates=room.ladders.Where(l=>l.bottom<=self.y+RoomSupport.ContactTolerance &&
                Mathf.Abs(l.top-target.y)<=RoomSupport.ContactTolerance && l.x+l.movementOffset>target.x && l.x+l.movementOffset<target.x+6).ToArray();
            Require(candidates.Length==1,"Не найдена единственная настоящая правая лестница к верхней мишени.");
            var ladder=candidates[0];var upper=new Vector2(target.x+2.55f,target.y);
            Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(self),upper) && ActorTerrainMotion.Supported(room,Anatomy.Movement(self),upper),
                $"Нет свободной верхней опоры для обычного пути: feet={upper}, ladder={ladder.x}.");
            var hull=Anatomy.Movement(self);var body=new Rect(upper.x-hull.width*.5f,upper.y,hull.width,hull.height);
            foreach(var prop in WorldProps.All(runtime.Session.World))
                if(prop.level=="hub" && prop.hasPhysicalBody && body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)))
                    throw new InvalidOperationException($"Верхняя опора перекрыта настоящим предметом: prop={prop.id}/{prop.definition}, feet={upper}, body={body}.");
            world=runtime.Session.World.worldId;actor=self.id;owner=self.owner;generation=self.generation;lifecycle=self.lifecycleSequence;
            previousPosition=self.Position;Evidence.target=target.id;Evidence.source=source;
            Evidence.targetX=target.x;Evidence.targetY=target.y;Evidence.initialHealth=target.health;
            Evidence.initialReceipts=target.resolvedAttacks.Count;Evidence.initialDamageSequence=target.damageSequence;
            Evidence.ladderX=ladder.x+ladder.movementOffset;Evidence.ladderBottom=ladder.bottom;Evidence.ladderTop=ladder.top;
            var shooter=runtime.Session.World.Find(source);
            var pistol=shooter.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
            Require(shooter.inventory.Any(i=>i.definition=="spear" && i.weaponState!=null),"В стартовом профиле нет реального копья.");
            Evidence.pistolItem=pistol.id;Evidence.pistolMagazineBefore=pistol.weaponState.rounds;
            Require(Evidence.pistolMagazineBefore>0,"Нет оставшегося настоящего патрона после прежних выстрелов.");
            metalClip=Resources.Load<AudioClip>("Original/Audio/hit-metal");
            Require(metalClip!=null && metalClip.samples>0,"Нет установленного реального hit-metal.");
            foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>()) voiceIds.Add(voice.GetInstanceID());
            phase=0;phaseAt=Time.unscaledTime;
        }
        public PlayerCommand Command()
        {
            if(!Started || Complete) return default;
            try
            {
                var self=ValidateContext();var command=new PlayerCommand {hasAim=true,aim=targetPosition+Vector2.up*.8f};
                if(phase==0)
                {
                    if(routeStage==0)
                    {
                        float dx=Evidence.ladderX-self.x;
                        if(Mathf.Abs(dx)>.055f) {command.movement.x=Mathf.Sign(dx);return command;}
                        routeStage=1;
                    }
                    if(routeStage==1)
                    {
                        if(self.y<Evidence.ladderTop-RoomSupport.ContactTolerance) {command.movement=Vector2.up;return command;}
                        routeStage=2;
                        if(self.climbing) {command.movement=Vector2.left;command.jump=true;return command;}
                    }
                    float remaining=targetPosition.x+2.55f-self.x;
                    if(Mathf.Abs(remaining)>.06f) command.movement.x=Mathf.Sign(remaining);
                    return command;
                }
                if(!Source) return command;
                if(phase==1)
                {
                    command.weapon="pistol";command.movement.y=1;
                    if(!fireIssued && self.weapon=="pistol" && self.weaponLift>.7f && runtime.Combat.ShotCooldownRemaining<=0 && Time.unscaledTime-phaseAt>.4f)
                    {fireIssued=true;command.fire=true;}
                }
                if(phase==2)
                {
                    command.weapon="spear";
                    if(!fireIssued || runtime.Combat.IsMeleeActive) command.movement.x=-1;
                    if(!fireIssued && self.weapon=="spear" && self.vx<-3 && self.x<=targetPosition.x+2 && runtime.Combat.ShotCooldownRemaining<=0)
                    {fireIssued=true;command.fire=true;}
                }
                return command;
            }
            catch(Exception error) {fail(error.Message+" "+Context);return default;}
        }
        public void Tick()
        {
            if(!Started || Complete || readySent) return;
            Guard(()=>
            {
                var self=ValidateContext();var target=runtime.Session.World.Find(TargetId);
                Require(target.Position==targetPosition && target.health==500 && !target.down && target.target==null && !target.attackBusy,
                    "Верхняя мишень сдвинулась, не восстановилась до500 или начала бой.");
                if(phase==0)
                {
                    Evidence.routeDistance+=Vector2.Distance(previousPosition,self.Position);previousPosition=self.Position;
                    if(Mathf.Abs(self.vx)>1) Evidence.routeWalkSamples++;
                    if(self.climbing && self.vy>0) Evidence.routeClimbSamples++;
                    bool arrived=routeStage==2 && Mathf.Abs(self.x-target.x-2.55f)<.13f && Mathf.Abs(self.y-target.y)<=RoomSupport.ContactTolerance &&
                        !self.climbing && ActorTerrainMotion.IsClear(room,Anatomy.Movement(self),self.Position) && ActorTerrainMotion.Supported(room,Anatomy.Movement(self),self.Position);
                    if(!arrived) {arrivalAt=-1;return;}
                    if(arrivalAt<0) arrivalAt=Time.unscaledTime;
                    if(Time.unscaledTime-arrivalAt<.5f) return;
                    Require(Evidence.routeWalkSamples>0 && Evidence.routeClimbSamples>0 && Evidence.routeDistance>30,"Нет наблюдений собственного обычного пути/подъёма.");
                    Evidence.reached=true;QueueCapture(22);return;
                }
                if(phase==1)
                {
                    Require(Evidence.pistolLaunches<=1 && Evidence.pistolContacts<=1 && Evidence.positiveFeedbacks==0 && Evidence.ownerDamageApplications==0 &&
                        target.damageSequence==Evidence.initialDamageSequence && target.resolvedAttacks.Count<=1,"Пистолет дал положительный/повторный урон броневой мишени.");
                    if(Evidence.pistolAttack==null || Evidence.pistolContacts!=1 || !target.resolvedAttacks.Contains(Evidence.pistolAttack)) return;
                    Require(target.resolvedAttacks.Count==1 && Evidence.pistolLaunches==1 && Evidence.pistolSounds==1,"Пистолет не оставил один launch, одну квитанцию и один настоящий металлический звук.");
                    if(receiptAt<0) receiptAt=Time.unscaledTime;
                    if(Time.unscaledTime-receiptAt<.6f) return;
                    var shooter=runtime.Session.World.Find(Evidence.source);var pistol=InventoryStacks.Find(shooter,Evidence.pistolItem);
                    Evidence.pistolMagazineAfter=pistol?.weaponState?.rounds??-1;
                    if(Evidence.pistolMagazineAfter!=Evidence.pistolMagazineBefore-1) return;
                    Evidence.absorbed=true;Evidence.pistolReceipts=target.resolvedAttacks.Count;Evidence.pistolDamageSequence=target.damageSequence;Evidence.healthAfterPistol=target.health;
                    QueueCapture(23);return;
                }
                if(phase==2)
                {
                    Require(Evidence.positiveFeedbacks<=1 && Evidence.ownerDamageApplications<=1 && target.damageSequence<=Evidence.initialDamageSequence+1 &&
                        target.resolvedAttacks.Count<=2,"Один настоящий swing дал повторный положительный урон или постороннюю квитанцию.");
                    if(Evidence.spearAttack==null || !target.resolvedAttacks.Contains(Evidence.spearAttack) || Evidence.positiveFeedbacks!=1 ||
                        !firstSpearContact || runtime.Combat.IsMeleeActive || Time.unscaledTime-lastSampleAt<.8f) return;
                    Require(target.resolvedAttacks.Count==2 && target.resolvedAttacks.Contains(Evidence.pistolAttack) &&
                        target.damageSequence==Evidence.initialDamageSequence+1 && Evidence.spearSamples>=2 && Evidence.movingSpearSamples>=2 &&
                        Evidence.maximumAnchorDisplacement>0 && Evidence.expectedDamage>0 && Mathf.Abs(Evidence.appliedDamage-Evidence.expectedDamage)<.001f && Evidence.spearSounds==1,
                        "Не подтверждены moving sampled spear, единственный положительный урон после защиты или один hit-metal.");
                    Evidence.movingSpearHit=true;Evidence.restored500=target.health==500;Evidence.exactlyOnce=true;
                    Evidence.spearReceipts=target.resolvedAttacks.Count;Evidence.spearDamageSequence=target.damageSequence;Evidence.healthAfterSpear=target.health;
                    Evidence.finalTargetX=target.x;Evidence.finalTargetY=target.y;QueueCapture(24);
                }
            });
        }
        private void QueueCapture(int capturePhase)
        {
            if(capturePending) return;
            if(runtime.Options.Smoke.Contains("-frames-")) {capturePending=true;capturePhases.Add(capturePhase);}
            else SendReady();
        }
        public void LateUpdate()
        {
            if(!capturePending) return;
            Guard(()=>
            {
                int frame=capturePhases[capturePhases.Count-1];
                NetworkSceneRecord.Save(runtime,frame);Evidence.framesCaptured++;
                capturePending=false;SendReady();
            });
        }
        private void SendReady()
        {
            if(readySent) return;readySent=true;
            NetworkClient.Send(new TrainingArmoredReadyMessage {phase=phase,attack=phase==1?Evidence.pistolAttack:phase==2?Evidence.spearAttack:null,ownerDamageApplications=Evidence.ownerDamageApplications});
        }
        private void SetPhase(int next,int ownerApplications)
        {
            Require(next==phase+1 && readySent,"Верхний этап сменился до независимого наблюдения/кадра.");
            phase=next;phaseAt=Time.unscaledTime;receiptAt=-1;readySent=false;fireIssued=false;
            projectileAudioBefore=runtime.ImpactAudio.Played;meleeAudioBefore=runtime.MeleeAudio.ActorPlayed;
            if(next==3)
            {
                Require(ownerApplications==1,"Нет общего подтверждения единственного owner apply.");
                Evidence.totalOwnerDamageApplications=ownerApplications;Evidence.complete=true;
            }
        }
        private ActorState ValidateContext()
        {
            var self=runtime.Session.LocalActor;
            Require(self!=null && runtime.Session.World.worldId==world && self.id==actor && self.owner==owner && self.generation==generation &&
                self.lifecycleSequence==lifecycle && self.level=="hub" && !self.down && self.health>0 && !runtime.Session.Travel.Pending && !runtime.Session.Pause.IsPaused("hub"),
                "Обычный верхний этап потерял мир, владельца, lifecycle, уровень или управление.");
            return self;
        }
        private void Attack(AttackEvent attack)
        {
            if(!Started || Complete || attack.attacker!=Evidence.source) return;
            Guard(()=>
            {
                Require(attack.level=="hub" && !string.IsNullOrEmpty(attack.id),"Атака верхней мишени потеряла level/ID.");
                if(phase==1 && attack.weapon=="pistol")
                {
                    Require(Evidence.pistolAttack==null || Evidence.pistolAttack==attack.id,"Пистолет запущен больше одного раза.");
                    Evidence.pistolAttack=attack.id;Evidence.pistolLaunches++;
                }
                if(phase==2 && attack.weapon=="spear")
                {
                    Require(MeleeTerrainOcclusion.Validate(attack,out _),"Нет конечного реального meleeAnchor.");
                    RememberSpear(attack.id);Evidence.spearSamples++;lastSampleAt=Time.unscaledTime;
                    float displacement=Vector2.Distance(attack.meleeAnchor.Start,attack.meleeAnchor.End);
                    if(displacement>.00001f) Evidence.movingSpearSamples++;
                    Evidence.maximumAnchorDisplacement=Mathf.Max(Evidence.maximumAnchorDisplacement,displacement);
                }
            });
        }
        private void ProjectileContact(ProjectileContact contact)
        {
            if(phase!=1 || contact.target!=TargetId || contact.weapon!="pistol") return;
            Guard(()=>
            {
                Require(contact.projectile==Evidence.pistolAttack && contact.targetType=="training-armored-dummy","Металлический контакт не принадлежит своему реальному выстрелу.");
                Evidence.pistolContacts++;Evidence.pistolSounds=runtime.ImpactAudio.Played-projectileAudioBefore;
                Require(NewMetalVoice(),"Поглощённый контакт не создал AudioSource с настоящим hit-metal.");
            });
        }
        private void MeleeContact(AttackEvent attack,ActorState target)
        {
            if(phase!=2 || target.id!=TargetId || attack.weapon!="spear" || attack.attacker!=Evidence.source) return;
            Guard(()=>
            {
                Require(MeleeTerrainOcclusion.Validate(attack,out _),"Melee contact без actual конечной опоры.");
                // Combat consumes the event before the later diagnostic AttackReceived subscriber.
                RememberSpear(attack.id);
                Evidence.spearContacts++;
                if(firstSpearContact) return;firstSpearContact=true;
                Evidence.firstContactSpeed=attack.speed;Evidence.incomingDamage=runtime.Session.Rules.Damage.Calculate(attack.weapon,attack.speed);
                Evidence.expectedDamage=protection.Resolve(target,attack,Evidence.incomingDamage);
                Evidence.spearSounds=runtime.MeleeAudio.ActorPlayed-meleeAudioBefore;
                Require(NewMetalVoice(),"Настоящий spear contact не создал AudioSource hit-metal.");
            });
        }
        private bool NewMetalVoice()
        {
            bool found=false;
            foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>())
                if(voiceIds.Add(voice.GetInstanceID()) && voice.clip==metalClip && voice.volume>0) found=true;
            return found;
        }
        private void OwnerDamage(DamageResult result)
        {
            if(!Started || result.actor!=TargetId) return;
            Evidence.ownerDamageApplications++;
            Guard(()=>{Require(phase==2,"Owner применил неожиданный положительный урон верхней мишени.");RememberSpear(result.attack);});
        }
        private void Feedback(DamageResult result)
        {
            if(!Started || result.actor!=TargetId) return;
            Evidence.positiveFeedbacks++;Evidence.appliedDamage=result.amount;
            Guard(()=>
            {
                Require(phase==2,"Получен неожиданный/поглощённый положительный damage feedback.");
                // Expedited cosmetic feedback can precede the reliable sampled attack on an observer.
                // Completion still requires the same ID's actual finite anchors, contacts and durable actor receipt.
                RememberSpear(result.attack);
            });
        }
        private void RememberSpear(string id)
        {
            Require(!string.IsNullOrEmpty(id) && id!=Evidence.pistolAttack && (Evidence.spearAttack==null || Evidence.spearAttack==id),"Наблюдаются разные IDs положительного swing / повтор ID пистолета.");
            Evidence.spearAttack=id;
        }
        private void Guard(Action action) {try {action();}catch(Exception error) {fail(error.Message+" "+Context);}}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
        public void Dispose()
        {
            runtime.Session.ServerReady-=RegisterServer;runtime.Session.ClientReady-=RegisterClient;runtime.Session.AttackReceived-=Attack;
            runtime.Combat.Projectiles.ContactObserved-=ProjectileContact;runtime.Combat.ContactObserved-=MeleeContact;
            runtime.Combat.DamageApplied-=OwnerDamage;runtime.DamageFeedback.Received-=Feedback;
        }
    }
}
