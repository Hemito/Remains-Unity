using System;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    public struct DoorProbeFinished : NetworkMessage {}
    // Isolated test placement only: does not install doors into ordinary worlds.
    public sealed class NetworkDoorProbe : MonoBehaviour
    {
        private const string DoorId="probe:door";
        private PrototypeRuntime runtime;
        private float deadline,openedAt=-1;
        private bool installed,opened,requestedOpen,requestedClose,finished;
        private int accepted;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;
            value.CommandSource=()=>default;
            value.Session.Doors.Finished+=reply=>{if(reply.accepted) accepted++;else Finish(false,reply.reason);};
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<DoorProbeFinished>(_=>
                Finish(opened && value.Session.World.doors.Any(d=>d.id==DoorId && !d.open && d.revision==2) &&
                    (value.Options.Role=="host" || accepted==2),"Открытие и закрытие удалённым участником, обе реплики совпали."));
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,"Таймаут проверки двери.");return;}
            var session=runtime.Session;
            if(session.LocalActor==null) return;
            if(runtime.Options.Role=="host" && !installed)
            {
                var players=session.ServerWorld.actors.Where(a=>a.player && a.connected).ToArray();
                if(players.Length!=2) return;
                var remote=players.Single(a=>a.id!=session.LocalActorId);
                session.ServerWorld.doors.Add(new DoorState {id=DoorId,definition="probe",level=remote.level,
                    x=remote.x+1.7f,y=remote.y,width=.2f,height=2,surfaceTexture="Hub/concrete"});
                installed=true;session.CommitWorld();
            }
            var door=session.World.doors.Find(d=>d.id==DoorId);
            if(door==null) return;
            if(door.open) {opened=true;if(openedAt<0) openedAt=Time.unscaledTime;}
            if(runtime.Options.Role=="host")
            {
                // Allow the reply, sent after the snapshot, to reach the requesting peer.
                if(opened && !door.open && door.revision==2 && Time.unscaledTime-openedAt>2)
                    NetworkServer.SendToAll(new DoorProbeFinished());
                return;
            }
            if(!requestedOpen) {requestedOpen=session.Doors.Request(DoorId,true);return;}
            if(opened && !requestedClose && !session.Doors.Pending && Time.unscaledTime-openedAt>.5f)
                requestedClose=session.Doors.Request(DoorId,false);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                detail=$"{detail} opened={opened}, accepted={accepted}."},true));
            exitCode=success?0:1;Invoke(nameof(Exit),1);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
