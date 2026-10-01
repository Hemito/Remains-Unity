using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct TelekinesisProbePhase : NetworkMessage {public int phase;}
    public struct TelekinesisProbeFinish : NetworkMessage {public bool success;public string detail;}
    public sealed class NetworkTelekinesisProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline;
        private bool traveled,finished;
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<TelekinesisProbePhase>(m=>phase=m.phase);
                NetworkClient.RegisterHandler<TelekinesisProbeFinish>(m=>Finish(m.success,m.detail));
            };
        }
        private PlayerCommand Command()
        {
            var prop=runtime.Session.World?.props.FirstOrDefault();
            if(!Host || prop==null) return default;
            if(phase==1 || phase==4) return new PlayerCommand {telekinesis=true,hasAim=true,aim=prop.Position};
            if(phase==2) return new PlayerCommand {telekinesis=true,hasAim=true,aim=new Vector2(-1,2)};
            return default;
        }
        private void SetPhase(int value) {phase=value;NetworkServer.SendToAll(new TelekinesisProbePhase {phase=value});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут телекинеза: phase={phase}");return;}
            var session=runtime.Session;var self=session.LocalActor;
            if(self==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(!Host)
            {
                if(phase==5 && !traveled && runtime.Options.Character=="session-client-1")
                {traveled=true;session.Travel.RequestFactory();}
                return;
            }
            var prop=session.ServerWorld.props.Single();
            if(phase==0 && !traveled) {traveled=true;session.Travel.RequestFactory();}
            if(phase==0 && self.level=="factory:0" && !session.Travel.Pending) SetPhase(1);
            else if(phase==1 && prop.held && prop.owner==session.LocalOwner) SetPhase(2);
            else if(phase==2 && Vector2.Distance(prop.Position,new Vector2(-1,2))<.15f) SetPhase(3);
            else if(phase==3 && !prop.held && prop.y<.4f) SetPhase(4);
            else if(phase==4 && prop.held)
            {session.Travel.RequestHub();SetPhase(5);}
            else if(phase==5)
            {
                var successor=session.ServerWorld.Find("player:session-client-1");
                if(self.level!="hub" || successor.level!="factory:0" || prop.owner!=successor.owner || prop.held || prop.y>.4f) return;
                bool valid=self.mana<100 && successor.mana==100 && prop.level=="factory:0" &&
                    session.World.actors.Where(a=>a.player && a.id!=self.id && a.id!=successor.id).All(a=>a.level=="hub" && a.mana==100);
                SetPhase(6);
                NetworkServer.SendToAll(new TelekinesisProbeFinish {success=valid,detail=valid?
                    "Обычные команды: захват, подъём, расход маны владельца, отпускание, повторный захват, уход в хаб и передача физики предмета другому игроку.":"Неверный расход маны или локация предмета/наблюдателей."});
            }
        }
        private void Finish(bool success,string detail)
        {if(finished)return;finished=true;File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));}
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
