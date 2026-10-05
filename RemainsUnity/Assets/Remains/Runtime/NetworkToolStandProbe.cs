using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Runtime
{
    public struct ToolStandFixture : NetworkMessage { public string pin; public int pins, hostGeneration, clientGeneration; }
    public struct ToolStandOpened : NetworkMessage { }
    public struct ToolStandClaim : NetworkMessage { public bool remaining; }
    public struct ToolStandObserved : NetworkMessage { public bool remaining, valid; public int attacks; }
    public struct ToolStandFinish : NetworkMessage { public bool success; public string detail; }

    // Only player placement is isolated. The authored container, loot, input/UI and claims are production systems.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkToolStandProbe : MonoBehaviour
    {
        private bool Office=>runtime.Options.Smoke.StartsWith("office-cabinet",StringComparison.Ordinal);
        private string StandId=>Office?"factory:0:original:plant:53:U7xbZnkwspR6bhob":"factory:0:original:plant:18:A1LGxhvIcc83ZfC2";
        private string Label=>Office?"Офисная картотека":"Стенд инструментов";
        private PrototypeRuntime runtime;
        private FactoryOfficeSeamProbe officeSeam;
        private ToolStandFixture fixture;
        private readonly HashSet<int> opened=new(), observed=new();
        private float deadline;
        private float settledSince=-1;
        private bool traveled, installed, opening, ready, claiming, remaining, pointerCheck, reported, finished;
        private int attacks, rounds, stage;
        private string capture;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.ProbePeers==1 || runtime.Options.Character=="session-client-1";
        private int ExpectedGeneration=>Host?fixture.hostGeneration:fixture.clientGeneration;
        private ContainerState Stand=>runtime.Session.World.containers.SingleOrDefault(c=>c.id==StandId);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+95;
            if(Office) officeSeam=new FactoryOfficeSeamProbe(value);
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException(Label+": требуется отдельный каталог сохранений.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка стенда не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Session.AttackReceived+=a=>{if(a.attacker==value.Session.LocalActorId) attacks++;};
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ToolStandFixture>(message=>fixture=message);
                NetworkClient.RegisterHandler<ToolStandClaim>(message=>
                {remaining=message.remaining;reported=false;claiming=true;StartCoroutine(Claim());});
                NetworkClient.RegisterHandler<ToolStandFinish>(message=>Finish(message.success,message.detail));
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<ToolStandOpened>((connection,_)=>opened.Add(connection.connectionId));
                NetworkServer.RegisterHandler<ToolStandObserved>((connection,message)=>Observe(connection,message));
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null || finished) return default;
            if(Office && !officeSeam.ContainerReady) return officeSeam.Command();
            if(actor.weapon!="pistol") return new PlayerCommand {weapon="pistol"};
            if(fixture.pin!=null && actor.generation==ExpectedGeneration)
            {
                if(actor.level=="factory:0" && settledSince>=0 && Time.unscaledTime-settledSince>=2.5f && Stand is { } stand &&
                    !runtime.Session.Travel.Pending && !runtime.Hud.ContainerOpen && !ready)
                {
                    var cursor=((InventorySource)stand).Bounds.center;
                    var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,cursor);
                    if(target.kind!=InteractionKind.Container || target.id!=StandId)
                        throw new InvalidOperationException($"E стенда выбрал {target.kind}/{target.id}, actor={actor.Position}, cursor={cursor}.");
                    opening=true;return new PlayerCommand {interact=true,hasAim=true,aim=cursor};
                }
                var mouse=Mouse.current;
                if(pointerCheck && mouse?.name=="SessionProbeMouse" && mouse.leftButton.isPressed)
                    return new PlayerCommand {fire=true,hasAim=true,aim=actor.Position+Vector2.left*5,hasPointer=true,pointer=mouse.position.ReadValue()};
                if(actor.health<60 && InventoryJournal.Count(actor,"heal")>0) return new PlayerCommand {heal=true};
            }
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут стенда: stage={stage}, installed={installed}, ready={ready}, claiming={claiming}, remaining={remaining}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(!traveled) {traveled=true;session.Travel.RequestFactory();}
            if(Office)
            {
                officeSeam.Tick();
                if(officeSeam.Failure!=null) {Finish(false,officeSeam.Failure);return;}
                if(!officeSeam.ContainerReady) return;
            }
            if(Host && !installed && session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level=="factory:0") &&
                session.World.actors.Where(a=>a.player && a.connected).All(a=>a.level=="factory:0") && !session.Travel.Pending)
            {
                try {Setup();} catch(InvalidOperationException error) {Finish(false,error.Message);return;}
            }
            if(fixture.pin==null || actor.generation!=ExpectedGeneration || actor.level!="factory:0" || Stand==null) return;
            if(settledSince<0) settledSince=Time.unscaledTime;
            if(actor.down) {Finish(false,"Игрок потерял сознание у стенда.");return;}
            if(!PolicyValid(Stand)) {Finish(false,Label+": нарушены физическое тело, масса или положение.");return;}
            if(opening && runtime.Hud.ContainerOpen && !ready)
            {
                if(runtime.Hud.ShownInventorySourceId!=StandId || runtime.Hud.InventoryOpen || runtime.Hud.BlocksGameplayInput)
                {Finish(false,"E стенда открыл другое окно или заблокировал движение.");return;}
                rounds=WeaponAmmunition.Magazine(actor,"pistol").rounds;ready=true;capture="open";
                NetworkClient.Send(new ToolStandOpened());
            }
            if(Host && stage==0 && opened.Count==runtime.Options.ProbePeers)
            {stage=1;NetworkServer.SendToAll(new ToolStandClaim());}
            if(claiming && !reported && session.World.pendingGrants.Count==0 && !Stand.items.Any(i=>i.id==fixture.pin) &&
                (!remaining || Stand.items.Count==0))
            {
                var pins=session.World.actors.Where(a=>a.player).SelectMany(a=>a.inventory).Where(i=>i.id==fixture.pin).ToArray();
                bool valid=pins.Length==1 && pins[0].count==fixture.pins && attacks==0 &&
                    WeaponAmmunition.Magazine(actor,"pistol").rounds==rounds;
                reported=true;capture=remaining?"empty":"claimed";
                NetworkClient.Send(new ToolStandObserved {remaining=remaining,valid=valid,attacks=attacks});
            }
        }
        private void Setup()
        {
            var session=runtime.Session;var stand=session.ServerWorld.containers.Single(c=>c.id==StandId);
            if(!PolicyValid(stand) || stand.items.All(i=>i.definition!="pin") || !Office && stand.items.All(i=>i.definition!="rep"))
                throw new InvalidOperationException("Обычный authored стенд отсутствует или не получил исходный Unity-лут.");
            var room=runtime.Geometry.Get("factory:0");
            foreach(var player in session.ServerWorld.actors.Where(a=>a.player && a.connected))
            {
                var start=new Vector2(stand.x+(player.character=="session-host"?-.95f:.95f),Office?7.2f:.8f);var hull=Anatomy.Movement(player);
                if(!ActorTerrainMotion.IsClear(room,hull,start) || !ActorTerrainMotion.Supported(room,hull,start))
                    throw new InvalidOperationException("Проверочное положение игрока у стенда не имеет опоры: "+start);
                player.Position=start;player.Velocity=Vector2.zero;player.climbing=false;player.pose="idle";player.poseTime=0;
                player.generation++;player.revision++;
            }
            var pin=stand.items.Single(i=>i.definition=="pin");fixture=new ToolStandFixture {pin=pin.id,pins=pin.count};
            fixture.hostGeneration=session.ServerWorld.actors.Single(a=>a.character=="session-host").generation;
            fixture.clientGeneration=session.ServerWorld.actors.SingleOrDefault(a=>a.character=="session-client-1")?.generation??0;
            installed=true;session.CommitWorld();NetworkServer.SendToAll(fixture);
            Debug.Log($"Изолированная проверка стенда: изменено только положение игроков, stand={stand.id}, pin={pin.id}, quantity={pin.count}.");
        }
        private IEnumerator Claim()
        {
            // Retain two complete rendered frames of the simultaneous open windows.
            yield return null;yield return null;
            if(remaining && !Source) yield break;
            pointerCheck=true;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            if(!remaining)
            {
                int index=Stand.items.FindIndex(i=>i.id==fixture.pin);
                // A completed racing claim may already have removed the cell on this observer.
                if(index>=0) yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(index),followTarget:true);
            }
            else
            {
                while(Stand.items.Count>0)
                {
                    string item=Stand.items[0].id;
                    yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0),followTarget:true);
                    while(Stand.items.Any(i=>i.id==item) || runtime.Session.World.pendingGrants.Count>0) yield return null;
                }
            }
            pointerCheck=false;
        }
        private void Observe(NetworkConnectionToClient connection,ToolStandObserved message)
        {
            if((stage==2)!=message.remaining || !observed.Add(connection.connectionId)) return;
            if(!message.valid || message.attacks!=0) {NetworkServer.SendToAll(new ToolStandFinish {detail="Клик стенда выстрелил или продублировал стопку."});return;}
            if(observed.Count!=runtime.Options.ProbePeers) return;
            observed.Clear();
            if(stage==1) {stage=2;NetworkServer.SendToAll(new ToolStandClaim {remaining=true});return;}
            var session=runtime.Session;var world=session.ServerWorld;var stand=world.containers.Single(c=>c.id==StandId);
            bool valid=PolicyValid(stand) && stand.items.Count==0 && world.pendingGrants.Count==0 &&
                world.actors.Where(a=>a.player).SelectMany(a=>a.inventory).Count(i=>i.id==fixture.pin)==1;
            if(valid)
            {
                session.CommitWorld();var saved=session.Saves.LoadWorld(false);var restored=saved.containers.Single(c=>c.id==StandId);
                valid=PolicyValid(restored) && restored.items.Count==0;
                if(valid)
                {
                    valid=RoomContentProbePersistence.UnchangedOnReinstall(saved,"factory:0");
                }
            }
            stage=3;NetworkServer.SendToAll(new ToolStandFinish {success=valid,detail=valid?
                Label+": общий лут, E и Unity UI, одна выдача полной стопки, клики без выстрела, сохранение пустого содержимого без пополнения; политика тела и массы сохранена.":
                "Сохранение или повторная установка изменили пустой настенный контейнер."});
        }
        private bool PolicyValid(ContainerState stand)=>stand!=null && !stand.held && Mathf.Abs(stand.vx)<.0001f && Mathf.Abs(stand.vy)<.0001f &&
            (Office?stand.definition=="filecab" && stand.hasPhysicalBody && stand.movable && stand.mass==12 &&
                Mathf.Abs(stand.x-79.6f)<.0001f && Mathf.Abs(stand.y-7.2f)<.0001f:
                !stand.hasPhysicalBody && !stand.movable && stand.mass==0 && Mathf.Abs(stand.x-40.8f)<.0001f && Mathf.Abs(stand.y-1.62f)<.0001f);
        private void LateUpdate()
        {
            officeSeam?.Capture();
            if(capture==null || !runtime.Options.Smoke.Contains("-frames-")) return;
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+(Office?".office-cabinet-":".tool-stand-")+capture+".png",runtime.Hud.GetComponent<Canvas>());
            capture=null;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,stage=stage,opened=ready,attacks=attacks,
                pin=fixture.pin,pins=fixture.pins,empty=Stand?.items.Count==0,policyValid=PolicyValid(Stand),seamVerified=officeSeam?.Verified??false},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success,opened,empty,policyValid,seamVerified;public string detail,pin;public int stage,attacks,pins;}
    }
}
