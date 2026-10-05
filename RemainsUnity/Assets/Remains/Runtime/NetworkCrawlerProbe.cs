using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct CrawlerFixture : NetworkMessage {public string player,item;public int generation,initialCaps,loot;public float initialHealth;}
    public struct CrawlerObserved : NetworkMessage {public bool paused;public int contacts;}
    public struct CrawlerFinish : NetworkMessage {public bool success;public string detail;}
    // Opt-in isolated probe. It places only the test player; the authored creature is not replaced or scripted.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkCrawlerProbe : MonoBehaviour
    {
        private string NpcId=>runtime.Options.Smoke.Contains("-office-",StringComparison.Ordinal)?
            "factory:0:original:plant:53:lAIXKaUQC2e4JtW5":"factory:0:original:plant:18:QyxFODdq6EPvuE6L";
        private PrototypeRuntime runtime;
        private CrawlerFixture fixture;
        private readonly HashSet<string> contacts=new();
        private readonly HashSet<string> shots=new();
        private float deadline,pausedAt,remaining,initialNpcX,frozenCooldown,frozenHealth;
        private Vector2 frozenPosition;
        private bool frozenBusy;
        private string pausedAttack,claim;
        private string capture;
        private bool traveled,installed,pauseSent,resumed,paused,claimed,sent,finished,approached;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.ProbePeers==1 || runtime.Options.Character=="session-client-1";
        private ActorState Npc=>runtime.Session.World?.Find(NpcId);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+65;value.CommandSource=Command;
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка радтаракана требует отдельный пустой каталог сохранений.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<CrawlerFixture>(message=>{fixture=message;initialNpcX=Npc?.x??0;if(Source) capture="encounter";});
                NetworkClient.RegisterHandler<CrawlerFinish>(message=>Finish(message.success,message.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<CrawlerObserved>((connection,message)=>
            {
                var player=value.Session.ConnectedActor(connection);var body=value.Session.ServerWorld.Find(NpcId);
                bool valid=player?.id==fixture.player && message.paused && message.contacts>0 && body is {actorType:"radroach",down:true,health:<=0} &&
                    string.IsNullOrEmpty(body.weapon) && body.inventory.Count==0 && body.resolvedAttacks.Any(shots.Contains) &&
                    player.health<fixture.initialHealth && player.resolvedAttacks.Any(id=>contacts.Contains(id)) &&
                    InventoryJournal.Count(player,"caps")==fixture.initialCaps+fixture.loot && !value.Session.ServerWorld.pendingGrants.Any(g=>g.containerId==InventorySource.BodyId(NpcId));
                if(valid)
                {
                    value.Session.CommitWorld();var saved=value.Session.Saves.LoadWorld(false);
                    valid=saved.Find(NpcId) is {down:true,health:<=0} dead && dead.inventory.Count==0 &&
                        InventoryJournal.Count(saved.Find(player.id),"caps")==fixture.initialCaps+fixture.loot &&
                        RoomContentProbePersistence.UnchangedOnReinstall(saved,body.level);
                }
                NetworkServer.SendToAll(new CrawlerFinish {success=valid,detail=valid?
                    "Авторский радтаракан подошёл, переждал подтверждённую паузу и нанёс естественный урон пострадавшему; побеждён настоящим выстрелом, единый лут тела подтверждён и смерть/выдача сохранены.":
                    $"Разошёлся радтаракан: body={body?.down}/{body?.health}, items={body?.inventory.Count}, player={player?.health}/{fixture.initialHealth}, caps={(player==null?-1:InventoryJournal.Count(player,"caps"))}/{fixture.initialCaps+fixture.loot}, contacts={message.contacts}, paused={message.paused}."});
            });
            value.Session.AttackReceived+=attack=>
            {
                if(attack.attacker==fixture.player && attack.weapon=="pistol") shots.Add(attack.id);
                if(attack.attacker!=NpcId) return;
                if(attack.naturalAttackType!="radroach" || attack.naturalAttackTarget!=fixture.player || !string.IsNullOrEmpty(attack.weapon)) {Finish(false,"Радтаракан атаковал предметом оружия, чужим видом или потерял адресата.");return;}
                contacts.Add(attack.id);
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;var npc=Npc;
            if(!Source || finished || !resumed || actor?.id!=fixture.player || actor.generation!=fixture.generation || npc==null || !actor.resolvedAttacks.Any(contacts.Contains)) return default;
            if(!npc.down)
            {
                if(!Anatomy.TryRegion(npc,"thorax",out var torso,out _)) throw new InvalidOperationException("Радтаракан без области thorax: "+npc.id);
                return new PlayerCommand {weapon="pistol",fire=true,hasAim=true,aim=torso,
                    reload=ProbeWeaponSelection.Magazine(actor,"pistol").rounds==0};
            }
            if(Mathf.Abs(actor.x-npc.x)>1) return new PlayerCommand {movement=Vector2.right*Mathf.Sign(npc.x-actor.x)};
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут радтаракана: fixture={installed}/{fixture.player}, source={Source}, npc={Npc?.health}/{Npc?.Position}/{Npc?.owner}/{Npc?.attackRemaining}, contacts={contacts.Count}, paused={paused}, claimed={claimed}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(Source && !traveled) {traveled=true;session.Travel.RequestFactory();}
            if(Host && !installed)
            {
                var source=session.ServerWorld.actors.SingleOrDefault(a=>a.player && a.connected && (runtime.Options.ProbePeers==1 || a.character=="session-client-1"));
                if(source?.level=="factory:0" && !session.Travel.Pending && session.World.Find(source.id)?.level==source.level) Setup(source);
            }
            if(!Source || actor.id!=fixture.player || actor.generation!=fixture.generation || Npc is not { } npc) return;
            approached |= Mathf.Abs(npc.x-initialNpcX)>.25f;
            if(npc.attackBusy && npc.owner==session.LocalOwner && !pauseSent)
            {pauseSent=true;session.Pause.SetManual(true);}
            if(pauseSent && session.Pause.IsPaused(npc.level) && !resumed)
            {
                if(pausedAt==0)
                {
                    // A remote pause has a round trip: preparation may finish before confirmation.
                    // Test the actual confirmed state, without lengthening the authored attack for the probe.
                    pausedAt=Time.unscaledTime;remaining=npc.attackRemaining;pausedAttack=npc.attackId;
                    frozenBusy=npc.attackBusy;frozenCooldown=npc.attackCooldown;frozenPosition=npc.Position;frozenHealth=actor.health;
                    capture=frozenBusy?"preparation-paused":"encounter-paused";
                }
                if(npc.attackBusy!=frozenBusy || frozenBusy && npc.npcWeaponAction?.kind!=NpcNaturalAttack.Kind || npc.attackId!=pausedAttack ||
                    Mathf.Abs(remaining-npc.attackRemaining)>.01f || Mathf.Abs(frozenCooldown-npc.attackCooldown)>.01f ||
                    Vector2.Distance(frozenPosition,npc.Position)>.01f || actor.health!=frozenHealth)
                {Finish(false,"Подтверждённая пауза изменила движение, действие или здоровье встречи.");return;}
                if(Time.unscaledTime-pausedAt>.65f) {paused=true;resumed=true;session.Pause.SetManual(false);}
            }
            if(npc.down && Mathf.Abs(actor.x-npc.x)<=1 && !claimed)
            {
                if(!approached || contacts.Count==0 || actor.health>=fixture.initialHealth || npc.inventory.Count!=1)
                {Finish(false,"Существо побеждено до настоящего подхода/укуса или потеряло единый инвентарь.");return;}
                claimed=true;capture="body";claim=session.RequestTrackedClaim(InventorySource.BodyId(npc.id),fixture.item);
            }
            if(claimed && !sent && npc.inventory.Count==0 && InventoryJournal.Count(actor,"caps")==fixture.initialCaps+fixture.loot &&
                !session.World.pendingGrants.Any(g=>g.containerId==InventorySource.BodyId(npc.id)))
            {sent=true;capture="granted";NetworkClient.Send(new CrawlerObserved {paused=paused,contacts=contacts.Count});}
        }
        private void Setup(ActorState player)
        {
            var session=runtime.Session;var npc=session.ServerWorld.Find(NpcId);var room=runtime.Geometry.Get("factory:0");
            if(npc is not {actorType:"radroach",down:false,health:8} || npc.inventory.Count!=1 || npc.inventory[0].definition!="caps")
                throw new InvalidOperationException("Проверка не получила сохранённый вид/расположение/инвентарь радтаракана.");
            // Enter the creature's ordinary close-detection radius from behind, leaving room for its approach.
            // The office's first crawler now shares its shelf with the restored tool container.
            // Use the second authored crawler and its clear left side; never move the creature or its props.
            var position=npc.Position+Vector2.right*(runtime.Options.Smoke.Contains("-office-",StringComparison.Ordinal)?-1.1f:1.1f);var hull=Anatomy.Movement(player);
            if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position))
                throw new InvalidOperationException("Нет безопасной опоры проверочного игрока у исходного радтаракана: "+position);
            player.Position=position;player.Velocity=Vector2.zero;player.climbing=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
            fixture=new CrawlerFixture {player=player.id,generation=player.generation,item=npc.inventory[0].id,loot=npc.inventory[0].count,
                initialCaps=InventoryJournal.Count(player,"caps"),initialHealth=player.health};
            installed=true;initialNpcX=npc.x;capture="encounter";session.CommitWorld();NetworkServer.SendToAll(fixture);
        }
        private void LateUpdate()
        {
            if(capture==null || !runtime.Options.Smoke.Contains("-frames-") || Npc==null) return;
            if(runtime.Session.LocalActor?.level!=Npc.level) {capture=null;return;}
            var camera=Camera.main;var saved=camera.transform.position;
            try
            {
                camera.transform.position=new Vector3(Npc.x,Npc.y+2,saved.z);
                NetworkFrameCapture.Save(camera,runtime.Options.Report+".crawler-"+capture+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
            }
            finally {camera.transform.position=saved;capture=null;}
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && (contacts.Count==0 || Npc is not {down:true,health:<=0} || Npc.inventory.Count!=0))
            {success=false;detail="Локальная реплика не подтвердила события/смерть/изъятие лута радтаракана.";}
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,source=Source,
                contacts=contacts.Count,paused=paused,preparationPaused=frozenBusy,approached=approached,claim=claim,npc=NpcId,owner=Npc?.owner,health=Npc?.health??-1},true));
            exitCode=success?0:1;Invoke(nameof(Exit),2);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success,source,paused,preparationPaused,approached;public int contacts;public float health;public string detail,claim,npc,owner;}
    }
}
