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
        private string claimedItem,claimedDefinition,claimedContainer;
        private int claimedCount,inventoryBefore;
        private bool lootVerified;
        private bool finished,doorRequested,containerRequested,doorOpened,containerOpened;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report))
                throw new InvalidOperationException("Проверка завода требует отдельного каталога сохранений и отчёта.");
            if(value.Options.Role=="host")
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка нового завода не заменяет существующий мир.");
                var world=new WorldState {worldId="2bbb03303d6848758e98eb9a573eac8d"};
                world.rooms.Add(new WorldRoom {level="factory:0",definition="base:factory-entry-46"});
                value.Session.Saves.SaveWorld(world);
            }
            value.CommandSource=Command;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FactoryRoomReady>((connection,_)=>
            {if(value.Session.ConnectedActor(connection)?.level=="hub") ready.Add(connection.connectionId);});
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FactoryRoomFinished>(_=>Finish(phase==4 && doorOpened && containerOpened && (runtime.Options.Role!="host" || lootVerified),"Вход, движение, дверь через E, окно контейнера через E и возврат обоих участников."));
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут новой комнаты: phase={phase}, position={runtime.Session.LocalActor?.Position}.");return;}
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
            else if(phase==6 && actor.x>=18.9f) phase=7;
            else if(phase==7 && actor.y<1.1f) phase=8;
            else if(phase==8 && Door.open) {doorOpened=true;phase=9;}
            else if(phase==9 && runtime.Hud.ContainerOpen) {containerOpened=true;phase=10;}
            else if(phase==11)
            {
                var source=session.World.containers.Single(c=>c.id==claimedContainer);
                if(source.items.Any(i=>i.id==claimedItem) || session.World.pendingGrants.Any(g=>g.containerId==claimedContainer)) return;
                if(InventoryJournal.Count(actor,claimedDefinition)!=inventoryBefore+claimedCount)
                {Finish(false,"Подтверждённый лут не совпал с изменением личного инвентаря.");return;}
                lootVerified=true;runtime.Hud.CloseContainer();phase=3;session.Travel.RequestHub();
            }
            else if(phase==3 && actor.level=="hub" && !session.Travel.Pending)
            {
                if(runtime.Options.Role=="host")
                {
                    var saved=session.Saves.LoadWorld(false);
                    if(!lootVerified || saved.containers.Single(c=>c.id==claimedContainer).items.Any(i=>i.id==claimedItem) ||
                        InventoryJournal.Count(saved.Find(actor.id),claimedDefinition)!=inventoryBefore+claimedCount)
                    {Finish(false,"После возврата сохранение потеряло результат подбора.");return;}
                }
                phase=4;NetworkClient.Send(new FactoryRoomReady());
            }
            if(runtime.Options.Role=="host" && ready.Count==runtime.Options.ProbePeers)
            {
                ready.Clear();NetworkServer.SendToAll(new FactoryRoomFinished());
            }
        }
        private DoorState Door=>runtime.Session.World.doors.Single(d=>d.level=="factory:0" && d.definition=="door3");
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;if(actor==null || finished) return default;
            if(phase==2 || phase==6) return new PlayerCommand {movement=Vector2.right};
            if(phase==8)
            {
                if(actor.x>10.4f) return new PlayerCommand {movement=Vector2.left};
                if(runtime.Options.Role=="host" && !doorRequested && !Door.open)
                {
                    var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules);
                    if(target.kind!=InteractionKind.Door || target.id!=Door.id) {Finish(false,"У двери E выбирает другой объект.");return default;}
                    doorRequested=true;return new PlayerCommand {interact=true};
                }
            }
            if(phase==9)
            {
                var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules);
                if(target.kind==InteractionKind.Container && !containerRequested)
                {containerRequested=true;return new PlayerCommand {interact=true};}
                if(!containerRequested) return new PlayerCommand {movement=Vector2.left};
            }
            return default;
        }
        private void LateUpdate()
        {
            // Capture after all pose/camera updates, before requesting another scene.
            if(finished) return;
            if(phase==5)
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".factory.png",runtime.Hud?.GetComponent<Canvas>());
                phase=6;
            }
            else if(phase==10)
            {
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".container.png",runtime.Hud.GetComponent<Canvas>());
                if(runtime.Options.Role=="host")
                {
                    var source=runtime.Session.World.containers.Single(c=>c.id==runtime.Hud.ShownInventorySourceId);
                    if(source.items.Count==0) {Finish(false,"Фиксированный тестовый мир не создал лут в исходном ящике.");return;}
                    var item=source.items[0];claimedItem=item.id;claimedDefinition=item.definition;claimedCount=item.count;claimedContainer=source.id;
                    inventoryBefore=InventoryJournal.Count(runtime.Session.LocalActor,item.definition);
                    phase=11;StartCoroutine(UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0)));
                }
                else {runtime.Hud.CloseContainer();phase=3;runtime.Session.Travel.RequestHub();}
            }
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,distance=distance,doorOpened=doorOpened,containerOpened=containerOpened,lootVerified=lootVerified,claimedCount=claimedCount},true));
            exitCode=success?0:1;Invoke(nameof(Exit),3);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase;public float distance;public bool doorOpened,containerOpened,lootVerified;public int claimedCount;}
    }
}
