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
    public struct BodyRevivalPhase : NetworkMessage {public int phase;}
    public struct BodyRevivalReady : NetworkMessage {public int stage;}
    public struct BodyRevivalFinish : NetworkMessage { }
    public sealed class NetworkBodyRevivalProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,returnAt,unblockAt;
        private bool traveled,disconnected,returned,ready,opened,clicked,offlineSeen,raisedPending,rejected,finished;
        private bool knocked;
        private string pendingId;
        private Transport blocked;
        private Action<ArraySegment<byte>,int> receive;
        private bool Deposit=>runtime.Options.Smoke.Contains("-deposit-");
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        private const string ActorId="player:session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;
            value.CommandSource=()=>
            {
                if(!Host) return default;
                if(phase==3 && !opened) {opened=true;value.Hud.OpenContainer(InventorySource.BodyId(ActorId));}
                if(phase==4) {value.Hud.CloseContainer();return new PlayerCommand {revive=true,hasAim=true,aim=InventorySource.BodyBounds(value.Session.World.Find(ActorId)).center};}
                return default;
            };
            value.Session.Notice+=text=>{if(text=="Инвентарь этого персонажа недоступен.") rejected=true;};
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<BodyRevivalPhase>(message=>
                {
                    phase=message.phase;
                    if(Source && phase==2)
                    {
                        blocked=value.Session.transport is LatencySimulation latency?latency.wrap:value.Session.transport;
                        receive=blocked.OnClientDataReceived;blocked.OnClientDataReceived=(_,_)=>{};
                        unblockAt=Time.unscaledTime+6;NetworkClient.Send(new BodyRevivalReady {stage=2});
                    }
                });
                NetworkClient.RegisterHandler<BodyRevivalFinish>(_=>Finish(Valid(value.Session.World),Deposit?"Персонаж поднят при ожидающем внесении: старый резерв завершён один раз, новый запрос отклонён.":"Персонаж поднят при ожидающем подборе: старый резерв завершён один раз, новый запрос отклонён."));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<BodyRevivalReady>((connection,message)=>
            {
                if(value.Session.ConnectedActor(connection)?.id!=ActorId) return;
                if(message.stage==0 && value.Session.ConnectedActor(connection).down)
                    connection.Send(new BodyRevivalPhase {phase=10});
                if(message.stage==1 && offlineSeen) {phase=2;NetworkServer.SendToAll(new BodyRevivalPhase {phase=2});}
                if(message.stage==2) phase=3;
            });
        }
        private bool Valid(WorldState world)=>world!=null && world.Find(ActorId) is {down:false,connected:true} source &&
            world.pendingGrants.Count==0 && world.pendingDeposits.Count==0 && InventoryJournal.Count(source,"grenade")== (Deposit?6:4) && InventoryJournal.Count(source,"heal")==5 &&
            InventoryJournal.Count(world.Find("player:session-host"),"grenade")== (Deposit?4:6) && InventoryJournal.Count(world.Find("player:session-host"),"heal")==4 &&
            world.actors.Sum(a=>InventoryJournal.Count(a,"grenade"))==runtime.Options.ProbePeers*5 &&
            world.actors.Sum(a=>InventoryJournal.Count(a,"heal"))==runtime.Options.ProbePeers*5-1 &&
            world.actors.Where(a=>a.player).All(a=>InventoryJournal.Count(a,"p10")==36);
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут поднятия при подборе: phase={phase}, raisedPending={raisedPending}, rejected={rejected}.");return;}
            var session=runtime.Session;
            if(Source && receive!=null && Time.unscaledTime>=unblockAt) {blocked.OnClientDataReceived=receive;receive=null;}
            if((Host || Source) && !traveled && session.LocalActor!=null) {traveled=true;session.Travel.RequestFactory();}
            if(Source && phase==1 && !knocked && session.LocalActor?.level=="factory:0" && !session.Travel.Pending)
            {
                knocked=true;var actor=session.LocalActor;
                var damage=new DamageReceiver(session.Rules.Damage,session.Rules.Knockdown);
                for(int i=0;i<2;i++) damage.Apply(actor,new AttackEvent {id="body-revival-fixture:"+i,attacker=actor.id,weapon="grenade",level=actor.level},true);
                if(!actor.down) {Finish(false,"Подготовка проверки не вызвала нокдаун правилом урона.");return;}
                session.PublishActor(actor);NetworkClient.Send(new BodyRevivalReady {stage=0});
            }
            if(Source && phase==10 && !disconnected)
            {disconnected=true;session.StopClient();returnAt=Time.unscaledTime+1;return;}
            if(Source && disconnected && !returned && Time.unscaledTime>=returnAt) {returned=true;session.StartClient();return;}
            if(Source && returned && session.LocalActor!=null && !ready)
            {ready=true;if(!session.LocalActor.down) {Finish(false,"Для проверки нет тела без сознания.");return;}NetworkClient.Send(new BodyRevivalReady {stage=1});}
            if(!Host || session.World==null) return;
            var body=session.ServerWorld.Find(ActorId);
            if(phase==0 && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers && body?.level=="factory:0" && session.LocalActor.level=="factory:0")
            {phase=1;NetworkServer.SendToAll(new BodyRevivalPhase {phase=1});}
            if(phase==1 && body is {connected:false,down:true}) offlineSeen=true;
            if(phase==3 && runtime.Hud.ContainerOpen && !clicked) {clicked=true;StartCoroutine(Take());}
            if(phase==3)
            {
                string id=Deposit?session.ServerWorld.pendingDeposits.SingleOrDefault(g=>g.containerId==InventorySource.BodyId(ActorId))?.transaction:
                    session.ServerWorld.pendingGrants.SingleOrDefault(g=>g.containerId==InventorySource.BodyId(ActorId))?.transaction;
                if(id!=null) {pendingId=id;phase=4;}
            }
            if(phase==4 && !body.down && !session.World.Find(ActorId).down)
            {
                if(!(Deposit?session.ServerWorld.pendingDeposits.Any(g=>g.transaction==pendingId):session.ServerWorld.pendingGrants.Any(g=>g.transaction==pendingId)) ||
                    (Deposit?InventoryJournal.Count(body,"grenade"):InventoryJournal.Count(session.ServerWorld.Find("player:session-host"),"grenade"))!=5)
                {Finish(false,"Поднятие не пересеклось с ожиданием резервной копии.");return;}
                raisedPending=true;phase=5;
                if(Deposit)
                {
                    if(session.Deposits.Request(InventorySource.BodyId(ActorId),session.LocalActor.inventory.Single(i=>i.definition=="p10").id,1))
                    {Finish(false,"Клиент разрешил новое внесение поднятому персонажу.");return;}
                }
                else session.RequestClaim(InventorySource.BodyId(ActorId),body.inventory.Single(i=>i.definition=="p10").id,1);
            }
            if(phase==5 && raisedPending && rejected && Valid(session.ServerWorld))
            {phase=6;NetworkServer.SendToAll(new BodyRevivalFinish());}
        }
        private IEnumerator Take()
        {
            if(runtime.Hud.ShownInventorySourceId!=InventorySource.BodyId(ActorId)) {Finish(false,"Не открыт нужный инвентарь тела.");yield break;}
            if(Deposit) InventoryTransactionProbe.Deposit(runtime,"grenade",1);
            else InventoryTransactionProbe.Claim(runtime,1,1);
            yield return null;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(receive!=null) {blocked.OnClientDataReceived=receive;receive=null;}
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,raisedPending=raisedPending,rejected=rejected},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success,raisedPending,rejected;public string detail;}
    }
}
