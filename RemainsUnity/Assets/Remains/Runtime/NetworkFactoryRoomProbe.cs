using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct FactoryRoomReady : NetworkMessage {}
    public struct FactoryRoomFinished : NetworkMessage {}
    // Uses isolated save directories supplied by verify_session; never edits user worlds.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkFactoryRoomProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private readonly HashSet<int> ready=new();
        private int phase;
        private float deadline,entered,xStart,distance;
        private bool finished;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report))
                throw new InvalidOperationException("Проверка завода требует отдельного каталога сохранений и отчёта.");
            if(value.Options.Role=="host")
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка нового завода не заменяет существующий мир.");
                var world=new WorldState();
                world.rooms.Add(new WorldRoom {level="factory:0",definition="base:factory-entry-46"});
                value.Session.Saves.SaveWorld(world);
            }
            value.CommandSource=()=>phase==2?new PlayerCommand {movement=Vector2.right}:default;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FactoryRoomReady>((connection,_)=>
            {if(value.Session.ConnectedActor(connection)?.level=="hub") ready.Add(connection.connectionId);});
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FactoryRoomFinished>(_=>Finish(phase==4,"Вход, движение, контент и возврат обоих участников."));
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,"Таймаут проверки новой комнаты.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null) return;
            if(phase==0)
            {
                if(session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(WorldRooms.Definition(session.World,"factory:0")!="base:factory-entry-46") {Finish(false,"Клиент потерял привязку новой комнаты.");return;}
                phase=1;session.Travel.RequestFactory();
            }
            else if(phase==1 && actor.level=="factory:0" && !session.Travel.Pending)
            {
                var room=runtime.Geometry.Get(actor.level);
                if(room.right!=38.4f || room.top!=20 || session.World.doors.Count(d=>d.level==actor.level)!=3 ||
                    session.World.containers.Count(c=>c.level==actor.level)!=8 || session.World.props.Count(p=>p.level==actor.level)!=4 ||
                    session.World.actors.Count(a=>!a.player && a.level==actor.level)!=1)
                {Finish(false,"Новая комната потеряла геометрию или содержимое на участнике.");return;}
                xStart=actor.x;entered=Time.unscaledTime;phase=2;
            }
            else if(phase==2 && Time.unscaledTime-entered>=1.2f)
            {
                distance=actor.x-xStart;
                if(distance<1 || actor.down || actor.y<0) {Finish(false,"Обычное движение у входа не выполнено.");return;}
                phase=5;
            }
            else if(phase==3 && actor.level=="hub" && !session.Travel.Pending)
            {
                phase=4;NetworkClient.Send(new FactoryRoomReady());
            }
            if(runtime.Options.Role=="host" && ready.Count==runtime.Options.ProbePeers)
            {
                ready.Clear();NetworkServer.SendToAll(new FactoryRoomFinished());
            }
        }
        private void LateUpdate()
        {
            // Capture after all pose/camera updates, before requesting another scene.
            if(finished || phase!=5) return;
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".factory.png",runtime.Hud?.GetComponent<Canvas>());
            phase=3;runtime.Session.Travel.RequestHub();
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,distance=distance},true));
            exitCode=success?0:1;Invoke(nameof(Exit),3);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase;public float distance;}
    }
}
