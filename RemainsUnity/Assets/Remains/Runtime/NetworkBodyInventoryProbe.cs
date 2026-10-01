using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct BodyInventoryPhase : NetworkMessage {public int phase;public long clickAt;}
    public struct BodyInventoryTried : NetworkMessage {public string item;}
    public struct BodyInventoryReady : NetworkMessage {public bool valid;}
    public struct BodyInventoryFinish : NetworkMessage { }
    public sealed class NetworkBodyInventoryProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,generation;
        private long clickAt;
        private string targetItem;
        private readonly System.Collections.Generic.HashSet<int> claimers=new();
        private bool Race=>runtime.Options.Smoke.Contains("-race-");
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool RemoteRace=>runtime.Options.Smoke.Contains("-remote-");
        private bool Taker=>Race?!Source && (!RemoteRace || !Host):Host;
        private float deadline,returnAt;
        private bool traveled,disconnected,returned,opened,clicked,gifted,verifiedOffline,ready,acknowledged,finished;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        private const string BodyId="player:session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;
            value.CommandSource=()=>
            {
                if(!Resume && (Taker || Host) && phase==2 && !opened) {opened=true;return new PlayerCommand {inventory=true};}
                return default;
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<BodyInventoryPhase>(message=>{phase=message.phase;clickAt=message.clickAt;});
                NetworkClient.RegisterHandler<BodyInventoryFinish>(_=>Finish(Valid(value.Session.World),Resume?"Новые процессы сохранили результаты обмена с телом.":Race?"Одновременные клики по стопке тела дали одного получателя; внесение и возвращение владельца сохранены.":"Подбор из тела, внесение и возвращение владельца проверены через UI."));
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<BodyInventoryReady>((connection,message)=>
                {if(value.Session.ConnectedActor(connection)?.id==BodyId) ready=message.valid;});
                NetworkServer.RegisterHandler<BodyInventoryTried>((connection,message)=>
                {if(value.Session.ConnectedActor(connection) is {} actor && actor.id!=BodyId && message.item==targetItem) claimers.Add(connection.connectionId);});
            };
        }
        private bool Valid(WorldState world)=>world!=null && world.pendingGrants.Count==0 && world.pendingDeposits.Count==0 &&
            InventoryJournal.Count(world.Find(BodyId),"grenade")== (Race?0:4) && InventoryJournal.Count(world.Find(BodyId),"heal")==6 &&
            (Race?world.actors.Count(a=>InventoryJournal.Count(a,"grenade")==10)==1:InventoryJournal.Count(world.Find("player:session-host"),"grenade")==6) && InventoryJournal.Count(world.Find("player:session-host"),"heal")==4 &&
            world.actors.Sum(a=>InventoryJournal.Count(a,"grenade"))==runtime.Options.ProbePeers*5 &&
            world.actors.Sum(a=>InventoryJournal.Count(a,"heal"))==runtime.Options.ProbePeers*5 &&
            world.containers.Count==1 && world.actors.Count(a=>a.id==BodyId)==1 &&
            (!RemoteRace || InventoryJournal.Count(world.Find("player:session-host"),"grenade")==5) &&
            world.actors.Where(a=>a.player).All(a=>InventoryJournal.Count(a,"p10")==36 && InventoryJournal.Count(a,"batt")==45) &&
            world.actors.SelectMany(a=>a.inventory).Concat(world.containers.SelectMany(c=>c.items)).GroupBy(i=>i.id).All(group=>group.Count()==1);
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут инвентаря тела: phase={phase}, offline={verifiedOffline}, ready={ready}, clicked={clicked}, gift={gifted}.");return;}
            var session=runtime.Session;
            if(Resume)
            {
                if(Host && phase==0 && session.ServerWorld!=null && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
                {
                    if(!Valid(session.ServerWorld)) {Finish(false,"Перезапуск изменил инвентари тела/получателя.");return;}
                    phase=3;NetworkServer.SendToAll(new BodyInventoryFinish());
                }
                return;
            }
            if(Taker && phase==2 && runtime.Hud.InventoryOpen && !clicked) {clicked=true;StartCoroutine(Take());}
            if((Host || Source || Race) && !traveled && session.LocalActor!=null) {traveled=true;session.Travel.RequestFactory();}
            if(Source && phase==1 && !disconnected)
            {generation=session.LocalActor.generation;disconnected=true;session.StopClient();returnAt=Time.unscaledTime+6;return;}
            if(Source && disconnected && !returned && Time.unscaledTime>=returnAt)
            {returned=true;session.StartClient();return;}
            if(Source && returned && session.LocalActor!=null && !acknowledged)
            {
                acknowledged=true;var actor=session.LocalActor;
                bool valid=actor.id==BodyId && actor.generation>generation && actor.down && actor.level=="factory:0" && Valid(session.World);
                if(!valid) {Finish(false,"Возвращение владельца тела потеряло результаты обмена.");return;}
                NetworkClient.Send(new BodyInventoryReady {valid=true});
            }
            if(!Host || session.World==null) return;
            var body=session.ServerWorld.Find(BodyId);
            if(phase==0 && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers &&
                body?.level=="factory:0" && session.LocalActor.level=="factory:0" && (!Race || session.ServerWorld.actors.Where(a=>a.player).All(a=>a.level=="factory:0")))
            {targetItem=body.inventory.Single(i=>i.definition=="grenade").id;phase=1;NetworkServer.SendToAll(new BodyInventoryPhase {phase=1});}
            if(phase==1 && body is {connected:false,down:true})
            {phase=2;clickAt=DateTime.UtcNow.AddSeconds(1).Ticks;NetworkServer.SendToAll(new BodyInventoryPhase {phase=2,clickAt=clickAt});}
            if(phase==2 && (Race?claimers.Count==runtime.Options.ProbePeers-1-(RemoteRace?1:0) && InventoryJournal.Count(body,"grenade")==0:InventoryJournal.Count(session.LocalActor,"grenade")==6) && !gifted)
            {gifted=true;StartCoroutine(Give());}
            if(phase==2 && !body.connected && Valid(session.ServerWorld)) verifiedOffline=true;
            if(verifiedOffline && ready && body.connected && phase==2)
            {phase=3;NetworkServer.SendToAll(new BodyInventoryFinish());}
        }
        private IEnumerator Take()
        {
            if(runtime.Hud.ShownInventorySourceId!=InventorySource.BodyId(BodyId)) {Finish(false,"UI выбрал не инвентарь отключённого тела.");yield break;}
            if(!Race) yield return UiPointerProbe.Click(runtime.Hud.ClaimQuantityRect);
            while(DateTime.UtcNow.Ticks<clickAt) yield return null;
            var item=runtime.Session.World.Find(BodyId).inventory.ElementAtOrDefault(1);
            if(item?.definition!="grenade") {Finish(false,"К моменту клика слот уже не содержал исходную стопку гранат.");yield break;}
            string intended=item.id;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(1));
            NetworkClient.Send(new BodyInventoryTried {item=intended});
        }
        private IEnumerator Give() {if(Race) yield return UiPointerProbe.Click(runtime.Hud.ClaimQuantityRect);yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(0));}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,verifiedOffline=verifiedOffline,claimers=claimers.Count,winner=runtime.Session.World?.actors.FirstOrDefault(a=>InventoryJournal.Count(a,"grenade")==10)?.character},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success,verifiedOffline;public int claimers;public string detail,winner;}
    }
}
