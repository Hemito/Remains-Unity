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
    public struct WorkshopLootFixture : NetworkMessage {public string source,item;public int generation;}
    public struct WorkshopLootObserved : NetworkMessage {public string item,owner;public bool defeated,opened,granted;}
    public struct WorkshopLootFinish : NetworkMessage {public bool success;public string detail;}
    // An isolated combat fixture: only the player is placed near an actual authored NPC.
    // Enemy health, position, controller and loot are untouched; production input and UI do the rest.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkWorkshopLootProbe : MonoBehaviour
    {
        private const string NpcId="factory:0:original:plant:18:bU92TKI1pfiaqh6L";
        private PrototypeRuntime runtime;
        private WorkshopLootFixture fixture;
        private float deadline;
        private bool traveled,installed,defeated,opened,clicked,granted,observed,finished;
        private string capture;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.ProbePeers==1 || runtime.Options.Character=="session-client-1";
        private ActorState Npc=>runtime.Session.World.Find(NpcId);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверка лута цеха требует отдельного каталога сохранений.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка лута цеха не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<WorkshopLootFixture>(message=>fixture=message);
                NetworkClient.RegisterHandler<WorkshopLootFinish>(message=>Finish(message.success,message.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<WorkshopLootObserved>((connection,message)=>
            {
                var player=value.Session.ConnectedActor(connection);var body=value.Session.ServerWorld.Find(NpcId);
                if(player?.id!=fixture.source || observed) return;
                observed=true;
                var received=player.inventory.SingleOrDefault(i=>i.id==fixture.item);
                bool valid=message.item==fixture.item && message.defeated && message.opened && message.granted && body is {down:true,health:<=0} &&
                    body.resolvedAttacks.Count>0 && body.owner==player.owner && body.owner==message.owner &&
                    received?.definition=="pistol" && received.weaponState?.rounds==6 && body.inventory.Count==2 &&
                    !body.inventory.Any(i=>i.id==fixture.item) && !value.Session.ServerWorld.pendingGrants.Any();
                if(valid)
                {
                    value.Session.CommitWorld();var saved=value.Session.Saves.LoadWorld(false);
                    valid=saved.Find(NpcId) is {down:true,health:<=0} savedBody && savedBody.inventory.Count==2 &&
                        !savedBody.inventory.Any(i=>i.id==fixture.item) && saved.Find(player.id).inventory.Any(i=>i.id==fixture.item && i.weaponState?.rounds==6);
                }
                NetworkServer.SendToAll(new WorkshopLootFinish {success=valid,detail=valid?
                    "Исходный рейдер цеха побеждён настоящими выстрелами; E открыл его единый инвентарь, клик выдал пистолет с магазином ровно один раз, результат сохранён.":
                    $"Разошлись бой/инвентарь/владелец/сохранение: down={body?.down}, items={body?.inventory.Count}, received={received?.id}, owner={body?.owner}/{message.owner}."});
            });
        }
        private PlayerCommand Command()
        {
            if(finished || !Source || fixture.source==null || runtime.Session.LocalActor is not { } actor ||
                actor.generation!=fixture.generation || Npc is not { } npc) return default;
            if(!npc.down) return new PlayerCommand {weapon="pistol",hasAim=true,aim=npc.Position+Vector2.up*.65f,fire=true,
                reload=ProbeWeaponSelection.Magazine(actor,"pistol").rounds==0,heal=actor.health<60 && InventoryJournal.Count(actor,"heal")>0};
            if(Mathf.Abs(actor.x-npc.x)>.12f) return new PlayerCommand {movement=Vector2.right*Mathf.Sign(npc.x-actor.x)};
            if(!opened)
            {
                var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules);
                if(target.kind!=InteractionKind.Container || target.id!=InventorySource.BodyId(npc.id))
                    throw new InvalidOperationException($"У тела цеха E выбрал другой объект: kind={target.kind}, id={target.id}.");
                opened=true;return new PlayerCommand {interact=true};
            }
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут лута цеха: installed={installed}, npc={Npc?.health}/{Npc?.Position}, opened={opened}, clicked={clicked}, granted={granted}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(Source && !traveled) {traveled=true;session.Travel.RequestFactory();}
            if(Host && !installed)
            {
                var player=session.ServerWorld.actors.SingleOrDefault(a=>a.player && a.connected &&
                    (runtime.Options.ProbePeers==1 || a.character=="session-client-1"));
                if(player?.level=="factory:0" && !session.Travel.Pending && session.World.Find(player.id)?.level==player.level)
                {
                    try {Setup(player);} catch(InvalidOperationException error) {Finish(false,error.Message);return;}
                }
            }
            if(!Source || fixture.source==null || actor.generation!=fixture.generation || Npc is not { } npc) return;
            if(actor.down) {Finish(false,"Игрок потерял сознание в боевой встрече цеха.");return;}
            if(npc.down && !defeated) {defeated=true;capture="defeated";}
            if(opened && !clicked && runtime.Hud.ContainerOpen)
            {
                if(runtime.Hud.ShownInventorySourceId!=InventorySource.BodyId(npc.id) || npc.inventory[0].id!=fixture.item)
                {Finish(false,"Открыто другое тело или окно использует копию инвентаря.");return;}
                clicked=true;capture="inventory";runtime.StartCoroutine(UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0),followTarget:true));
            }
            if(clicked && actor.inventory.Any(i=>i.id==fixture.item) && !npc.inventory.Any(i=>i.id==fixture.item) &&
                !session.World.pendingGrants.Any(g=>g.containerId==InventorySource.BodyId(npc.id)) && !granted)
            {
                granted=true;runtime.Hud.CloseContainer();capture="granted";
                NetworkClient.Send(new WorkshopLootObserved {item=fixture.item,owner=npc.owner,defeated=defeated,opened=opened,granted=true});
            }
        }
        private void Setup(ActorState player)
        {
            var session=runtime.Session;var npc=session.ServerWorld.Find(NpcId);var room=runtime.Geometry.Get("factory:0");
            if(WorldRooms.Definition(session.ServerWorld,"factory:0")!=NewGameWorld.FactoryDefinitionId || npc==null || npc.down || npc.inventory.Count!=3)
                throw new InvalidOperationException("Проверка не получила обычного исходного рейдера и инвентарь цеха.");
            var start=npc.Position+Vector2.left*1.6f;var hull=Anatomy.Movement(player);
            if(!ActorTerrainMotion.IsClear(room,hull,start) || !ActorTerrainMotion.Supported(room,hull,start))
                throw new InvalidOperationException("Проверочная позиция игрока у рейдера цеха не поддержана: "+start);
            player.Position=start;player.Velocity=Vector2.zero;player.climbing=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
            fixture=new WorkshopLootFixture {source=player.id,generation=player.generation,item=WeaponInventory.Selected(npc).id};
            installed=true;session.CommitWorld();NetworkServer.SendToAll(fixture);capture="encounter";
            Debug.Log($"Боевой стенд цеха: source={player.id}, npc={npc.id}, originalPosition={npc.Position}, inventoryItem={fixture.item}. Изменено только проверочное положение игрока.");
        }
        private void LateUpdate()
        {
            if(capture==null || !runtime.Options.Smoke.Contains("-frames-") || Npc==null) return;
            var camera=Camera.main;var saved=camera.transform.position;
            try
            {
                camera.transform.position=new Vector3(Npc.x,Npc.y+3,saved.z);
                NetworkFrameCapture.Save(camera,runtime.Options.Report+".workshop-loot-"+capture+".png",runtime.Hud.GetComponent<Canvas>());
            }
            finally {camera.transform.position=saved;capture=null;}
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,source=Source,
                defeated=defeated,opened=opened,granted=granted,npcOwner=Npc?.owner,item=fixture.item},true));
            exitCode=success?0:1;Invoke(nameof(Exit),2);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success,source,defeated,opened,granted;public string detail,npcOwner,item;}
    }
}
