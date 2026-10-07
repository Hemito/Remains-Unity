using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Runtime
{
    public struct TrainingLayerPhaseMessage : NetworkMessage {public int phase;}
    public struct TrainingLayerReadyMessage : NetworkMessage
    {
        public int phase,frame,actorOrder,targetOrder,renderers,firstClimbSamples,rightClimbSamples;
        public bool overlap,sharedTable,visible;
        public string actor,world;
    }
    public struct TrainingLayerHealingMessage : NetworkMessage {public string transaction;}
    public struct TrainingLayerFinishMessage : NetworkMessage {public bool success;public string detail;}

    // Opt-in observation of the normal fresh hub. All changes use ordinary player commands.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkTrainingLayerProbe : MonoBehaviour
    {
        public const string TargetId="training:original:uDSH7FxhgwoTe4Ab";
        public const string ShooterId="player:session-host",VictimId="player:session-client-1";
        private PrototypeRuntime runtime;
        private RoomLayout room;
        private RoomLadder firstLadder,rightLadder;
        private readonly HashSet<int> barrier=new();
        private readonly HashSet<string> attacks=new();
        private readonly List<Witness> proofs=new();
        private View victimView,shooterView,targetView;
        private Vector2 targetAnchor,previousPosition;
        private string world,owner,pistolItem,healingTransaction;
        private int generation,phase,serverPhase,routeStage,firstClimbSamples,rightClimbSamples,walkSamples,initialRounds;
        private int shotRequests,pendingDamageSequence,downLifecycle=-1,successfulHealingReplies;
        private float deadline,settledAt=-1,shotAt,routeDistance,maximumRevivalProgress;
        private bool prepared,readySent,capturing,finished,shotPending,firstExitIssued,rightExitIssued;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Victim=>runtime.Session.World?.Find(VictimId);
        private ActorState Shooter=>runtime.Session.World?.Find(ShooterId);
        private ActorState Target=>runtime.Session.World?.Find(TargetId);
        private float DestinationX=>targetAnchor.x+(Host?2.2f:.65f);

        public void Initialize(PrototypeRuntime value)
        {
            if(value==null || value.Options.ProbePeers!=2 || value.Options.Smoke==null ||
                !value.Options.Smoke.StartsWith("training-layers-frames-",StringComparison.Ordinal))
                throw new InvalidOperationException("Проверка слоёв требует training-layers-frames и двух участников.");
            runtime=value;deadline=Time.unscaledTime+150;runtime.CommandSource=Command;
            AudioListener.volume=0; // Device output only; ordinary combat/audio still runs.
            runtime.Session.ServerReady+=RegisterServer;
            runtime.Session.ClientReady+=RegisterClient;
            runtime.Session.AttackReceived+=Attack;
            runtime.Session.HealingFinished+=HealingFinished;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<TrainingLayerPhaseMessage>(message=>
            {
                if(message.phase!=phase+1) {Fail($"Нарушен порядок фаз: before={phase}, incoming={message.phase}.");return;}
                phase=message.phase;readySent=false;settledAt=-1;
                Debug.Log($"Слои верхней мишени: phase={phase}, actor={runtime.Session.LocalActorId}.");
            });
            NetworkClient.RegisterHandler<TrainingLayerHealingMessage>(message=>healingTransaction=message.transaction);
            NetworkClient.RegisterHandler<TrainingLayerFinishMessage>(message=>Finish(message.success,message.detail));
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<TrainingLayerReadyMessage>((connection,message)=>
            {
                try
                {
                    var actor=runtime.Session.ConnectedActor(connection);
                    Require(actor!=null && actor.id==message.actor && actor.id is ShooterId or VictimId,
                        "Барьер получен от другого персонажа.");
                    Require(message.phase==serverPhase && message.world==runtime.Session.ServerWorld.worldId,
                        "Барьер относится к другой фазе или миру.");
                    Require(message.firstClimbSamples>0 && message.rightClimbSamples>0,"Нет обоих обычных подъёмов.");
                    if(message.phase>0)
                    {
                        var category=message.phase==2?WorldVisualCategory.Body:WorldVisualCategory.Player;
                        Require(message.frame>0 && message.overlap && message.sharedTable && message.visible && message.renderers>1 &&
                            WorldVisualOrderPolicy.Contains(category,message.actorOrder) &&
                            WorldVisualOrderPolicy.Contains(WorldVisualCategory.Scenery,message.targetOrder) && message.actorOrder>message.targetOrder,
                            "Участник не подтвердил фактические слои, рисунки и перекрытие в кадре.");
                    }
                    if(!barrier.Add(connection.connectionId) || barrier.Count!=2) return;
                    barrier.Clear();
                    if(serverPhase==3)
                        NetworkServer.SendToAll(new TrainingLayerFinishMessage {success=true,
                            detail="Оба участника подтвердили стоящего игрока, тело и поднятие над настоящей верхней мишенью."});
                    else {serverPhase++;NetworkServer.SendToAll(new TrainingLayerPhaseMessage {phase=serverPhase});}
                }
                catch(Exception error) {Fail(error.Message);}
            });
            NetworkServer.RegisterHandler<TrainingLayerHealingMessage>((connection,message)=>
            {
                if(runtime.Session.ConnectedActor(connection)?.id!=ShooterId || serverPhase!=3 ||
                    string.IsNullOrEmpty(message.transaction) || !runtime.Session.ServerWorld.completedTransactions.Contains(message.transaction))
                {Fail("Поднятие не связано с подтверждённой транзакцией стрелка.");return;}
                NetworkServer.SendToAll(message);
            });
            NetworkServer.RegisterHandler<TrainingLayerFinishMessage>((connection,message)=>
            {
                if(runtime.Session.ConnectedActor(connection)!=null && !message.success)
                    NetworkServer.SendToAll(message);
            });
        }
        private bool ReadyLevel()
        {
            var self=runtime.Session.LocalActor;
            return self!=null && self.connected && self.level=="hub" && self.owner==runtime.Session.LocalOwner &&
                NetworkClient.isConnected && runtime.LevelPresentationReady && !runtime.IsLevelPreparing &&
                !runtime.Session.Travel.Pending && !runtime.Session.Pause.IsPaused("hub") && runtime.Hud?.BlocksGameplayInput!=true;
        }
        private void Prepare()
        {
            var self=runtime.Session.LocalActor;var target=Target;
            Require(self.id==(Host?ShooterId:VictimId) && self.health==100 && !self.down &&
                Shooter is {connected:true,player:true,health:100} && Victim is {connected:true,player:true,health:100} &&
                runtime.Session.World.actors.Count(a=>a.player && a.connected)==2,
                "Нужны два свежих обычных персонажа без изменений стартового здоровья.");
            Require(target is {actorType:"training-armored-dummy",health:500,down:false} && target.id==TargetId && target.level=="hub" &&
                target.resolvedAttacks.Count==0 && target.damageSequence==0,"Нет нетронутой настоящей верхней мишени uDSH.");
            Require(runtime.Session.World.friendlyFire && runtime.Session.World.friendlyFireDecision==FriendlyFireDecision.Victim,
                "Нужен обычный дружественный огонь с решением жертвы.");
            world=runtime.Session.World.worldId;owner=self.owner;generation=self.generation;targetAnchor=target.Position;
            room=runtime.Geometry.Get("hub");previousPosition=self.Position;
            firstLadder=room.ladders.Where(l=>l.x<38 && l.bottom<1 && l.top>7.2f).OrderByDescending(l=>l.x).First();
            rightLadder=room.ladders.Single(l=>Mathf.Abs(l.top-target.y)<.0001f && l.bottom<target.y &&
                l.x+l.movementOffset>target.x && l.x+l.movementOffset<target.x+6);
            foreach(var x in new[]{target.x+.65f,target.x+2.2f})
            {
                var position=new Vector2(x,target.y);
                Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(self),position) &&
                    ActorTerrainMotion.Supported(room,Anatomy.Movement(self),position),$"Нет реальной верхней опоры: feet={position}.");
                var hull=Anatomy.Movement(self);var bounds=new Rect(x-hull.width*.5f,target.y,hull.width,hull.height);
                foreach(var prop in WorldProps.All(runtime.Session.World))
                    Require(prop.level!="hub" || !prop.hasPhysicalBody || !bounds.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),
                        $"Верхнее место перекрыто предметом {prop.id}: feet={position}.");
            }
            var pistol=Shooter.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
            pistolItem=pistol.id;initialRounds=pistol.weaponState.rounds;
            Require(initialRounds==12 && InventoryJournal.Count(Shooter,"heal")==5,"Изменилось настоящее стартовое оснащение 12 патронов / 5 аптечек.");
            Require(!ShotObstacles.FirstContact(room,new Vector2(target.x+2.2f,target.y+.72f),new Vector2(target.x+.65f,target.y+.72f)).HasValue,
                "Линия торса между верхними местами перекрыта настоящим terrain.");
            prepared=true;
            Debug.Log($"Слои мишени: world={world}, target={target.id}@{target.Position}, first={firstLadder.x+firstLadder.movementOffset}, right={rightLadder.x+rightLadder.movementOffset}, destination={DestinationX},{target.y}.");
        }
        private PlayerCommand Command()
        {
            if(finished || !prepared || !ReadyLevel()) return default;
            try
            {
                var self=runtime.Session.LocalActor;
                if(phase==0) return Route(self);
                if(!Host) return default;
                if(phase==2 && !Victim.down)
                {
                    Require(Anatomy.TryRegion(Victim,"torso",out var aim,out _),"Нет настоящей области торса жертвы.");
                    var command=new PlayerCommand {weaponItem=Shooter.equippedWeaponItem==pistolItem?null:pistolItem,hasAim=true,aim=aim};
                    if(shotPending && Victim.damageSequence>pendingDamageSequence) shotPending=false;
                    if(shotPending)
                    {
                        if(Time.unscaledTime-shotAt>4) Fail("Выстрел не дал подтверждённого урона жертве: "+Context());
                        return command;
                    }
                    if(runtime.Combat.ShotCooldownRemaining<=0 && Shooter.weapon=="pistol")
                    {
                        Require(shotRequests<initialRounds,"Стартового магазина не хватило для обычного нокдауна.");
                        Require(!ShotObstacles.FirstContact(room,WeaponPose.RangedAnchor(Shooter),WeaponPose.Muzzle(Shooter)).HasValue,
                            "Настоящий ствол перекрыт перед выстрелом.");
                        shotPending=true;pendingDamageSequence=Victim.damageSequence;shotAt=Time.unscaledTime;shotRequests++;command.fire=true;
                    }
                    return command;
                }
                if(phase==3 && Victim.down)
                {
                    if(Vector2.Distance(self.Position,Victim.Position)>1.6f)
                    {
                        var approach=new Vector2(Victim.x+1.35f,targetAnchor.y);
                        Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(self),approach) &&
                            ActorTerrainMotion.Supported(room,Anatomy.Movement(self),approach),
                            $"Нет обычной верхней опоры для подхода к настоящему телу: feet={approach}.");
                        return Walk(approach.x,self);
                    }
                    // Release movement before starting the normal timed interaction.
                    if(Mathf.Abs(self.vx)>.01f || !Grounded(self)) return default;
                    return new PlayerCommand {hasAim=true,aim=InventorySource.BodyBounds(Victim).center,revive=true};
                }
                return default;
            }
            catch(Exception error) {Fail(error.Message);return default;}
        }
        private PlayerCommand Route(ActorState self)
        {
            float firstAxis=firstLadder.x+firstLadder.movementOffset,rightAxis=rightLadder.x+rightLadder.movementOffset;
            if(routeStage==0)
            {
                // Normal fresh spawn is already on the lower hub floor. No fixture relocation.
                if(Mathf.Abs(self.x-firstAxis)>.075f) return Walk(firstAxis,self);
                routeStage=1;
            }
            if(routeStage==1)
            {
                if(self.y<7.22f) return new PlayerCommand {movement=Vector2.up};
                routeStage=2;firstExitIssued=true;
                return new PlayerCommand {movement=Vector2.right,jump=true};
            }
            if(routeStage==2)
            {
                if(self.x<26 || !Grounded(self)) return new PlayerCommand {movement=Vector2.right};
                Require(Mathf.Abs(self.y-7.2f)<.0001f,"Первый обычный выход не попал на средний ярус.");routeStage=3;
            }
            if(routeStage==3)
            {
                if(Mathf.Abs(self.x-rightAxis)>.075f) return Walk(rightAxis,self);
                if(!Grounded(self)) return default;
                routeStage=4;
            }
            if(routeStage==4)
            {
                if(self.y<rightLadder.top-.001f) return new PlayerCommand {movement=Vector2.up};
                routeStage=5;
                if(self.climbing) {rightExitIssued=true;return new PlayerCommand {movement=Vector2.left,jump=true};}
            }
            return Walk(DestinationX,self);
        }
        private static PlayerCommand Walk(float x,ActorState self)=>new()
        {movement=Mathf.Abs(x-self.x)>.075f?Vector2.right*Mathf.Sign(x-self.x):Vector2.zero};
        private bool Grounded(ActorState actor)=>!actor.climbing && Mathf.Abs(actor.vy)<.001f &&
            ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
        private bool SettledUpper(ActorState actor)=>actor!=null && Math.Abs(actor.y-targetAnchor.y)<.001f &&
            Math.Abs(actor.vx)<.01f && Grounded(actor);
        private bool AtUpper(ActorState actor,float x)=>SettledUpper(actor) && Math.Abs(actor.x-x)<.14f;
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Fail("Истёк срок проверки: "+Context());return;}
            if(!ReadyLevel() || Shooter==null || Victim==null || Target==null) return;
            try
            {
                if(!prepared) Prepare();
                var self=runtime.Session.LocalActor;
                Require(runtime.Session.World.worldId==world && self.owner==owner && self.generation==generation &&
                    Shooter.level=="hub" && Victim.level=="hub" && Shooter.connected && Victim.connected,
                    "Проверка сменила мир, владельца, поколение или область.");
                Require(Target.health==500 && !Target.down && Target.damageSequence==0 && Target.resolvedAttacks.Count==0 &&
                    Vector2.Distance(Target.Position,targetAnchor)<.00001f,"Верхняя мишень была изменена игровым действием.");
                if(phase==0)
                {
                    routeDistance+=Vector2.Distance(previousPosition,self.Position);previousPosition=self.Position;
                    if(Math.Abs(self.vx)>.1f) walkSamples++;
                    if(self.climbing && routeStage<=2) firstClimbSamples++;
                    if(self.climbing && routeStage>=4) rightClimbSamples++;
                    if(!AtUpper(self,DestinationX) || routeStage!=5) {settledAt=-1;return;}
                    if(settledAt<0) settledAt=Time.unscaledTime;
                    if(!readySent && Time.unscaledTime-settledAt>.4f)
                    {
                        Require(firstExitIssued && firstClimbSamples>0 && rightClimbSamples>0 && walkSamples>0 && routeDistance>30,
                            "Не наблюдался настоящий путь по двум лестницам.");
                        readySent=true;NetworkClient.Send(new TrainingLayerReadyMessage {phase=0,actor=self.id,world=world,
                            firstClimbSamples=firstClimbSamples,rightClimbSamples=rightClimbSamples});
                    }
                }
                if(phase==3 && Host) maximumRevivalProgress=Mathf.Max(maximumRevivalProgress,runtime.Revival.Progress);
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void LateUpdate()
        {
            if(finished || !prepared || phase==0 || readySent || capturing || !ReadyLevel()) return;
            bool down=phase==2;
            if(!(phase==3?SettledUpper(Shooter):AtUpper(Shooter,targetAnchor.x+2.2f)) ||
                !(phase==1?AtUpper(Victim,targetAnchor.x+.65f):SettledUpper(Victim)) ||
                Victim.down!=down || down && Victim.health>0 || !down && Victim.health!=(phase==1?100:35)) return;
            if(phase==3 && (string.IsNullOrEmpty(healingTransaction) ||
                !runtime.Session.World.completedTransactions.Contains(healingTransaction))) return;
            if(phase>=2)
            {
                // Attack events, inventory snapshots and victim-owned HP can arrive separately.
                var item=Shooter.inventory.Find(i=>i.id==pistolItem);
                if(item?.weaponState==null || attacks.Count==0 || initialRounds-item.weaponState.rounds!=attacks.Count ||
                    !attacks.All(id=>Victim.resolvedAttacks.Contains(id))) return;
                if(phase==3 && (InventoryJournal.Count(Shooter,"heal")!=4 || Host && successfulHealingReplies!=1)) return;
            }
            if(settledAt<0) settledAt=Time.unscaledTime;
            if(Time.unscaledTime-settledAt<.4f) return;
            capturing=true;StartCoroutine(Capture(phase));
        }
        private IEnumerator Capture(int requestedPhase)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                Require(!finished && phase==requestedPhase && ReadyLevel(),"Фаза изменилась до фактического кадра.");
                victimView??=View.Capture(runtime,VictimId);shooterView??=View.Capture(runtime,ShooterId);targetView??=View.Capture(runtime,TargetId);
                var witness=Measure(requestedPhase);
                NetworkSceneRecord.Save(runtime,requestedPhase);
                witness.scene=runtime.Options.Report+$".phase-{requestedPhase}.scene.json";
                witness.png=runtime.Options.Report+$".phase-{requestedPhase}.live.png";
                Require(File.Exists(witness.png) && new FileInfo(witness.png).Length>1000 && File.Exists(witness.scene),"Не сохранён настоящий PNG/scene кадр.");
                File.WriteAllText(runtime.Options.Report+$".phase-{requestedPhase}.layers.json",StateJson.ToJson(witness,true));
                proofs.Add(witness);readySent=true;
                NetworkClient.Send(new TrainingLayerReadyMessage {phase=phase,frame=witness.frame,actor=runtime.Session.LocalActorId,world=world,
                    actorOrder=witness.victim.order,targetOrder=witness.target.order,renderers=witness.victim.renderers+witness.target.renderers,
                    overlap=witness.overlap,sharedTable=witness.sharedTable,visible=witness.visible,
                    firstClimbSamples=firstClimbSamples,rightClimbSamples=rightClimbSamples});
                Debug.Log($"Слои подтверждены: observer={runtime.Session.LocalActorId}, phase={phase}, victim={witness.victim.category}/{witness.victim.order}, target={witness.target.category}/{witness.target.order}, overlap={witness.overlapBounds}, frame={witness.frame}.");
            }
            catch(Exception error) {Fail(error.Message);}
            capturing=false;
        }
        private Witness Measure(int requestedPhase)
        {
            Require(Screen.width==1920 && Screen.height==1080,"Проверка требует настоящий viewport 1920×1080.");
            Require((requestedPhase==3?SettledUpper(Shooter):AtUpper(Shooter,targetAnchor.x+2.2f)) &&
                (requestedPhase==1?AtUpper(Victim,targetAnchor.x+.65f):SettledUpper(Victim)),"Игроки не стоят на настоящей верхней опоре.");
            Require(Victim.down==(requestedPhase==2) && Victim.health==(requestedPhase==1?100:requestedPhase==2?0:35),"Поза/HP не соответствует игровому этапу.");
            Require(Shooter.health==100 && !Shooter.down,"Стрелок получил непредусмотренный урон.");
            Visual victim=victimView.Measure(runtime.WorldCamera),shooter=shooterView.Measure(runtime.WorldCamera),target=targetView.Measure(runtime.WorldCamera);
            var expected=requestedPhase==2?WorldVisualCategory.Body:WorldVisualCategory.Player;
            Require(victim.drawnClip==ActorPose.Clip(Victim) && victim.drawnClip==(requestedPhase==2?"down":"idle"),
                "Фактически нарисованный клип жертвы не соответствует стоя/тело/поднятию.");
            Require(victim.category==expected.ToString() && shooter.category==WorldVisualCategory.Player.ToString() && target.category==WorldVisualCategory.Scenery.ToString(),
                "Общая таблица не классифицирует фактические player/body/armored scenery.");
            bool shared=ReferenceEquals(victimView.group.Depth.Orders,targetView.group.Depth.Orders) &&
                ReferenceEquals(shooterView.group.Depth.Orders,targetView.group.Depth.Orders) && targetView.group.Depth.Orders.WorldId==world;
            Require(shared && victim.order>target.order && shooter.order>target.order,"Изображения не используют одну таблицу или мишень перекрывает игрока/тело.");
            Rect overlap=Rect.MinMaxRect(Mathf.Max(victim.bounds.x,target.bounds.x),Mathf.Max(victim.bounds.y,target.bounds.y),
                Mathf.Min(victim.bounds.x+victim.bounds.width,target.bounds.x+target.bounds.width),
                Mathf.Min(victim.bounds.y+victim.bounds.height,target.bounds.y+target.bounds.height));
            Require(overlap.width>.001f && overlap.height>.001f,"Нет фактического перекрытия рисунка игрока/тела с настоящей мишенью.");
            int rounds=Shooter.inventory.Single(i=>i.id==pistolItem).weaponState.rounds;
            if(requestedPhase==1) Require(rounds==initialRounds && attacks.Count==0 && InventoryJournal.Count(Shooter,"heal")==5,"Стартовый кадр не предшествует выстрелам.");
            else Require(attacks.Count>0 && initialRounds-rounds==attacks.Count && Victim.damageSequence>0 &&
                attacks.All(id=>Victim.resolvedAttacks.Contains(id)),"Нет точного расхода патронов и игровых receipt урона жертве.");
            if(requestedPhase==2) downLifecycle=Victim.lifecycleSequence;
            if(requestedPhase==3) Require(downLifecycle>=0 && Victim.lifecycleSequence==downLifecycle+1 &&
                InventoryJournal.Count(Shooter,"heal")==4 && runtime.Session.World.completedTransactions.Contains(healingTransaction) &&
                (!Host || successfulHealingReplies==1 && maximumRevivalProgress>0),"Поднятие не подтверждено одной обычной аптечкой/таймером/транзакцией.");
            return new Witness {phase=requestedPhase,label=requestedPhase==1?"standing":requestedPhase==2?"down-body":"risen",frame=Time.frameCount,
                width=Screen.width,height=Screen.height,world=world,observer=runtime.Session.LocalActorId,victim=victim,shooter=shooter,target=target,
                overlap=true,overlapBounds=new RectangleEvidence(overlap.x,overlap.y,overlap.width,overlap.height),
                sharedTable=shared,visible=true,victimHealth=Victim.health,victimDown=Victim.down,
                victimPose=ActorPose.Clip(Victim),victimLifecycle=Victim.lifecycleSequence,rounds=rounds,heals=InventoryJournal.Count(Shooter,"heal"),
                healingTransaction=healingTransaction,attacks=attacks.OrderBy(id=>id,StringComparer.Ordinal).ToArray(),revivalProgressMaximum=maximumRevivalProgress};
        }
        private void Attack(AttackEvent attack)
        {
            if(attack.attacker!=ShooterId) return;
            if(phase!=2 || attack.weapon!="pistol") {Fail("Неожиданная атака стрелка: "+attack.weapon);return;}
            attacks.Add(attack.id);
        }
        private void HealingFinished(string transaction,bool success)
        {
            if(!Host || finished || phase!=3) return;
            if(!success) {Fail("Обычное timed E поднятие отклонено: "+transaction);return;}
            successfulHealingReplies++;
            if(successfulHealingReplies!=1) {Fail("Повторное подтверждение поднятия.");return;}
            NetworkClient.Send(new TrainingLayerHealingMessage {transaction=transaction});
        }
        private string Context()=>$"phase={phase}, route={routeStage}, self={runtime.Session.LocalActor?.Position}, victim={Victim?.Position}/{Victim?.health}/{Victim?.down}, shots={shotRequests}/{attacks.Count}, progress={runtime.Revival.Progress}, ready={readySent}";
        private void Fail(string detail)
        {
            if(finished) return;detail+=" "+Context();Debug.LogError("Проверка слоёв мишени: "+detail,this);
            if(NetworkClient.isConnected) NetworkClient.Send(new TrainingLayerFinishMessage {success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && (proofs.Count!=3 || !proofs.Select(p=>p.phase).SequenceEqual(new[]{1,2,3})))
            {success=false;detail="Нет полного локального набора стоя/тело/поднятие.";}
            try {runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();}
            catch(Exception error) {success=false;detail+=" Сохранение: "+error.Message;}
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,world=world,observer=runtime.Session.LocalActorId,
                target=TargetId,shooter=ShooterId,victim=VictimId,ordinaryInputs=true,initialFixture=false,batchMuted=AudioListener.volume==0,
                firstLadderX=firstLadder?.x+firstLadder?.movementOffset??0,rightLadderX=rightLadder?.x+rightLadder?.movementOffset??0,
                firstClimbSamples=firstClimbSamples,rightClimbSamples=rightClimbSamples,walkSamples=walkSamples,routeDistance=routeDistance,
                targetAnchor=targetAnchor,initialRounds=initialRounds,shotRequests=shotRequests,successfulHealingReplies=successfulHealingReplies,
                firstExitIssued=firstExitIssued,rightExitIssued=rightExitIssued,proofs=proofs.ToArray(),context=Context()},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1.5f);Application.Quit(success?0:2);}
        private static void Require(bool condition,string detail) {if(!condition) throw new InvalidOperationException(detail);}
        private void OnDestroy()
        {
            if(runtime==null) return;runtime.Session.ServerReady-=RegisterServer;runtime.Session.ClientReady-=RegisterClient;
            runtime.Session.AttackReceived-=Attack;runtime.Session.HealingFinished-=HealingFinished;
        }
        private sealed class View
        {
            public string id;public ActorVisualGroup group;private ActorVisual visual;
            public static View Capture(PrototypeRuntime runtime,string id)
            {
                var visual=runtime.FindActorVisual(id);Require(visual!=null,$"Нет настоящего изображения {id}.");
                var group=visual.GetComponentInParent<ActorVisualGroup>();
                Require(group!=null && group.ActorId==id && group.Depth!=null && group.Depth.Id==id && group.Depth.Kind==WorldVisualKind.Actor,
                    $"Нет внешней привязки слоя {id}.");
                return new View {id=id,group=group,visual=visual};
            }
            public Visual Measure(Camera camera)
            {
                Require(camera!=null && group.Sorting.enabled && group.gameObject.activeInHierarchy,$"Нет активной камеры/группы {id}.");
                bool any=false;Bounds bounds=default;int count=0;
                // Explicit capture only: a new pose can grow the pony renderer pool.
                foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    Require(ReferenceEquals(RenderSorting.FindOwner(renderer),group.Sorting),$"Рендерер {renderer.name} обходит внешний слой {id}.");
                    Require((camera.cullingMask & 1<<renderer.gameObject.layer)!=0,$"Камера не рисует {id}/{renderer.name}.");
                    if(!any) {bounds=renderer.bounds;any=true;} else bounds.Encapsulate(renderer.bounds);count++;
                }
                Require(any && bounds.size.x>0 && bounds.size.y>0,$"Нет непустых actual renderer bounds {id}.");
                foreach(var point in new[]{new Vector3(bounds.min.x,bounds.min.y,bounds.center.z),new Vector3(bounds.max.x,bounds.max.y,bounds.center.z)})
                {
                    var v=camera.WorldToViewportPoint(point);
                    Require(v.x>=0 && v.x<=1 && v.y>=0 && v.y<=1 && v.z>=camera.nearClipPlane && v.z<=camera.farClipPlane,
                        $"Рисунок {id} вне кадра: viewport={v}.");
                }
                var category=group.Depth.Category;int order=group.Depth.Orders.GetOrder(WorldVisualKind.Actor,id);
                Require(group.Order==order && group.Sorting.sortingLayerID==0 && WorldVisualOrderPolicy.Contains(category,order),$"Фактическая внешняя группа не совпала с общей таблицей {id}.");
                var pony=visual as PonyVisual;
                return new Visual {id=id,groupInstance=group.Sorting.GetInstanceID(),order=order,category=category.ToString(),renderers=count,
                    drawnClip=pony?.DrawnClip,drawnFrame=pony?.DrawnFrame??-1,
                    sortingLayer=group.Sorting.sortingLayerID,bounds=new RectangleEvidence(bounds.min.x,bounds.min.y,bounds.size.x,bounds.size.y)};
            }
        }
        // StateJson stores public fields; Unity Rect exposes properties, so it
        // must not be used as a persisted evidence payload.
        [Serializable] public struct RectangleEvidence
        {
            public float x,y,width,height;
            public RectangleEvidence(float x,float y,float width,float height)
            {
                if(!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height) || width<=0 || height<=0)
                    throw new InvalidOperationException($"Некорректная actual граница доказательства: x={x}, y={y}, width={width}, height={height}.");
                this.x=x;this.y=y;this.width=width;this.height=height;
            }
        }
        [Serializable] public sealed class Visual {public string id,category,drawnClip;public int groupInstance,order,renderers,sortingLayer,drawnFrame;public RectangleEvidence bounds;}
        [Serializable] public sealed class Witness
        {
            public int phase,frame,width,height,rounds,heals,victimLifecycle;public string label,world,observer,victimPose,healingTransaction,scene,png;
            public bool overlap,sharedTable,visible,victimDown;public float victimHealth,revivalProgressMaximum;public RectangleEvidence overlapBounds;
            public Visual victim,shooter,target;public string[] attacks;
        }
        [Serializable] public sealed class Report
        {
            public bool success,ordinaryInputs,initialFixture,batchMuted,firstExitIssued,rightExitIssued;
            public string detail,world,observer,target,shooter,victim,context;
            public int firstClimbSamples,rightClimbSamples,walkSamples,initialRounds,shotRequests,successfulHealingReplies;
            public float firstLadderX,rightLadderX,routeDistance;public Vector2 targetAnchor;public Witness[] proofs;
        }
    }
}
