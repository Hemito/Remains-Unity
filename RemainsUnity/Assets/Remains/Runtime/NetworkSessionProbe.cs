using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct SessionProbePhase : NetworkMessage { public int phase; }
    public struct SessionProbeAck : NetworkMessage { public int phase; }

    // No ActorState writes: commands enter the production loop, NPCs keep running.
    public sealed class NetworkSessionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1;
        private float phaseTime,deadline;
        private bool acked,travelRequested,finished;
        private readonly HashSet<int> acknowledgements=new();
        private bool Host=>runtime.Options.Role=="host";

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+50;
            runtime.CommandSource=Command;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<SessionProbePhase>(message=>
            {phase=message.phase;phaseTime=Time.unscaledTime;acked=false;travelRequested=false;});
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<SessionProbeAck>((connection,message)=>
            {
                if(message.phase==phase && value.Session.ConnectedActor(connection)!=null) acknowledgements.Add(connection.connectionId);
            });
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(self==null || phase!=3) return default;
            float destination=Host?-2:2;
            float delta=destination-self.x;
            return new PlayerCommand { movement=new Vector2(Mathf.Abs(delta)>.05f?Mathf.Sign(delta):0,0),
                hasAim=true,aim=self.Position+new Vector2(Host?-5:5,.8f),weapon="pistol" };
        }
        private void Advance(int next)
        {
            acknowledgements.Clear();
            NetworkServer.SendToAll(new SessionProbePhase {phase=next});
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: phase={phase}, level={runtime.Session.LocalActor?.level}, pending={runtime.Session.Travel.Pending}");return;}
            var session=runtime.Session;var self=session.LocalActor;
            if(self==null) return;
            var players=session.World.actors.Where(a=>a.player && a.connected).ToArray();
            if(players.Length!=runtime.Options.ProbePeers) return;
            if(phase<0) {if(Host) Advance(0);return;}
            if(phase==4) {if(Time.unscaledTime-phaseTime>1) Finish(true,"Обычная симуляция: разделение группы, переход через кнопку, независимое движение и видимость; NPC включён.");return;}
            if((phase==1 && Host || phase==2 && !Host) && !travelRequested)
            {travelRequested=true;runtime.Hud.ActivateFactoryButton();}
            if(Time.unscaledTime-phaseTime<1) return;
            bool ready=phase switch
            {
                0=>players.All(a=>a.level=="hub"),
                1=>self.level==(Host?"factory:0":"hub") && players.Count(a=>a.level=="factory:0")==1,
                2=>players.All(a=>a.level=="factory:0") && !session.Travel.Pending,
                3=>players.All(a=>a.level=="factory:0" && (a.character=="session-host"?a.x<-1.8f && a.facing==-1:a.x>1.8f && a.facing==1)),
                _=>false
            };
            if(!ready) return;
            if(players.Select(a=>a.id).Distinct().Count()!=players.Length) {Finish(false,"Повтор ID персонажа");return;}
            var expected=session.World.actors.Where(a=>a.level==self.level).Select(a=>a.id).OrderBy(id=>id).ToArray();
            var visible=FindObjectsByType<PonyVisual>(FindObjectsSortMode.None).Select(v=>v.ActorId).OrderBy(id=>id).ToArray();
            if(!expected.SequenceEqual(visible)) {Finish(false,$"Неверная видимость: expected={string.Join(",",expected)}, actual={string.Join(",",visible)}");return;}
            if(!acked) {acked=true;NetworkClient.Send(new SessionProbeAck {phase=phase});}
            if(Host && acknowledgements.Count==runtime.Options.ProbePeers) Advance(phase+1);
        }
        private void Finish(bool success,string detail)
        {
            finished=true;
            string json=JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,peers=runtime.Options.ProbePeers,actor=runtime.Session.LocalActorId},true);
            if(runtime.Options.Report!=null) File.WriteAllText(runtime.Options.Report,json);
            Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success;public string detail,actor;public int phase,peers;}
    }
}
