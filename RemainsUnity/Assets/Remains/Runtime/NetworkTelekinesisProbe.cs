using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using UnityEngine;
using Remains.Domain;

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
        private bool pauseStarted,pauseRunning;
        private NetworkPauseWindow pauseWindow;
        private bool PauseScenario=>runtime.Options.Smoke.Contains("-pause-");
        private bool Host=>runtime.Options.Role=="host";
        private bool ContainerScenario=>runtime.Options.Smoke.Contains("-container-");
        private string initialItems;
        private PropState Target(WorldState world)=>world==null?null:ContainerScenario?world.containers.Single(c=>c.id==ContainerState.PrototypeId):world.props.Single();
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            if(PauseScenario) pauseWindow=new NetworkPauseWindow(this,value.Session,value.Options.ProbePeers,PauseSnapshot,
                ()=>Target(value.Session.World) is {held:true} prop &&
                    prop.owner==value.Session.World.Find("player:session-host")?.owner &&
                    (!Host || Mathf.Abs(prop.vx)+Mathf.Abs(prop.vy)>.01f),
                ()=>finished,reason=>Finish(false,reason));
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<TelekinesisProbePhase>(m=>phase=m.phase);
                NetworkClient.RegisterHandler<TelekinesisProbeFinish>(m=>Finish(m.success,m.detail));
            };
        }
        private PlayerCommand Command()
        {
            var prop=Target(runtime.Session.World);
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
            var prop=Target(session.ServerWorld);
            if(ContainerScenario && initialItems==null) initialItems=Remains.Serialization.StateJson.ToJson(((ContainerState)prop).items);
            if(pauseRunning) return;
            if(PauseScenario && phase==2 && !pauseStarted && prop.held && prop.y>1)
            {pauseStarted=true;pauseRunning=true;StartCoroutine(PauseHeldProp());return;}
            if(phase==0 && !traveled) {traveled=true;session.Travel.RequestFactory();}
            if(phase==0 && self.level=="factory:0" && !session.Travel.Pending) SetPhase(1);
            else if(phase==1 && prop.held && prop.owner==session.LocalOwner) SetPhase(2);
            else if(phase==2 && Vector2.Distance(prop.Position,new Vector2(-1,2))<.15f) SetPhase(3);
            else if(phase==3 && !prop.held && prop.y-prop.anchorHeight<.1f) SetPhase(4);
            else if(phase==4 && prop.held)
            {session.Travel.RequestHub();SetPhase(5);}
            else if(phase==5)
            {
                var successor=session.ServerWorld.Find("player:session-client-1");
                if(self.level!="hub" || successor.level!="factory:0" || prop.owner!=successor.owner || prop.held || prop.y-prop.anchorHeight>.1f) return;
                bool valid=self.mana<100 && successor.mana==100 && prop.level=="factory:0" &&
                    session.World.actors.Where(a=>a.player && a.id!=self.id && a.id!=successor.id).All(a=>a.level=="hub" && a.mana==100);
                if(ContainerScenario)
                {
                    session.CommitWorld();
                    var saved=session.Saves.LoadWorld().containers.Single(c=>c.id==ContainerState.PrototypeId);
                    valid &= Remains.Serialization.StateJson.ToJson(((ContainerState)prop).items)==initialItems &&
                        Remains.Serialization.StateJson.ToJson(saved.items)==initialItems && saved.Position==prop.Position;
                }
                SetPhase(6);
                NetworkServer.SendToAll(new TelekinesisProbeFinish {success=valid,detail=valid?
                    "Обычные команды: захват, подъём, расход маны владельца, отпускание, повторный захват, уход в хаб и передача физики предмета другому игроку."+(ContainerScenario?" Содержимое и ID вещей контейнера сохранены без изменений, положение записано в сейв.":""):"Неверный расход маны, положение или сохранность содержимого."});
            }
        }
        private float[] PauseSnapshot()
        {
            var prop=Target(runtime.Session.World);var holder=runtime.Session.World.Find("player:session-host");
            return new[]{prop.x,prop.y,prop.vx,prop.vy,(float)prop.generation,holder.mana};
        }
        private IEnumerator PauseHeldProp()
        {yield return pauseWindow.Run("удерживаемый предмет телекинеза");pauseRunning=false;}
        private void Finish(bool success,string detail)
        {
            if(finished)return;finished=true;
            if(PauseScenario) {success &= pauseWindow.Verified==1;detail+=$" Проверенные паузы телекинеза: {pauseWindow.Verified}.";}
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
