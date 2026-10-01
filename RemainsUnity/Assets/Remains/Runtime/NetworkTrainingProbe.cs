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
        private readonly HashSet<int> done=new();
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+75;value.CommandSource=Command;
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
            Finish(true,"Три мишени, обычный маршрут к ним, по три выстрела каждого игрока, фактический урон и восстановление без дубликатов.");
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;if(self==null || finished) return default;
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
                stage=3;
            }
            bool ready=runtime.Session.World.actors.Where(a=>a.player && a.connected).All(a=>Mathf.Abs(a.x-26)<.15f && a.y<1);
            return new PlayerCommand {weapon="pistol",hasAim=true,aim=new Vector2(29,1.6f),fire=ready && shots<3};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут мишеней: stage={stage}, shots={shots}, results={results}, position={runtime.Session.LocalActor?.Position}.");return;}
            var self=runtime.Session.LocalActor;if(self==null) return;
            if(NetworkServer.active && !configured) {configured=true;runtime.Session.SetFriendlyFire(false);}
            if(runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers || runtime.Session.World.friendlyFire) return;
            var targets=runtime.Session.World.actors.Where(a=>a.actorType=="training-dummy").ToArray();
            if(targets.Length!=3 || targets.Any(a=>a.down || a.health!=500 || a.target!=null || a.attackBusy))
            {Finish(false,"Мишени дублируются, погибают или ведут бой.");return;}
            if(results<3*runtime.Options.ProbePeers || sent) return;
            if(results!=3*runtime.Options.ProbePeers || shots!=3 || targets.Single(a=>a.id=="training:hub:1").resolvedAttacks.Count!=results) return;
            var views=FindObjectsByType<StaticActorVisual>(FindObjectsSortMode.None);
            if(views.Length!=3 || views.Select(v=>v.ActorId).Distinct().Count()!=3) {Finish(false,"Неверное количество моделей мишеней.");return;}
            foreach(var view in views)
                if(view.GetComponentsInParent<ActorVisualGroup>().Length!=1) {Finish(false,"Мишень не включена в целую группу отрисовки.");return;}
            if(runtime.Options.Smoke.Contains("-frames-"))
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".training.png");
            sent=true;NetworkClient.Send(new TrainingDoneMessage());
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            string json=JsonUtility.ToJson(new Report {success=success,detail=detail,shots=shots,results=results},true);
            File.WriteAllText(runtime.Options.Report,json);Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success;public string detail;public int shots,results;}
    }
}
