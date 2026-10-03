using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Runtime
{
    public struct TrainingDoneMessage : NetworkMessage {}
    public struct TrainingFinishMessage : NetworkMessage {}
    public sealed class NetworkTrainingProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private int stage,shots,results;
        private bool sent,finished,configured;
        private bool rejected50,rejected120,observedLift;
        private float manaBeforeLift,minimumLiftMana=100;
        private readonly TrainingMagicRecoveryExercise magicRecovery=new();
        private readonly TrainingSprintExercise sprinting=new();
        private float liftObservedAt=-1;
        private bool Host=>runtime.Options.Role=="host";
        private ContainerState Crate(int index)=>runtime.Session.World.containers.Single(c=>c.id=="training:crate:"+index);
        private readonly HashSet<int> done=new();
        private readonly TrainingJumpExercise jumping=new();
        private readonly TrainingCrouchExercise crouching=new();
        private readonly HashSet<string> observedCrouch=new();
        private readonly HashSet<string> observedLower=new(),observedRise=new();
        private readonly HashSet<string> observedLanding=new();
        private bool independentStance,crouchCaptured;
        private readonly HashSet<string> observedJumpSupport=new();
        private readonly HashSet<string> observedFall=new();
        private bool fallCaptured;
        private readonly TrainingAnimationTrace animationTrace=new();
        private readonly TrainingWeaponTrace weaponTrace=new();
        private int movingShots;
        private bool movingShotCaptured;
        private bool MovingShotExercise=>runtime.Options.Smoke.Contains("-moving-");
        private InteractionVisuals interactionVisuals;
        private void LateUpdate()
        {
            if(finished || runtime?.Session.World==null) return;
            foreach(var actor in runtime.Session.World.actors)
                if(actor.player) animationTrace.Observe(actor,runtime.FindActorVisual(actor.id) as PonyVisual,actor.id==runtime.Session.LocalActorId,interactionVisuals.FindWeapon(actor.id));
            if(weaponTrace.Draw(runtime.Session.World,interactionVisuals) && !movingShotCaptured && runtime.Options.Smoke.Contains("-frames-"))
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".moving-shot.png",runtime.Hud.GetComponent<Canvas>());
                movingShotCaptured=true;
            }
        }
        private readonly Dictionary<string,(int generation,int lifecycle,string pose,float time)> remotePhases=new();
        private int remoteRunSamples,remoteRunRewinds,remoteRunFrameRewinds;
        private float maximumRemoteRunRewind;
        private string previousLocalPose;
        private void ObserveRemoteRun(ActorState actor)
        {
            if(remotePhases.TryGetValue(actor.id,out var previous) && previous.generation==actor.generation &&
                previous.lifecycle==actor.lifecycleSequence && previous.pose=="run" && actor.pose=="run")
            {
                remoteRunSamples++;
                float rewind=previous.time-actor.poseTime;
                if(rewind>.0001f)
                {
                    remoteRunRewinds++;
                    Debug.Log($"Фаза удалённого бега: actor={actor.id}, before={previous.time}, after={actor.poseTime}, rewind={rewind}, x={actor.x}, facing={actor.facing}, time={Time.unscaledTime}.");
                    maximumRemoteRunRewind=Mathf.Max(maximumRemoteRunRewind,rewind);
                    if(rewind>=1f/30) remoteRunFrameRewinds++;
                }
            }
            remotePhases[actor.id]=(actor.generation,actor.lifecycleSequence,actor.pose,actor.poseTime);
        }
        public void Initialize(PrototypeRuntime value)
        {
            interactionVisuals=FindFirstObjectByType<InteractionVisuals>();
            value.Session.RemotePoseCorrection+=(source,id,before,after)=>
                Debug.Log($"Источник изменения фазы: source={source}, actor={id}, before={before}, after={after}, time={Time.unscaledTime}.");
            runtime=value;deadline=Time.unscaledTime+75;value.CommandSource=Command;
            value.Session.Notice+=notice=>
            {
                if(notice.StartsWith("Слишком тяжело: 50 кг",StringComparison.Ordinal)) rejected50=true;
                if(notice.StartsWith("Слишком тяжело: 120 кг",StringComparison.Ordinal)) rejected120=true;
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<TrainingDoneMessage>((connection,message)=>
            {done.Add(connection.connectionId);if(done.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new TrainingFinishMessage());});
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<TrainingFinishMessage>(_=>StartCoroutine(FinishAfterDelivery()));
            value.Session.AttackReceived+=attack=>
            {
                weaponTrace.Observe(attack,value.Session.World?.Find(attack.attacker),attack.attacker==value.Session.LocalActorId);
                if(attack.attacker!=value.Session.LocalActorId || attack.weapon!="pistol") return;
                if(MovingShotExercise) movingShots++;else shots++;
            };
            value.DamageFeedback.Received+=result=>
            {
                if(result.actor!="training:hub:1") return;
                if(result.amount!=11) {Finish(false,"Неверное число урона мишени: "+result.amount);return;}
                results++;
            };
        }
        private IEnumerator FinishAfterDelivery()
        {
            yield return new WaitForSecondsRealtime(1);
            Finish(true,MovingShotExercise?"Измерение оружия на бегу: три выстрела каждым игроком; расхождение точки запуска записано отдельно и не считается исправленным.":
                (runtime.Options.ProbePeers==1?"Одиночная игра. ":"Кооператив; стойки и прыжки видны другим клиентам. ")+"Приседание, короткий/длинный прыжок, балка и двойное S. Три мишени, по три выстрела на игрока; отказ 50/120 кг и подъём/падение 25 кг.");
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;if(self==null || finished) return default;
            if(runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return default;
            if(stage==0)
            {
                float delta=14.98f-self.x;
                if(Mathf.Abs(delta)>.08f) return new PlayerCommand {movement=new Vector2(Mathf.Sign(delta),0)};
                stage=1;
            }
            if(stage==1)
            {
                if(self.y>1) return new PlayerCommand {movement=Vector2.down};
                stage=2;
            }
            if(stage==2)
            {
                float delta=26-self.x;
                if(Mathf.Abs(delta)>.08f) return new PlayerCommand {movement=new Vector2(Mathf.Sign(delta),0)};
                stage=11;
            }
            if(stage==11)
            {
                if(!crouching.Complete) return crouching.Step(self,Host);
                var jumpCommand=jumping.Step(self);
                if(!jumping.Complete) return jumpCommand;
                if(Mathf.Abs(jumping.ShortHeight-1.44f)>.15f || Mathf.Abs(jumping.LongHeight-3.36f)>.15f)
                {Finish(false,$"Неверная высота прыжка от обычного ввода: short={jumping.ShortHeight}, long={jumping.LongHeight}.");return default;}
                if(!sprinting.Complete)
                {
                    try
                    {
                        var command=sprinting.Step(self);
                        if(MovingShotExercise)
                        {
                            command.weapon="pistol";command.hasAim=true;
                            command.aim=self.Position+Vector2.up*10;
                            command.fire=self.pose=="run" && movingShots<3;
                        }
                        return command;
                    }
                    catch(InvalidOperationException error) {Finish(false,error.Message);return default;}
                }
                stage=3;
            }
            if(MovingShotExercise) return new PlayerCommand {hasAim=true,aim=self.Position+Vector2.up*10};
            if(Host && stage==3 && shots==3 && results==3*runtime.Options.ProbePeers)
            {stage=4;manaBeforeLift=self.mana;}
            if(stage>=4) return CrateCommand();
            // The jump intentionally preserves momentum; readiness is the firing area, not one exact point.
            bool ready=runtime.Session.World.actors.Where(a=>a.player && a.connected).All(a=>Mathf.Abs(a.x-26)<1 && a.y<1);
            return new PlayerCommand {weapon="pistol",hasAim=true,aim=new Vector2(29,1.6f),fire=ready && shots<3};
        }
        private PlayerCommand CrateCommand()
        {
            switch(stage)
            {
                case 4: stage=5;return new PlayerCommand {telekinesis=true,hasAim=true,aim=Crate(2).Position};
                case 5: if(rejected50) stage=6;return default;
                case 6: stage=7;return new PlayerCommand {telekinesis=true,hasAim=true,aim=Crate(3).Position};
                case 7: if(rejected120) stage=8;return default;
                case 8: stage=9;return new PlayerCommand {telekinesis=true,hasAim=true,aim=Crate(1).Position};
                case 9:
                    // Keep the normal held input long enough for remote snapshots to observe it.
                    if(observedLift && Time.unscaledTime-liftObservedAt>.75f) stage=10;
                    return new PlayerCommand {telekinesis=true,hasAim=true,aim=new Vector2(18,1.5f)};
                default:return default;
            }
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут мишеней: stage={stage}, shots={shots}, results={results}, position={runtime.Session.LocalActor?.Position}.");return;}
            var self=runtime.Session.LocalActor;if(self==null) return;
            if(MovingShotExercise && stage==3 && movingShots==3)
            {
                if(!sent) {sent=true;NetworkClient.Send(new TrainingDoneMessage());}
                return;
            }
            if(previousLocalPose!=self.pose)
            {
                if(previousLocalPose=="run" || self.pose=="run")
                    Debug.Log($"Переход локального бега: actor={self.id}, from={previousLocalPose}, to={self.pose}, phase={self.poseTime}, x={self.x}, facing={self.facing}, time={Time.unscaledTime}.");
                previousLocalPose=self.pose;
            }
            if(Host && stage>=4) minimumLiftMana=Mathf.Min(minimumLiftMana,self.mana);
            foreach(var other in runtime.Session.World.actors)
                if(other.player && other.id!=self.id)
                {
                    ObserveRemoteRun(other);
                    if(other.jumpSupportRemaining>0) observedJumpSupport.Add(other.id);
                    if(other.crouched) {observedCrouch.Add(other.id);if(!self.crouched) independentStance=true;}
                    if(other.pose=="crouchLower") observedLower.Add(other.id);
                    if(other.pose=="crouchRise") observedRise.Add(other.id);
                    if(other.pose=="landing") observedLanding.Add(other.id);
                    if(other.pose=="fall") observedFall.Add(other.id);
                }
            if(Host && !crouchCaptured && self.pose=="crawl" && Mathf.Abs(self.vx+2.1f)<.05f && runtime.Options.Smoke.Contains("-frames-"))
            {NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".crouch.png",runtime.Hud.GetComponent<Canvas>());crouchCaptured=true;}
            if(Host && !fallCaptured && self.pose=="fall" && self.vy<-1.2f && runtime.Options.Smoke.Contains("-frames-"))
            {NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".fall.png",runtime.Hud.GetComponent<Canvas>());fallCaptured=true;}
            if(NetworkServer.active && !configured) {configured=true;runtime.Session.SetFriendlyFire(false);}
            if(runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers || runtime.Session.World.friendlyFire) return;
            var targets=runtime.Session.World.actors.Where(a=>a.actorType=="training-dummy").ToArray();
            var light=Crate(1);
            if(light.held && light.y>1.15f)
            {if(!observedLift) liftObservedAt=Time.unscaledTime;observedLift=true;}
            if(Crate(2).held || Crate(3).held || Vector2.Distance(Crate(2).Position,new Vector2(20,.8f))>.03f ||
                Vector2.Distance(Crate(3).Position,new Vector2(23,.8f))>.03f)
            {Finish(false,"Тяжёлый ящик сдвинут или захвачен при пределе 30 кг.");return;}
            if(targets.Length!=3 || targets.Any(a=>a.down || a.health!=500 || a.target!=null || a.attackBusy))
            {Finish(false,"Мишени дублируются, погибают или ведут бой.");return;}
            if(results<3*runtime.Options.ProbePeers || sent) return;
            if(results!=3*runtime.Options.ProbePeers || shots!=3 || targets.Single(a=>a.id=="training:hub:1").resolvedAttacks.Count!=results) return;
            if(!observedLift || light.held || Host && (stage<10 || !rejected50 || !rejected120)) return;
            if(runtime.PropAudio.Landings<1) return;
            if(runtime.PropAudio.Landings!=1) {Finish(false,"Звук единственного падения ящика повторён.");return;}
            if(!jumping.Complete || observedJumpSupport.Count!=runtime.Options.ProbePeers-1) return;
            if(!crouching.Complete || observedCrouch.Count!=runtime.Options.ProbePeers-1 || runtime.Options.ProbePeers>1 && !independentStance) return;
            if(observedLower.Count!=runtime.Options.ProbePeers-1 || observedRise.Count!=runtime.Options.ProbePeers-1) return;
            if(observedLanding.Count!=runtime.Options.ProbePeers-1 || observedFall.Count!=runtime.Options.ProbePeers-1) return;
            if(runtime.MovementAudio.LocalContacts<5 || runtime.Options.ProbePeers>1 && runtime.MovementAudio.RemoteContacts<5 || runtime.MovementAudio.Landings<2)
            {Finish(false,"Не воспроизведены локальные/удалённые шаги и приземления.");return;}
            if(runtime.MovementAudio.LandingVoices!=runtime.MovementAudio.Landings*2)
            {Finish(false,"Приземление не создало два звуковых контакта.");return;}
            if(Host && minimumLiftMana>=manaBeforeLift) {Finish(false,"Подъём 25 кг не расходует ману.");return;}
            if(Host)
            {
                if(done.Count<runtime.Options.ProbePeers-1) return;
                try {if(!magicRecovery.Step(runtime.Session,self)) return;}
                catch(Exception error) {Finish(false,error.Message);return;}
            }
            var views=FindObjectsByType<StaticActorVisual>(FindObjectsSortMode.None);
            if(views.Length!=3 || views.Select(v=>v.ActorId).Distinct().Count()!=3) {Finish(false,"Неверное количество моделей мишеней.");return;}
            foreach(var view in views)
                if(view.GetComponentsInParent<ActorVisualGroup>().Length!=1) {Finish(false,"Мишень не включена в целую группу отрисовки.");return;}
            if(runtime.Options.Smoke.Contains("-frames-"))
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".training.png",runtime.Hud.GetComponent<Canvas>());
            sent=true;NetworkClient.Send(new TrainingDoneMessage());
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && MovingShotExercise && movingShots!=3) {success=false;detail="Ожидалось три выстрела на бегу, получено "+movingShots;}
            string json=JsonUtility.ToJson(new Report {success=success,participants=runtime.Options.ProbePeers,detail=detail,shots=shots,results=results,
                rejected50=rejected50,rejected120=rejected120,observedLift=observedLift,shortJump=jumping.ShortHeight,longJump=jumping.LongHeight,remoteJumps=observedJumpSupport.Count,
                remoteRunSamples=remoteRunSamples,remoteRunRewinds=remoteRunRewinds,remoteRunFrameRewinds=remoteRunFrameRewinds,maximumRemoteRunRewind=maximumRemoteRunRewind,
                sprintSamples=sprinting.Samples,sprintBackwardSteps=sprinting.BackwardSteps,longestRun=sprinting.LongestContinuousRun,movingShots=movingShots,
                recoveryChecked=magicRecovery.Complete,pausedMana=magicRecovery.PausedMana,recoveredMana=magicRecovery.RecoveredMana,
                localContacts=runtime.MovementAudio.LocalContacts,remoteContacts=runtime.MovementAudio.RemoteContacts,landings=runtime.MovementAudio.Landings,landingVoices=runtime.MovementAudio.LandingVoices,ladderContacts=runtime.MovementAudio.LadderContacts,
                remoteCrouches=observedCrouch.Count,remoteLower=observedLower.Count,remoteRise=observedRise.Count,remoteLandingPoses=observedLanding.Count,remoteFallPoses=observedFall.Count,propLandings=runtime.PropAudio.Landings,impactSounds=runtime.ImpactAudio.Played,independentStance=independentStance},true);
            animationTrace.Save(runtime.Options.Report+".animation.json");
            weaponTrace.Save(runtime.Options.Report+".weapons.json");
            File.WriteAllText(runtime.Options.Report,json);Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public int remoteRunSamples,remoteRunRewinds,remoteRunFrameRewinds;public float maximumRemoteRunRewind;public bool recoveryChecked;public float pausedMana,recoveredMana;public bool success,rejected50,rejected120,observedLift,independentStance;public string detail;public int sprintSamples,sprintBackwardSteps;public float longestRun;public int movingShots;public int participants,shots,results,remoteJumps,localContacts,remoteContacts,landings,landingVoices,ladderContacts,remoteCrouches,remoteLower,remoteRise,remoteLandingPoses,remoteFallPoses,propLandings,impactSounds;public float shortJump,longJump;}
    }
}
