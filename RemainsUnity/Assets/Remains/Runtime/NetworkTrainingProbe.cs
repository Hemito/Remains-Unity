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
        private float liftObservedAt=-1;
        private bool Host=>runtime.Options.Role=="host";
        private ContainerState Crate(int index)=>runtime.Session.World.containers.Single(c=>c.id=="training:crate:"+index);
        private readonly HashSet<int> done=new();
        private readonly TrainingJumpExercise jumping=new();
        private readonly TrainingCrouchExercise crouching=new();
        private readonly HashSet<string> observedCrouch=new();
        private readonly HashSet<string> observedLower=new(),observedRise=new();
        private bool independentStance,crouchCaptured;
        private readonly HashSet<string> observedJumpSupport=new();
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+75;value.CommandSource=Command;
            value.Session.Notice+=notice=>
            {
                if(notice.StartsWith("Слишком тяжело: 50 кг",StringComparison.Ordinal)) rejected50=true;
                if(notice.StartsWith("Слишком тяжело: 120 кг",StringComparison.Ordinal)) rejected120=true;
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<TrainingDoneMessage>((connection,message)=>
            {done.Add(connection.connectionId);if(done.Count==value.Options.ProbePeers) NetworkServer.SendToAll(new TrainingFinishMessage());});
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<TrainingFinishMessage>(_=>StartCoroutine(FinishAfterDelivery()));
            value.Session.AttackReceived+=attack=>{if(attack.attacker==value.Session.LocalActorId && attack.weapon=="pistol") shots++;};
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
            Finish(true,(runtime.Options.ProbePeers==1?"Одиночная игра. ":"Кооператив; стойки и прыжки видны другим клиентам. ")+"Приседание, короткий/длинный прыжок, балка и двойное S. Три мишени, по три выстрела на игрока; отказ 50/120 кг и подъём/падение 25 кг.");
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
                stage=3;
            }
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
            if(Host && stage>=4) minimumLiftMana=Mathf.Min(minimumLiftMana,self.mana);
            foreach(var other in runtime.Session.World.actors)
                if(other.player && other.id!=self.id)
                {
                    if(other.jumpSupportRemaining>0) observedJumpSupport.Add(other.id);
                    if(other.crouched) {observedCrouch.Add(other.id);if(!self.crouched) independentStance=true;}
                    if(other.pose=="crouchLower") observedLower.Add(other.id);
                    if(other.pose=="crouchRise") observedRise.Add(other.id);
                }
            if(Host && !crouchCaptured && self.pose=="crawl" && Mathf.Abs(self.vx+2.1f)<.05f && runtime.Options.Smoke.Contains("-frames-"))
            {NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".crouch.png",runtime.Hud.GetComponent<Canvas>());crouchCaptured=true;}
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
            if(runtime.MovementAudio.LocalContacts<5 || runtime.Options.ProbePeers>1 && runtime.MovementAudio.RemoteContacts<5 || runtime.MovementAudio.Landings<2)
            {Finish(false,"Не воспроизведены локальные/удалённые шаги и приземления.");return;}
            if(Host && minimumLiftMana>=manaBeforeLift) {Finish(false,"Подъём 25 кг не расходует ману.");return;}
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
            string json=JsonUtility.ToJson(new Report {success=success,participants=runtime.Options.ProbePeers,detail=detail,shots=shots,results=results,
                rejected50=rejected50,rejected120=rejected120,observedLift=observedLift,shortJump=jumping.ShortHeight,longJump=jumping.LongHeight,remoteJumps=observedJumpSupport.Count,
                localContacts=runtime.MovementAudio.LocalContacts,remoteContacts=runtime.MovementAudio.RemoteContacts,landings=runtime.MovementAudio.Landings,ladderContacts=runtime.MovementAudio.LadderContacts,
                remoteCrouches=observedCrouch.Count,remoteLower=observedLower.Count,remoteRise=observedRise.Count,propLandings=runtime.PropAudio.Landings,impactSounds=runtime.ImpactAudio.Played,independentStance=independentStance},true);
            File.WriteAllText(runtime.Options.Report,json);Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success,rejected50,rejected120,observedLift,independentStance;public string detail;public int participants,shots,results,remoteJumps,localContacts,remoteContacts,landings,ladderContacts,remoteCrouches,remoteLower,remoteRise,propLandings,impactSounds;public float shortJump,longJump;}
    }
}
