using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ReceiptLossPhase : NetworkMessage {public int phase;}
    public struct ReceiptLossReady : NetworkMessage {public int before,after;}
    public struct ReceiptLossFinish : NetworkMessage { }
    // Drops inbound transport delivery after the throw, before the client can apply/save its receipt.
    public sealed class NetworkReceiptLossProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,staleCount;
        private float deadline,disconnectAt,returnAt;
        private bool threw,disconnected,returned,finished,retained,ready,acknowledged,traveled;
        private Action<ArraySegment<byte>,int> receive;
        private Transport blockedTransport;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        private bool Loot=>runtime.Options.Smoke.Contains("-loot-");
        private string Item=>Loot?"caps":"grenade";
        private int Before=>Loot?0:5;
        private int After=>Loot?20:4;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ReceiptLossPhase>(m=>phase=m.phase);
                NetworkClient.RegisterHandler<ReceiptLossFinish>(_=>Finish(true,Loot?"Приём резерва/выдачи потерян: после обрыва и возвращения в тело восстановлены 20 крышек без дубликата.":"Потерян приём результата расхода: устаревший профиль не вернул предмет после отключения и входа в хаб."));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ReceiptLossReady>((connection,m)=>
            {if(value.Session.ConnectedActor(connection)?.character=="session-client-1") ready=m.before==Before && m.after==After;});
        }
        private PlayerCommand Command()
        {
            var session=runtime.Session;
            if(!Source || phase!=1 || threw || session.LocalActor==null) return default;
            threw=true;blockedTransport=session.transport is LatencySimulation latency?latency.wrap:session.transport;
            receive=blockedTransport.OnClientDataReceived;
            blockedTransport.OnClientDataReceived=(_,_)=>{};
            disconnectAt=Time.unscaledTime+1.2f;
            if(Loot)
            {
                var container=session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
                session.RequestClaim(container.id,container.items.Single(i=>i.definition=="caps").id);return default;
            }
            return new PlayerCommand {grenade=true,hasAim=true,aim=session.LocalActor.Position+Vector2.down*3};
        }
        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут результата: phase={phase}, retained={retained}, ready={ready}, stale={staleCount}.");return;}
            if(Source && threw && !disconnected && Time.unscaledTime>=disconnectAt)
            {
                disconnected=true;session.StopClient();returnAt=Time.unscaledTime+3;
                var saved=JsonUtility.FromJson<ActorState>(File.ReadAllText(Path.Combine(session.Saves.DirectoryPath,"character.json")));
                staleCount=InventoryJournal.Count(saved,Item);
                if(staleCount!=Before) Finish(false,"Тест не удержал устаревший локальный профиль до подтверждения операции.");
                return;
            }
            if(Source && disconnected && !returned && Time.unscaledTime>=returnAt)
            {returned=true;blockedTransport.OnClientDataReceived=receive;session.StartClient();return;}
            if(Source && returned && !acknowledged && session.LocalActor!=null)
            {
                acknowledged=true;int count=InventoryJournal.Count(session.LocalActor,Item);
                if(count!=After) {Finish(false,"Устаревший профиль потерял результат операции.");return;}
                NetworkClient.Send(new ReceiptLossReady {before=staleCount,after=count});
            }
            if(Loot && (Host || Source) && !traveled && session.LocalActor!=null)
            {traveled=true;session.Travel.RequestFactory();}
            if(!Host || session.World==null) return;
            if(phase==0 && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {
                if(Loot && session.ServerWorld.actors.Where(a=>a.character=="session-host" || a.character=="session-client-1").Any(a=>a.level!="factory:0")) return;
                phase=1;NetworkServer.SendToAll(new ReceiptLossPhase {phase=1});
            }
            if(phase!=1) return;
            var source=session.ServerWorld.Find("player:session-client-1");
            if(source!=null && !source.connected)
            {
                if(Loot && session.ServerWorld.pendingGrants.Count>0) return;
                if(ActorPresence.IsPresent(source)!=Loot || (!Loot && source.owner!=null) || InventoryJournal.Count(source,Item)!=After || source.down!=Loot)
                {Finish(false,"Отключённая запись хаба потеряла расход или осталась телом с владельцем.");return;}
                retained=true;
            }
            if(!retained || !ready || source?.connected!=true || session.World.grenades.Count!=0) return;
            if(Loot && (session.ServerWorld.actors.Sum(a=>InventoryJournal.Count(a,"caps"))!=20 || session.ServerWorld.containers.Any(c=>c.items.Any(i=>i.definition=="caps"))))
            {Finish(false,"Выдача после обрыва потеряна или продублирована.");return;}
            phase=2;NetworkServer.SendToAll(new ReceiptLossFinish());
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,item=Item,staleItemCount=staleCount},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail,item;public int staleItemCount;}
    }
}
