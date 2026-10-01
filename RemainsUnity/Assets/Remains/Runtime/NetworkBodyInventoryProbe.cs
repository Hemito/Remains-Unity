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
    public struct BodyInventoryPhase : NetworkMessage {public int phase;}
    public struct BodyInventoryReady : NetworkMessage {public bool valid;}
    public struct BodyInventoryFinish : NetworkMessage { }
    public sealed class NetworkBodyInventoryProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,generation;
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
                if(Host && phase==2 && !opened) {opened=true;return new PlayerCommand {inventory=true};}
                return default;
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<BodyInventoryPhase>(message=>phase=message.phase);
                NetworkClient.RegisterHandler<BodyInventoryFinish>(_=>Finish(Valid(value.Session.World),"Подбор из тела, внесение и возвращение владельца проверены через UI."));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<BodyInventoryReady>((connection,message)=>
            {if(value.Session.ConnectedActor(connection)?.id==BodyId) ready=message.valid;});
        }
        private bool Valid(WorldState world)=>world!=null && world.pendingGrants.Count==0 && world.pendingDeposits.Count==0 &&
            InventoryJournal.Count(world.Find(BodyId),"grenade")==4 && InventoryJournal.Count(world.Find(BodyId),"heal")==6 &&
            InventoryJournal.Count(world.Find("player:session-host"),"grenade")==6 && InventoryJournal.Count(world.Find("player:session-host"),"heal")==4 &&
            world.actors.Sum(a=>InventoryJournal.Count(a,"grenade"))==runtime.Options.ProbePeers*5 &&
            world.actors.Sum(a=>InventoryJournal.Count(a,"heal"))==runtime.Options.ProbePeers*5 &&
            world.containers.Count==1 && world.actors.Count(a=>a.id==BodyId)==1;
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут инвентаря тела: phase={phase}, offline={verifiedOffline}, ready={ready}, clicked={clicked}, gift={gifted}.");return;}
            var session=runtime.Session;
            if((Host || Source) && !traveled && session.LocalActor!=null) {traveled=true;session.Travel.RequestFactory();}
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
                body?.level=="factory:0" && session.LocalActor.level=="factory:0")
            {phase=1;NetworkServer.SendToAll(new BodyInventoryPhase {phase=1});}
            if(phase==1 && body is {connected:false,down:true}) phase=2;
            if(phase==2 && runtime.Hud.InventoryOpen && !clicked)
            {clicked=true;StartCoroutine(Take());}
            if(phase==2 && InventoryJournal.Count(session.LocalActor,"grenade")==6 && !gifted)
            {gifted=true;StartCoroutine(Give());}
            if(phase==2 && !body.connected && Valid(session.ServerWorld)) verifiedOffline=true;
            if(verifiedOffline && ready && body.connected && phase==2)
            {phase=3;NetworkServer.SendToAll(new BodyInventoryFinish());}
        }
        private IEnumerator Take()
        {
            if(runtime.Hud.ShownInventorySourceId!=InventorySource.BodyId(BodyId)) {Finish(false,"UI выбрал не инвентарь отключённого тела.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.ClaimQuantityRect);
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(1));
        }
        private IEnumerator Give() {yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(0));}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,verifiedOffline=verifiedOffline},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success,verifiedOffline;public string detail;}
    }
}
