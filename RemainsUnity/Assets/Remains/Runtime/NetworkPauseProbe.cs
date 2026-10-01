using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public struct PauseProbePhase : NetworkMessage {public int phase;}
    public struct PauseProbeAck : NetworkMessage {public int phase;}
    public struct PauseProbeFinish : NetworkMessage { }
    // Transport/lifecycle contract probe. It does not claim gameplay simulation is frozen.
    public sealed class NetworkPauseProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private readonly HashSet<int> acknowledgements=new();
        private int phase=-1,hostPhase=-1,acknowledged=-1;
        private float deadline;
        private bool finished;
        private bool First=>runtime.Session.CharacterId=="session-client-1";
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<PauseProbeAck>((connection,message)=>
                {if(message.phase==hostPhase) acknowledgements.Add(connection.connectionId);});
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PauseProbePhase>(Receive);
                NetworkClient.RegisterHandler<PauseProbeFinish>(_=>Finish(true));
            };
        }
        private IEnumerator Start()
        {
            if(!Host) yield break;
            while(runtime.Session.World==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) yield return null;
            for(int step=0;step<=11 && !finished;step++)
            {
                hostPhase=step;acknowledgements.Clear();
                if(step==6) runtime.Session.Pause.SetSettings(true,false);
                if(step==8) runtime.Session.Pause.SetSettings(false,true);
                if(step==11) runtime.Session.Pause.SetSettings(false,false);
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
            if(phase==2 && First) session.Pause.SetManual(true);
            if(phase==3 && Host) session.Travel.RequestHub();
            if(phase==4 && Host)
            {
                // Bypass only the optimistic local gate; host must still reject an actual travel request.
                var actor=session.LocalActor;
                NetworkClient.Send(new TravelRequest {id="pause-probe-blocked",source=actor.level,destination="factory:0",
                    generation=actor.generation,lifecycle=actor.lifecycleSequence});
                StartCoroutine(AcknowledgeBlockedEntry());
            }
            if(phase==5 && First) session.Pause.SetManual(false);
            if(phase==6 && First) session.Pause.SetIntent(true,false);
            if(phase==7 && First) session.Pause.SetIntent(false,true);
            if(phase==9 && First) session.Travel.RequestHub();
            if(phase==10 && First) session.Travel.RequestFactory();
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
            if(phase<0 || acknowledged==phase || session.LocalActor==null || session.Travel.Pending || session.Pause.Revision<0) return;
            if(phase==4 && Host) return;
            bool expectedPause=phase is 2 or 3 or 4 or 6 or 8;
            bool expectedMenu=phase is 6 or 7;
            bool expectedInventory=phase is 8 or 9 or 10;
            string expectedLevel=First && phase>=1 && phase!=9 || Host && phase is 1 or 2?"factory:0":"hub";
            if(session.LocalActor.level!=expectedLevel || session.Pause.IsPaused("factory:0")!=expectedPause || session.Pause.IsPaused("hub") ||
                session.Pause.PauseOnMenu!=expectedMenu || session.Pause.PauseOnInventory!=expectedInventory) return;
            Acknowledge();
        }
        private void Acknowledge()
        {acknowledged=phase;NetworkClient.Send(new PauseProbeAck {phase=phase});}
        private void Finish(bool success)
        {
            if(finished) return;finished=true;exitCode=success?0:1;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                detail=$"Контракт сети паузы: phase={phase}, ack={acknowledged}, revision={runtime.Session.Pause.Revision}, level={runtime.Session.LocalActor?.level}, factoryPaused={runtime.Session.Pause.IsPaused("factory:0")}. Симуляция не входит в этот тест."},true));
            Invoke(nameof(Exit),1);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
