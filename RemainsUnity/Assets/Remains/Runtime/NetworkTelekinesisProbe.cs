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
        private Action<Remains.Persistence.RecoverySnapshot> checkpointHandler;
        private Vector2 abovePlatform,belowPlatform;
        private const string TargetPropId="prop:crate-1";
        private PropState Target(WorldState world)=>FindTarget(world,ContainerScenario);
        // The fixture owns this ID; level content may add any number of other bodies.
        public static PropState FindTarget(WorldState world,bool container)=>world==null?null:container?
            world.containers.SingleOrDefault(c=>c.id==ContainerState.PrototypeId):world.props.SingleOrDefault(p=>p.id==TargetPropId);
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
            if(phase==7 || phase==8) return new PlayerCommand {telekinesis=true,hasAim=true,aim=phase==7?abovePlatform:belowPlatform};
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
            if(prop==null) {Finish(false,"В тестовом мире отсутствует явный объект телекинеза: "+(ContainerScenario?ContainerState.PrototypeId:TargetPropId));return;}
            if(ContainerScenario && initialItems==null) initialItems=Remains.Serialization.StateJson.ToJson(((ContainerState)prop).items);
            if(pauseRunning) return;
            if(PauseScenario && phase==2 && !pauseStarted && prop.held && prop.y>1)
            {pauseStarted=true;pauseRunning=true;StartCoroutine(PauseHeldProp());return;}
            if(phase==0 && !traveled) {traveled=true;session.Travel.RequestFactory();}
            if(phase==0 && self.level=="factory:0" && !session.Travel.Pending)
            {
                var surface=runtime.Geometry.Get(self.level).platforms.FirstOrDefault(p=>p.oneWay && p.width>2);
                if(surface==null) {Finish(false,"Для проверки спуска нет достаточно широкой платформы.");return;}
                float floor=surface.y+surface.height;
                abovePlatform=new Vector2(surface.x+1,floor+1+prop.anchorHeight);
                belowPlatform=new Vector2(surface.x+1,floor-1+prop.anchorHeight);
                SetPhase(1);
            }
            else if(phase==1 && prop.held && prop.owner==session.LocalOwner) SetPhase(2);
            else if(phase==2 && Vector2.Distance(prop.Position,new Vector2(-1,2))<.15f) SetPhase(7);
            else if(phase==7 && Vector2.Distance(prop.Position,abovePlatform)<.1f) SetPhase(8);
            else if(phase==8 && Vector2.Distance(prop.Position,belowPlatform)<.1f) SetPhase(3);
            else if(phase==3 && !prop.held && prop.y-prop.anchorHeight<.1f) SetPhase(4);
            else if(phase==4 && prop.held)
            {session.Travel.RequestHub();SetPhase(5);}
            else if(phase==5)
            {
                var successor=session.ServerWorld.Find("player:session-client-1");
                if(self.level!="hub" || successor.level!="factory:0" || prop.owner!=successor.owner || prop.held || prop.y-prop.anchorHeight>.1f) return;
                bool valid=self.mana<100 && successor.mana==100 && prop.level=="factory:0" &&
                    session.World.actors.Where(a=>a.player && a.id!=self.id && a.id!=successor.id).All(a=>a.level=="hub" && a.mana==100);
                string failure=$"Мана владельца={self.mana}, преемника={successor.mana}, уровень предмета={prop.level}.";
                if(ContainerScenario)
                {
                    SetPhase(9);StartCoroutine(VerifyContainerCheckpoint((ContainerState)prop,valid,failure));return;
                }
                PublishResult(valid,failure);
            }
        }
        private IEnumerator VerifyContainerCheckpoint(ContainerState prop,bool valid,string failure)
        {
            // CommitWorld publishes ticks; it does not promise a new durable file.
            // Wait for the released body to settle, then for a later recovery write.
            int stillFrames=0;var previous=prop.Position;
            while(!finished && stillFrames<10)
            {
                yield return null;
                stillFrames=prop.vx==0 && prop.vy==0 && prop.Position==previous?stillFrames+1:0;
                previous=prop.Position;
            }
            if(finished) yield break;
            var session=runtime.Session;var expectedPosition=prop.Position;
            long revision=session.ServerWorld.revision+1;bool persisted=false;
            checkpointHandler=snapshot=>{if(snapshot.Revision>=revision) persisted=true;};
            session.RecoveryPersisted+=checkpointHandler;
            try
            {
                session.CommitWorld();
                while(!finished && !persisted) yield return null;
                if(finished) yield break;
                var saved=session.Saves.LoadWorld(false).containers.Single(c=>c.id==ContainerState.PrototypeId);
                bool liveItems=Remains.Serialization.StateJson.ToJson(prop.items)==initialItems;
                bool savedItems=Remains.Serialization.StateJson.ToJson(saved.items)==initialItems;
                bool savedPosition=saved.Position==expectedPosition && prop.Position==expectedPosition;
                valid &= liveItems && savedItems && savedPosition;
                failure+=$" Содержимое: live={liveItems}, saved={savedItems}; позиция saved=({saved.x:R},{saved.y:R}), expected=({expectedPosition.x:R},{expectedPosition.y:R}).";
                PublishResult(valid,failure);
            }
            finally {session.RecoveryPersisted-=checkpointHandler;checkpointHandler=null;}
        }
        private void PublishResult(bool valid,string failure)
        {
            SetPhase(6);
            NetworkServer.SendToAll(new TelekinesisProbeFinish {success=valid,detail=valid?
                "Обычные команды: захват, подъём и спуск сквозь платформу, расход маны владельца, отпускание, повторный захват, уход в хаб и передача физики предмета другому игроку."+(ContainerScenario?" Содержимое и ID вещей контейнера сохранены без изменений, положение записано в сейв.":""):"Неверный расход маны, положение или сохранность содержимого. "+failure});
        }
        private void OnDestroy()
        {if(checkpointHandler!=null) runtime.Session.RecoveryPersisted-=checkpointHandler;}
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
