using System;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct NpcDoorFixture : NetworkMessage {public string npc,source,door;public int npcGeneration,sourceGeneration;}
    public struct NpcDoorObserved : NetworkMessage {public bool heard,acknowledged,passed;public string owner;}
    public struct NpcDoorFinish : NetworkMessage {public bool success;public string detail;}
    // Explicit isolated placements around an existing authored factory door.
    // After setup, only a real gunshot and the production NPC controller act.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkNpcDoorProbe : MonoBehaviour
    {
        private const string DoorId="factory:0:original:plant:46:aaChLmLMEdgLxJEr";
        private PrototypeRuntime runtime;
        private NpcDoorFixture fixture;
        private float deadline,passedAt=-1;
        private bool installed,traveled,fired,heard,acknowledged,observed,finished,closedCaptured;
        private string capture;
        private bool Host=>runtime.Options.Role=="host";
        private bool Solo=>runtime.Options.ProbePeers==1;
        private bool Source=>Solo || runtime.Options.Character=="session-client-1";
        private ActorState Npc=>runtime.Session.World.Find(fixture.npc);
        private DoorState Door=>runtime.Session.World.doors.Find(d=>d.id==DoorId);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+50;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверка дверей NPC требует отдельного каталога сохранений.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка NPC не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            value.CommandSource=Command;
            value.Session.Doors.NpcFinished+=(actor,door,reply)=>
            {if(actor==fixture.npc && door==DoorId && reply.accepted) acknowledged=true;};
            value.Session.AttackReceived+=attack=>
            {if(attack.attacker==fixture.source && attack.weapon=="pistol") heard=true;};
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<NpcDoorFixture>(message=>fixture=message);
                NetworkClient.RegisterHandler<NpcDoorFinish>(message=>Finish(message.success,message.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<NpcDoorObserved>((connection,message)=>
            {
                var player=value.Session.ConnectedActor(connection);
                var door=value.Session.ServerWorld.doors.Find(d=>d.id==DoorId);
                var npc=value.Session.ServerWorld.Find(fixture.npc);
                if(player?.id!=fixture.source || observed) return;
                observed=true;
                bool valid=message.heard && message.acknowledged && message.passed && npc!=null &&
                    npc.owner==player.owner && message.owner==player.owner && npc.generation==fixture.npcGeneration && door is {open:true,revision:1};
                var saved=value.Session.Saves.LoadWorld(false);
                valid&=saved!=null && saved.doors.Any(d=>d.id==DoorId && d.open && d.revision==1);
                NetworkServer.SendToAll(new NpcDoorFinish {success=valid,
                    detail=valid?"Исходная дверь завода открыта владельцем NPC после настоящего выстрела; NPC прошёл, результат сохранён.":"Разошлись владелец, подтверждение, проход, версия двери или сохранение."});
            });
        }
        private PlayerCommand Command()
        {
            if(finished || !Source || fixture.npc==null || runtime.Session.LocalActor is not { } actor ||
                actor.generation!=fixture.sourceGeneration || Npc?.generation!=fixture.npcGeneration || fired) return default;
            if(Door is not {open:false,revision:0}) return default;
            fired=true;
            return new PlayerCommand {weapon="pistol",fire=true,hasAim=true,aim=actor.Position+Vector2.up*8};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут двери NPC: fired={fired}, heard={heard}, ack={acknowledged}, npc={Npc?.Position}, owner={Npc?.owner}, memory={Npc?.perception?.hasKnownPosition}, doorOpen={Door?.open}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(Source && !traveled) {traveled=true;session.Travel.RequestFactory();}
            if(Host && !installed)
            {
                var player=session.ServerWorld.actors.SingleOrDefault(a=>a.player && a.connected && (Solo || a.character=="session-client-1"));
                // The server applies travel before the host client receives its world snapshot.
                // Wait for that client-side geometry instead of preparing against the old hub snapshot.
                if(player?.level=="factory:0" && !session.Travel.Pending &&
                    session.World.Find(player.id)?.level==player.level) Setup(player);
            }
            if(!Source || fixture.npc==null || Npc?.generation!=fixture.npcGeneration || actor.generation!=fixture.sourceGeneration) return;
            if(!closedCaptured && Door is {open:false}) {closedCaptured=true;capture="closed";}
            var npc=Npc;var door=Door;
            if(npc==null || door==null) return;
            if(npc.owner!=session.LocalOwner) {Finish(false,"NPC передан другому владельцу во время проверки двери.");return;}
            if(actor.down || npc.down || npc.attackBusy) {Finish(false,"Проверка двери неожиданно перешла в нокдаун или атаку.");return;}
            if(door.open && door.revision!=1) {Finish(false,"Дверь получила повторное изменение.");return;}
            if(door.open && npc.x>door.x+door.width+Anatomy.Movement(npc).width*.5f+.05f && passedAt<0)
            {
                passedAt=Time.unscaledTime;capture="passed";
                var view=runtime.DoorVisuals.Find(door.id);
                if(view==null || view.Artwork.sprite!=view.Definition.Opened || runtime.DoorVisuals.SoundChanges!=1 || runtime.DoorVisuals.PlayedSounds!=1)
                {Finish(false,"Открытие NPC не обновило изображение/звук действующей двери.");return;}
            }
            if(passedAt>=0 && Time.unscaledTime-passedAt>2 && !observed && acknowledged)
            {
                if(!Host) observed=true;
                NetworkClient.Send(new NpcDoorObserved {heard=heard,acknowledged=acknowledged,passed=true,owner=npc.owner});
            }
        }
        private void Setup(ActorState player)
        {
            var session=runtime.Session;
            if(WorldRooms.Definition(session.ServerWorld,"factory:0")!=LegacyFactoryFixture.DefinitionId)
                throw new InvalidOperationException("Проверка двери использует другой завод.");
            var door=session.ServerWorld.doors.Single(d=>d.id==DoorId);
            var npc=session.ServerWorld.actors.Single(a=>a.id=="factory:0:original:plant:46:Pca2ee6SiIQvzRpV");
            var room=runtime.Geometry.Get("factory:0");
            var npcStart=new Vector2(door.x-1.6f,door.y);var playerStart=new Vector2(door.x+door.width+7.2f,door.y);
            foreach(var placement in new[]{(actor:npc,position:npcStart),(actor:player,position:playerStart)})
            {
                var hull=Anatomy.Movement(placement.actor);
                if(!ActorTerrainMotion.IsClear(room,hull,placement.position) || !ActorTerrainMotion.Supported(room,hull,placement.position))
                    throw new InvalidOperationException($"Проверочное размещение у исходной двери не поддержано: actor={placement.actor.id}, position={placement.position}.");
                placement.actor.Position=placement.position;placement.actor.Velocity=Vector2.zero;
                placement.actor.climbing=false;placement.actor.pose="idle";placement.actor.poseTime=0;
                placement.actor.generation++;placement.actor.revision++;
            }
            if(door.open || door.revision!=0) throw new InvalidOperationException("Исходная дверь должна быть закрыта в новом проверочном мире.");
            npc.facing=1;npc.target=null;npc.perception=new NpcPerceptionMemory();npc.attackBusy=false;npc.attackCooldown=40;
            fixture=new NpcDoorFixture {door=door.id,npc=npc.id,source=player.id,npcGeneration=npc.generation,sourceGeneration=player.generation};
            installed=true;session.CommitWorld();
            fixture.npcGeneration=npc.generation;NetworkServer.SendToAll(fixture);
            Debug.Log($"Проверочная расстановка двери NPC: door={door.id}, npc={npc.id}, owner={npc.owner}, source={player.id}, npcPosition={npc.Position}, playerPosition={player.Position}.");
        }
        private void LateUpdate()
        {
            if(capture==null || !runtime.Options.Smoke.Contains("-frames-")) return;
            var camera=Camera.main;var saved=camera.transform.position;var door=Door;
            try
            {
                camera.transform.position=new Vector3(door.x+2.8f,door.y+2.8f,saved.z);
                NetworkFrameCapture.Save(camera,runtime.Options.Report+".npc-door-"+capture+".png",runtime.Hud.GetComponent<Canvas>());
            }
            finally {camera.transform.position=saved;capture=null;}
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,source=Source,heard=heard,
                acknowledged=acknowledged,passed=passedAt>=0,npcOwner=Npc?.owner,doorRevision=Door?.revision??-1},true));
            exitCode=success?0:1;Invoke(nameof(Exit),2);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success,source,heard,acknowledged,passed;public string detail,npcOwner;public long doorRevision;}
    }
}
