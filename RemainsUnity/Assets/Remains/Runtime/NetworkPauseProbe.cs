using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public struct PauseProbePhase : NetworkMessage {public int phase;}
    public struct PauseProbeAck : NetworkMessage {public int phase;}
    public struct PauseProbeFinish : NetworkMessage { }
    // Uses ordinary player commands and session requests, with per-client simulation evidence.
    public sealed class NetworkPauseProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private readonly HashSet<int> acknowledgements=new();
        private int phase=-1,hostPhase=-1,acknowledged=-1;
        private float deadline;
        private bool finished;
        private bool fired,reloaded,sampling;
        private int frozenSamples,activeSamples;
        private string failure;
        private bool menuCommand,uiBusy;
        private bool First=>runtime.Session.CharacterId=="session-client-1";
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;
            value.CommandSource=Command;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<PauseProbeAck>((connection,message)=>
                {if(message.phase==hostPhase) acknowledgements.Add(connection.connectionId);});
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PauseProbePhase>(Receive);
                NetworkClient.RegisterHandler<PauseProbeFinish>(_=>Finish(true));
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null) return default;
            if(menuCommand) {menuCommand=false;return new PlayerCommand {menu=true};}
            var result=new PlayerCommand {movement=Vector2.right*.15f,hasAim=true,aim=actor.Position+Vector2.right*20};
            if(First && phase==1 && actor.level=="factory:0" && !runtime.Session.Travel.Pending)
            {
                if(!fired) {result.fire=true;result.grenade=true;fired=true;}
                else if(!reloaded) {result.reload=true;reloaded=true;}
            }
            return result;
        }
        private IEnumerator Start()
        {
            if(!Host) yield break;
            while(runtime.Session.World==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) yield return null;
            for(int step=0;step<=11 && !finished;step++)
            {
                hostPhase=step;acknowledgements.Clear();
                if(step==6) yield return ConfigureSettings(true,false);
                if(step==8) yield return ConfigureSettings(false,true);
                if(step==11) yield return ConfigureSettings(false,false);
                NetworkServer.SendToAll(new PauseProbePhase {phase=step});
                while(acknowledgements.Count!=runtime.Options.ProbePeers && !finished) yield return null;
            }
            if(!finished) NetworkServer.SendToAll(new PauseProbeFinish());
        }
        private void Receive(PauseProbePhase message)
        {
            phase=message.phase;
            var session=runtime.Session;
            if(phase==1 && (Host || First)) session.Travel.RequestFactory();
            if(phase==2 && First) StartCoroutine(ManualPause(true));
            if(phase==3 && Host) session.Travel.RequestHub();
            if(phase==4 && Host)
            {
                // Bypass only the optimistic local gate; host must still reject an actual travel request.
                var actor=session.LocalActor;
                NetworkClient.Send(new TravelRequest {id="pause-probe-blocked",source=actor.level,destination="factory:0",
                    generation=actor.generation,lifecycle=actor.lifecycleSequence});
                StartCoroutine(AcknowledgeBlockedEntry());
            }
            if(phase==5 && First) StartCoroutine(ManualPause(false));
            if(phase==6 && First) session.Pause.SetIntent(true,false);
            if(phase==7 && First) session.Pause.SetIntent(false,true);
            if(phase==9 && First) session.Travel.RequestHub();
            if(phase==10 && First) session.Travel.RequestFactory();
        }
        private IEnumerator OpenMenu()
        {
            if(runtime.Hud.PauseMenu.IsOpen) yield break;
            menuCommand=true;
            while(!runtime.Hud.PauseMenu.IsOpen && !finished) yield return null;
        }
        private IEnumerator CloseMenu()
        {
            if(!runtime.Hud.PauseMenu.IsOpen) yield break;
            menuCommand=true;
            while(runtime.Hud.PauseMenu.IsOpen && !finished) yield return null;
        }
        private IEnumerator ManualPause(bool paused)
        {
            uiBusy=true;yield return OpenMenu();
            yield return UiPointerProbe.Click(paused?runtime.Hud.PauseMenu.ManualPauseRect:runtime.Hud.PauseMenu.ResumeRect);
            while(runtime.Session.Pause.IsPaused("factory:0")!=paused && !finished) yield return null;
            yield return CloseMenu();uiBusy=false;
        }
        private IEnumerator ConfigureSettings(bool menu,bool inventory)
        {
            yield return OpenMenu();
            if(runtime.Session.Pause.PauseOnMenu!=menu)
            {
                yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.MenuSettingRect);
                while(runtime.Session.Pause.PauseOnMenu!=menu && !finished) yield return null;
            }
            if(runtime.Session.Pause.PauseOnInventory!=inventory)
            {
                yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.InventorySettingRect);
                while(runtime.Session.Pause.PauseOnInventory!=inventory && !finished) yield return null;
            }
            yield return CloseMenu();
        }
        private IEnumerator AcknowledgeBlockedEntry()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if(runtime.Session.ServerWorld.Find(runtime.Session.LocalActorId).level!="hub") {Finish(false);yield break;}
            Acknowledge();
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false);return;}
            var session=runtime.Session;
            if(uiBusy || phase<0 || acknowledged==phase || session.LocalActor==null || session.Travel.Pending || session.Pause.Revision<0) return;
            if(phase==4 && Host) return;
            bool expectedPause=phase is 2 or 3 or 4 or 6 or 8;
            bool expectedMenu=phase is 6 or 7;
            bool expectedInventory=phase is 8 or 9 or 10;
            string expectedLevel=First && phase>=1 && phase!=9 || Host && phase is 1 or 2?"factory:0":"hub";
            if(session.LocalActor.level!=expectedLevel || session.Pause.IsPaused("factory:0")!=expectedPause || session.Pause.IsPaused("hub") ||
                session.Pause.PauseOnMenu!=expectedMenu || session.Pause.PauseOnInventory!=expectedInventory) return;
            if(phase==1 && First && (!runtime.Combat.Reload.Active || !runtime.Grenades.Flights.Any())) return;
            if(phase is 2 or 3)
            {if(!sampling) {sampling=true;StartCoroutine(SampleSimulation());}return;}
            Acknowledge();
        }
        private float[] Snapshot()
        {
            var session=runtime.Session;var actor=session.LocalActor;
            var values=new List<float> {actor.x,actor.y,actor.vx,actor.vy,actor.poseTime,actor.weaponAngle,
                runtime.Combat.ShotCooldownRemaining,runtime.Combat.Reload.Remaining,runtime.Revival.Progress,actor.mana};
            foreach(var npc in session.World.actors.Where(a=>!a.player && a.owner==session.LocalOwner && a.level==actor.level).OrderBy(a=>a.id))
                values.AddRange(new[]{npc.x,npc.y,npc.vx,npc.vy,npc.poseTime,npc.attackRemaining,npc.attackCooldown});
            foreach(var flight in runtime.Grenades.Flights.Where(f=>f.state.level==actor.level).OrderBy(f=>f.state.id))
                values.AddRange(new[]{flight.state.x,flight.state.y,flight.state.vx,flight.state.vy,flight.state.remaining});
            foreach(var prop in session.World.props.Where(p=>p.level==actor.level && p.owner==session.LocalOwner).OrderBy(p=>p.id))
                values.AddRange(new[]{prop.x,prop.y,prop.vx,prop.vy});
            return values.ToArray();
        }
        private IEnumerator SampleSimulation()
        {
            yield return new WaitForSecondsRealtime(.8f); // Allow already sent pose packets to settle.
            bool paused=runtime.Session.Pause.IsPaused(runtime.Session.LocalActor.level);
            var before=Snapshot();
            if(First && (!runtime.Combat.Reload.Active || !runtime.Grenades.Flights.Any(f=>f.state.remaining>0)))
            {failure="Не создана незавершённая перезарядка/граната перед паузой";Finish(false);yield break;}
            yield return new WaitForSecondsRealtime(.8f);
            var after=Snapshot();
            if(paused && !before.SequenceEqual(after)) {failure="Симуляция изменилась во время паузы";Finish(false);yield break;}
            if(!paused && after[0]<=before[0]+.1f) {failure="Активный хаб не продолжает движение";Finish(false);yield break;}
            if(paused) frozenSamples++;else activeSamples++;
            Acknowledge();sampling=false;
        }
        private void Acknowledge()
        {acknowledged=phase;NetworkClient.Send(new PauseProbeAck {phase=phase});}
        private void Finish(bool success)
        {
            if(finished) return;finished=true;exitCode=success?0:1;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                detail=$"Пауза: phase={phase}, ack={acknowledged}, frozenSamples={frozenSamples}, activeSamples={activeSamples}, revision={runtime.Session.Pause.Revision}, level={runtime.Session.LocalActor?.level}, failure={failure}."},true));
            Invoke(nameof(Exit),1);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
