using System;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Content;
using UnityEngine;

namespace Remains.Runtime
{
    public struct DoorProbeFinished : NetworkMessage {}
    // Isolated test placement only: does not install doors into ordinary worlds.
    [DefaultExecutionOrder(1100)] // Observe the camera after InteractionVisuals has published light and door artwork.
    public sealed class NetworkDoorProbe : MonoBehaviour
    {
        private const string DoorId="probe:door";
        private PrototypeRuntime runtime;
        private float deadline,openedAt=-1;
        private bool installed,opened,requestedOpen,requestedClose,finished;
        private int accepted;
        private bool capturedOpen,reinstalled;
        private bool capture;
        private readonly DoorTiming[] timing={new(),new()};
        private DoorLayout authored;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;
            capture=value.Options.Smoke.Contains("-frames-");
            value.CommandSource=Command;
            value.Session.Doors.Finished+=reply=>{if(reply.accepted) accepted++;else Finish(false,reply.reason);};
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<DoorProbeFinished>(_=>
                Finish(opened && value.Session.World.doors.Any(d=>d.id==DoorId && !d.open && d.revision==2) &&
                    (value.Options.Role=="host" || accepted==2) && value.DoorVisuals.SoundChanges==2 && value.DoorVisuals.PlayedSounds==2 && value.Session.World.contentPatches.Contains("probe:doors:v1") &&
                    value.DoorVisuals.Find(DoorId).Artwork.sprite==value.DoorVisuals.Find(DoorId).Definition.Closed,"Открытие и закрытие удалённым участником, обе реплики совпали."));
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
                authored=new DoorLayout {patch="probe:doors:v1",level=remote.level,doors=new[]{new DoorState {id=DoorId,definition="door1",level=remote.level,
                    x=remote.x+1.7f,y=remote.y,width=.8f,height=1.6f,surfaceTexture="door:wood"}}};
                DoorContentInstaller.Apply(session.ServerWorld,authored);
                installed=true;session.CommitWorld();
            }
            var door=session.World.doors.Find(d=>d.id==DoorId);
            if(door==null) return;
            if(capture && door.revision is 1 or 2)
            {
                var sample=timing[(int)door.revision-1];
                if(sample.stateFrame<0)
                {
                    sample.open=door.open;sample.geometryRevision=runtime.Geometry.OcclusionRevision(door.level);
                    sample.stateFrame=Time.frameCount;sample.stateTime=Time.realtimeSinceStartupAsDouble;
                }
            }
            if(door.open)
            {
                opened=true;if(openedAt<0) openedAt=Time.unscaledTime;
            }
            if(runtime.Options.Role=="host")
            {
                if(opened && !reinstalled)
                {
                    reinstalled=true;
                    if(DoorContentInstaller.Apply(session.ServerWorld,authored) || !session.ServerWorld.doors.Find(d=>d.id==DoorId).open)
                    {Finish(false,"Повтор расстановки сбросил открытую дверь.");return;}
                }
                // Allow the reply, sent after the snapshot, to reach the requesting peer.
                if(opened && !door.open && door.revision==2 && Time.unscaledTime-openedAt>2 && (!capture || timing[1].lightFrame>=0))
                    NetworkServer.SendToAll(new DoorProbeFinished());
                return;
            }
        }
        private void LateUpdate()
        {
            if(finished || !capture || runtime.ActiveFactoryLighting==null) return;
            var door=runtime.Session.World?.doors.Find(d=>d.id==DoorId);
            var view=runtime.DoorVisuals.Find(DoorId);
            if(door==null || door.revision is not (1 or 2) || view==null)return;
            var sample=timing[(int)door.revision-1];
            if(sample.stateFrame<0)return;
            double elapsed=(Time.realtimeSinceStartupAsDouble-sample.stateTime)*1000;
            if(sample.lightFrame<0 && runtime.ActiveFactoryLighting.PresentedDoorRevision==sample.geometryRevision)
            {sample.lightFrame=Time.frameCount;sample.lightMs=elapsed;}
            if(sample.artworkFrame<0 && view.Artwork.enabled && view.Artwork.sprite==(sample.open?view.Definition.Opened:view.Definition.Closed))
            {sample.artworkFrame=Time.frameCount;sample.artworkMs=elapsed;}
            if(sample.open && !capturedOpen && sample.artworkFrame>=0 && sample.lightFrame>=0)
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".door-open.png",runtime.Hud.GetComponent<Canvas>());
                capturedOpen=true;
            }
        }
        private Remains.Gameplay.PlayerCommand Command()
        {
            var session=runtime.Session;
            if(finished || runtime.Options.Role=="host" || session.LocalActor==null ||
                session.World.doors.Find(d=>d.id==DoorId)==null) return default;
            var cursor=session.World.doors.Find(d=>d.id==DoorId).Bounds.center;
            if(!requestedOpen) {requestedOpen=true;return new Remains.Gameplay.PlayerCommand {interact=true,hasAim=true,aim=cursor};}
            if(opened && !requestedClose && !session.Doors.Pending && Time.unscaledTime-openedAt>.5f && (!capture || capturedOpen))
            {requestedClose=true;return new Remains.Gameplay.PlayerCommand {interact=true,hasAim=true,aim=cursor};}
            return default;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && capture)
                success=capturedOpen && timing[0].open && !timing[1].open &&
                    timing.All(sample=>sample.lightFrame>=sample.stateFrame && sample.stateFrame>=0 && sample.artworkFrame==sample.lightFrame);
            if(success && capture)
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".door-closed.png",runtime.Hud.GetComponent<Canvas>());
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                timing=capture?timing:null,
                detail=$"{detail} opened={opened}, accepted={accepted}, visualChanges={runtime.DoorVisuals.SoundChanges}, sounds={runtime.DoorVisuals.PlayedSounds}."},true));
            exitCode=success?0:1;Invoke(nameof(Exit),1);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class DoorTiming
        {
            public bool open;
            public long geometryRevision;
            public int stateFrame=-1,lightFrame=-1,artworkFrame=-1;
            public double stateTime,lightMs,artworkMs;
        }
        [Serializable] private sealed class Report {public bool success;public string detail;public DoorTiming[] timing;}
    }
}
