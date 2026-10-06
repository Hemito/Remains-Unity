using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Persistence;
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
        private bool Deposit=>runtime.Options.Smoke.Contains("-deposit-");
        private bool Body=>Loot || Deposit;
        private bool sawDepositPending,captured,loadedPending;
        private string recoveryTransaction,pendingItemId,capturedSnapshot,capturedArchive;
        private bool operationReady,operationObserved,observedDebit;
        private int operationBeforeCount=-1,canonicalAfterCount=-1,reconnectItemCount=-1,operationGeneration;
        private string operationActor,operationLevel,observedTransaction,waitingFor="Фаза операции ещё не началась.";
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool Crash=>runtime.Options.Smoke.Contains("-crash-");
        private bool Loot=>runtime.Options.Smoke.Contains("-loot-");
        private string Item=>Loot?"caps":"grenade";
        private int Before=>Loot?0:5;
        private int After=>Loot?20:4;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            value.Session.RecoveryPersisted+=RecoveryPersisted;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ReceiptLossPhase>(m=>phase=m.phase);
                NetworkClient.RegisterHandler<ReceiptLossFinish>(_=>Finish(true,Resume?"Новые процессы завершили сохранённый резерв внесения без повторного списания и дубликата в контейнере.":Deposit?"Потеря подтверждения внесения: старый профиль не вернул предмет после переподключения, резерв завершён ровно один раз.":Loot?"Приём резерва/выдачи потерян: после обрыва и возвращения в тело восстановлены 20 крышек без дубликата.":"Потерян приём результата расхода: устаревший профиль не вернул предмет после отключения и входа в хаб."));
            };
            value.Session.ServerReady+=()=>
            {
                if(Resume)
                {
                    var pending=value.Session.ServerWorld.pendingDeposits;
                    loadedPending=pending.Count==1 && pending[0].item.definition=="grenade" && pending[0].item.count==1;
                    if(loadedPending) recoveryTransaction=pending[0].transaction;
                }
                NetworkServer.RegisterHandler<ReceiptLossReady>((connection,m)=>
                {if(value.Session.ConnectedActor(connection)?.character=="session-client-1") ready=m.before==Before && m.after==After;});
            };
        }
        private PlayerCommand Command()
        {
            var session=runtime.Session;
            if(finished || !Source || phase!=1 || threw || session.LocalActor==null) return default;
            if(!SettledReady(Body?"factory:0":"hub")) return default;
            var actor=session.LocalActor;
            operationBeforeCount=InventoryJournal.Count(actor,Item);
            if(operationBeforeCount!=Before)
            {Finish(false,$"Неверное исходное количество: actor={actor.id}, level={actor.level}, item={Item}, expected={Before}, actual={operationBeforeCount}.");return default;}
            operationReady=true;operationActor=actor.id;operationLevel=actor.level;operationGeneration=actor.generation;
            Debug.Log($"Потеря приёма: действие подготовлено, actor={operationActor}, generation={operationGeneration}, level={operationLevel}, phase={phase}, item={Item}, count={operationBeforeCount}; локация готова, интерфейс свободен, перехода и паузы нет, тело на опоре.");
            threw=true;blockedTransport=session.transport is LatencySimulation latency?latency.wrap:session.transport;
            receive=blockedTransport.OnClientDataReceived;
            blockedTransport.OnClientDataReceived=(_,_)=>{};
            disconnectAt=Time.unscaledTime+1.2f;
            if(Deposit)
            {
                var item=session.LocalActor.inventory.Single(i=>i.definition=="grenade");
                if(!session.Deposits.Request(ContainerState.PrototypeId,item.id,1)) Finish(false,"Запрос внесения не отправлен.");
                return default;
            }
            if(Loot)
            {
                var container=session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
                session.RequestClaim(container.id,container.items.Single(i=>i.definition=="caps").id);return default;
            }
            return new PlayerCommand {grenade=true,hasAim=true,aim=session.LocalActor.Position+Vector2.down*3};
        }
        private bool SettledReady(string level)
        {
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || !NetworkClient.isConnected || !actor.connected || actor.owner!=session.LocalOwner)
            {waitingFor="Нет своего подключённого персонажа.";return false;}
            if(!runtime.LevelPresentationReady || runtime.IsLevelPreparing)
            {waitingFor="Локация ещё готовится.";return false;}
            if(runtime.Hud?.BlocksGameplayInput==true)
            {waitingFor="Интерфейс блокирует игровой ввод.";return false;}
            if(session.Travel.Pending || actor.level!=level)
            {waitingFor=$"Переход не завершён: expected={level}, actual={actor.level}, pending={session.Travel.Pending}.";return false;}
            if(actor.down || session.Pause.IsPaused(actor.level))
            {waitingFor=$"Персонаж или уровень не готовы: down={actor.down}, paused={session.Pause.IsPaused(actor.level)}.";return false;}
            if(actor.climbing || Mathf.Abs(actor.vx)>.001f || Mathf.Abs(actor.vy)>.001f ||
                !ActorTerrainMotion.Supported(runtime.Geometry.Get(actor.level),Anatomy.Movement(actor),actor.Position))
            {waitingFor=$"Нет неподвижной опоры: body={actor.Position}, velocity=({actor.vx};{actor.vy}), climbing={actor.climbing}.";return false;}
            waitingFor=null;return true;
        }
        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут результата: phase={phase}, retained={retained}, ready={ready}, stale={staleCount}, operationReady={operationReady}, canonicalAfter={canonicalAfterCount}, observed={operationObserved}, waiting={waitingFor}, durablePending={captured}, transaction={recoveryTransaction}, snapshot={capturedSnapshot}.");return;}
            if(Resume)
            {
                if(!Host || phase==2 || session.ServerWorld==null) return;
                var world=session.ServerWorld;
                if(!loadedPending) {Finish(false,"Перезапуск не загрузил незавершённый резерв внесения.");return;}
                if(world.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers || world.pendingDeposits.Count!=0) return;
                if(!world.completedTransactions.Contains(recoveryTransaction) || InventoryJournal.Count(world.Find("player:session-client-1"),"grenade")!=4 ||
                    world.actors.Sum(a=>InventoryJournal.Count(a,"grenade"))!=runtime.Options.ProbePeers*5-1 ||
                    world.containers.SelectMany(c=>c.items).Where(i=>i.definition=="grenade").Sum(i=>i.count)!=4)
                {Finish(false,"Восстановление резерва внесения изменило общее количество.");return;}
                phase=2;NetworkServer.SendToAll(new ReceiptLossFinish());return;
            }
            if(!Crash && Source && threw && !disconnected && Time.unscaledTime>=disconnectAt)
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
                reconnectItemCount=count;
                if(count!=After) {Finish(false,$"Устаревший профиль потерял результат операции: actor={session.LocalActor.id}, generation={session.LocalActor.generation}, item={Item}, expected={After}, actual={count}, before={operationBeforeCount}, operationReady={operationReady}.");return;}
                NetworkClient.Send(new ReceiptLossReady {before=staleCount,after=count});
            }
            if(Body && (Host || Source) && !traveled && session.LocalActor!=null && SettledReady("hub"))
            {session.Travel.RequestFactory();traveled=session.Travel.Pending;if(!traveled) Finish(false,"Переход проверки в завод не отправлен.");}
            if(!Host || session.World==null) return;
            if(phase==0 && session.ServerWorld.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {
                if(Body && session.ServerWorld.actors.Where(a=>a.character=="session-host" || a.character=="session-client-1").Any(a=>a.level!="factory:0")) return;
                phase=1;NetworkServer.SendToAll(new ReceiptLossPhase {phase=1});
            }
            if(phase!=1) return;
            var source=session.ServerWorld.Find("player:session-client-1");
            if(!operationObserved && source!=null && InventoryJournal.Count(source,Item)==After)
            {
                operationObserved=true;canonicalAfterCount=InventoryJournal.Count(source,Item);
                if(!Body)
                {
                    var grenade=session.ServerWorld.grenades.Find(value=>value.attacker==source.id);
                    observedTransaction=grenade?.id;
                    observedDebit=observedTransaction!=null && source.inventoryUses.Any(use=>use.id==observedTransaction);
                }
                Debug.Log($"Потеря приёма: подтверждён расход/выдача в мире, actor={source.id}, generation={source.generation}, level={source.level}, item={Item}, count={canonicalAfterCount}, transaction={observedTransaction}, journal={observedDebit}.");
            }
            if(Deposit && source?.connected==true && session.ServerWorld.pendingDeposits.Count>0)
            {
                if(session.ServerWorld.pendingDeposits.Count!=1 || !MatchesPending(session.ServerWorld,session.ServerWorld.pendingDeposits[0]))
                {Finish(false,"Неподтверждённый резерв внесения имеет неверные source/item/количества.");return;}
                var pending=session.ServerWorld.pendingDeposits[0];
                if(recoveryTransaction!=null && (recoveryTransaction!=pending.transaction || pendingItemId!=pending.item.id))
                {Finish(false,"Текущий резерв внесения сменил transaction/item до подтверждения.");return;}
                recoveryTransaction=pending.transaction;pendingItemId=pending.item.id;sawDepositPending=true;
                // The crash marker must witness BOTH a completed exact archive write and
                // the still-pending live transaction. Never create a fixture world here.
                if(Crash && captured && !File.Exists(runtime.Options.Report+".crash-ready"))
                    File.WriteAllText(runtime.Options.Report+".crash-ready",pending.transaction);
            }
            if(source!=null && !source.connected)
            {
                if(Loot && session.ServerWorld.pendingGrants.Count>0 || Deposit && session.ServerWorld.pendingDeposits.Count>0) return;
                if(ActorPresence.IsPresent(source)!=Body || (!Body && source.owner!=null) || InventoryJournal.Count(source,Item)!=After || ActorSessionRules.IsDisconnectedBody(source)!=Body)
                {Finish(false,$"Отключённая запись потеряла результат или состояние тела: actor={source.id}, generation={source.generation}, item={Item}, expected={After}, actual={InventoryJournal.Count(source,Item)}, earlierCanonical={canonicalAfterCount}, transaction={observedTransaction}, journal={observedDebit}.");return;}
                retained=true;
            }
            if(!retained || !ready || source?.connected!=true || session.World.grenades.Count!=0) return;
            if(!operationObserved) {Finish(false,"Мир не подтвердил реальное изменение количества до итогового барьера.");return;}
            if(Loot && (session.ServerWorld.actors.Sum(a=>InventoryJournal.Count(a,"caps"))!=20 || session.ServerWorld.containers.Any(c=>c.items.Any(i=>i.definition=="caps"))))
            {Finish(false,"Выдача после обрыва потеряна или продублирована.");return;}
            if(Deposit && (!sawDepositPending || session.ServerWorld.pendingDeposits.Count!=0 ||
                session.ServerWorld.containers.SelectMany(c=>c.items).Where(i=>i.definition=="grenade").Sum(i=>i.count)!=4 ||
                session.ServerWorld.actors.Sum(a=>InventoryJournal.Count(a,"grenade"))!=runtime.Options.ProbePeers*5-1))
            {Finish(false,"Внесение после обрыва потеряно, продублировано либо не наблюдалось ожидание копии.");return;}
            if(Deposit && !captured) return; // Wait for the pending archive event, not the current selected head.
            phase=2;NetworkServer.SendToAll(new ReceiptLossFinish());
        }
        private static bool MatchesPending(WorldState world,PendingDeposit pending)
        {
            var source=world.Find("player:session-client-1");
            return source?.connected==true && pending!=null && !string.IsNullOrEmpty(pending.transaction) &&
                pending.source==source.id && pending.containerId==ContainerState.PrototypeId &&
                pending.item?.definition=="grenade" && pending.item.count==1 && !string.IsNullOrEmpty(pending.item.id) &&
                InventoryJournal.Count(source,"grenade")==4 &&
                world.containers.SelectMany(c=>c.items).Where(i=>i.definition=="grenade").Sum(i=>i.count)==3;
        }
        private void RecoveryPersisted(RecoverySnapshot snapshot)
        {
            if(finished || !Host || !Deposit || Resume || captured) return;
            try
            {
                var current=runtime.Session.ServerWorld;
                if(snapshot==null || current==null || snapshot.WorldId!=current.worldId) return;
                if(recoveryTransaction==null)
                {
                    // This is an independent live witness before reading the completed file;
                    // a late event can otherwise only match an already observed transaction.
                    if(current.pendingDeposits.Count!=1 || !MatchesPending(current,current.pendingDeposits[0])) return;
                    var live=current.pendingDeposits[0];recoveryTransaction=live.transaction;pendingItemId=live.item.id;sawDepositPending=true;
                }
                var saved=snapshot.CreateLiveWorld();
                var pending=saved.pendingDeposits.Find(p=>p.transaction==recoveryTransaction);
                if(pending==null) return; // An earlier queued capture need not contain this deposit.
                if(saved.pendingDeposits.Count!=1 || pending.item?.id!=pendingItemId || !MatchesPending(saved,pending))
                    throw new InvalidOperationException("Завершённый снимок содержит неверные transaction/item/количества резерва.");
                string archive=Path.Combine("Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
                string path=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,archive);
                if(!File.Exists(path) || File.ReadAllText(path)!=snapshot.Json)
                    throw new InvalidOperationException($"Точный архив внесения не совпал: snapshot={snapshot.Id}, path={path}.");
                // Evidence copy of the real durable archive, never a write to the live save.
                File.Copy(path,runtime.Options.Report+".active-world.json",true);
                capturedSnapshot=snapshot.Id;capturedArchive=archive;captured=true;
            }
            catch(Exception error) {Finish(false,"Не подтверждён durable резерв внесения: "+error.Message);}
        }
        private void OnDestroy()
        {if(runtime!=null) runtime.Session.RecoveryPersisted-=RecoveryPersisted;}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && Resume)
            {
                var world=runtime.Session.World;
                success=world!=null && world.pendingDeposits.Count==0 && InventoryJournal.Count(world.Find("player:session-client-1"),"grenade")==4 &&
                    world.containers.SelectMany(c=>c.items).Where(i=>i.definition=="grenade").Sum(i=>i.count)==4;
                if(!success) detail="Клиент не получил окончательный результат восстановленного резерва.";
            }
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,item=Item,staleItemCount=staleCount,sawDepositPending=sawDepositPending,loadedPending=loadedPending,durablePendingCaptured=captured,pendingTransaction=recoveryTransaction,pendingSnapshot=capturedSnapshot,pendingArchive=capturedArchive,
                operationReady=operationReady,operationObserved=operationObserved,observedDebit=observedDebit,operationBeforeCount=operationBeforeCount,canonicalAfterCount=canonicalAfterCount,reconnectItemCount=reconnectItemCount,
                operationActor=operationActor,operationLevel=operationLevel,operationGeneration=operationGeneration,observedTransaction=observedTransaction,waitingFor=waitingFor,phase=phase},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report
        {
            public bool success,sawDepositPending,loadedPending,durablePendingCaptured,operationReady,operationObserved,observedDebit;
            public string detail,item,pendingTransaction,pendingSnapshot,pendingArchive,operationActor,operationLevel,observedTransaction,waitingFor;
            public int staleItemCount,operationBeforeCount,canonicalAfterCount,reconnectItemCount,operationGeneration,phase;
        }
    }
}
