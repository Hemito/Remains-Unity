using System;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;

namespace Remains.Editor
{
    public static class DepositChecks
    {
        private sealed class Deny : IDepositRule
        {public string AccessRejection(LootAccessContext context)=>new NearbyLootRule().AccessRejection(context);
         public string Rejection(DepositContext context)=>"Тест: контейнер не принимает предмет.";}
        public static void Run()
        {
            CheckReconcileNotifications();
            var world=new WorldState();var source=new ActorState {id="donor",player=true,connected=true};
            world.actors.Add(source);var container=new ContainerState {x=0};world.containers.Add(container);
            var item=SharedInventory.Create("heal",5);source.inventory.Add(item);
            item.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"quality",3}};
            var locked=source.Copy();locked.inventoryReservations[item.id]=4;
            Require(InventoryJournal.Count(locked,"heal")==1 && InventoryJournal.Consume(locked,"heal",1) &&
                !InventoryJournal.Consume(locked,"heal",1) && !SharedInventory.Consume(locked,"heal") &&
                !InventoryJournal.Debit(locked,"blocked",item.id,1),"Зарезервированное количество не тратится ни одним путём расхода");
            Require(locked.Copy().inventoryReservations.Count==0 && !StateJson.ToJson(locked).Contains("inventoryReservations"),"Временная клиентская блокировка не попадает в сейв/сеть");
            locked.inventoryReservations.Clear();Require(InventoryJournal.Count(locked,"heal")==4,"Снятие блокировки освобождает только остаток");
            var deposits=new SharedDeposits(world);
            string before=StateJson.ToJson(world);
            Require(!deposits.Prepare("negative",source,container.id,item.id,-1,out _) &&
                !deposits.Prepare("large",source,container.id,item.id,6,out _) &&
                !new SharedDeposits(world,new Deny()).Prepare("denied",source,container.id,item.id,1,out _) &&
                StateJson.ToJson(world)==before,"Отказ не меняет мир");
            source.level="hub";Require(!deposits.Prepare("wrong-level",source,container.id,item.id,1,out _),"Чужой уровень отклонён");source.level=container.level;
            var staleOwner=source.Copy();
            Require(deposits.Prepare("deposit",source,container.id,item.id,2,out _) && source.inventory.Single().count==3 &&
                container.items.Count==0 && world.pendingDeposits.Single().item.count==2,"Резерв удалён у отправителя, но ещё недоступен в контейнере");
            Require(!deposits.Prepare("deposit",source,container.id,item.id,2,out _) &&
                !new SharedInventory(world).PrepareClaim("deposit",container.id,"absent",source),"Один ID не используется повторно или другой операцией");
            Require(InventoryJournal.Merge(source,staleOwner,out var rebased,out _) && rebased.Single().count==3,"Старый пакет владельца не возвращает внесённые предметы");
            Require(InventoryJournal.Consume(staleOwner,"heal",1) && InventoryJournal.Merge(source,staleOwner,out rebased,out _) && rebased.Single().count==2,"Независимый допустимый расход согласуется с резервом");
            string directory=Path.Combine(Path.GetTempPath(),"remains-deposits-"+Guid.NewGuid().ToString("N"));
            try
            {
                var saves=new SaveStore(directory);saves.SaveWorld(world);var restored=saves.LoadWorld(false);
                var recovery=new SharedDeposits(restored);
                Require(recovery.Commit("deposit") && !recovery.Commit("deposit") && !recovery.Cancel("deposit"),"После загрузки commit выполняется ровно один раз");
                var delivered=restored.containers.Single().items.Single();
                Require(delivered.count==2 && delivered.id!=item.id && (int)delivered.extensions["quality"]==3 &&
                    InventoryJournal.Count(restored.Find(source.id),"heal")==3,"Сохранены сумма, отдельный ID и данные мода");
                Require(deposits.Cancel("deposit") && !deposits.Cancel("deposit") && InventoryJournal.Count(source,"heal")==5 && container.items.Count==0,"Явная отмена возвращает резерв один раз");
                Require(InventoryJournal.Merge(source,source.Copy(),out rebased,out _) && rebased.Sum(i=>i.count)==5,"Журнал отменённого резерва не списывается снова");
                Require(deposits.Prepare("whole",source,container.id,item.id,0,out _),"Полный остаток резервируется");
                world.containers.Clear();bool failed=false;
                try {deposits.Commit("whole");} catch(InvalidOperationException) {failed=true;}
                Require(failed && world.pendingDeposits.Count==1 && !world.completedTransactions.Contains("whole"),"Исчезновение контейнера сохраняет незавершённый резерв и выдаёт ошибку");
                Require(deposits.Cancel("whole") && InventoryJournal.Count(source,"heal")==5,"Явное восстановление после исчезновения контейнера");
            }
            finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
            UnityEngine.Debug.Log("DepositChecks PASS: резерв, отказ, журнал, перезапуск, commit/отмена и неизвестные поля.");
        }
        private static void CheckReconcileNotifications()
        {
            var root=new UnityEngine.GameObject("Проверка уведомлений внесения");
            try
            {
                var session=root.AddComponent<Remains.Networking.CoopSession>();
                var world=new WorldState();var actor=new ActorState {id="deposit-notifications",player=true,connected=true};world.actors.Add(actor);
                typeof(Remains.Networking.CoopSession).GetProperty("World").SetValue(session,world);
                typeof(Remains.Networking.CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                var deposits=session.Deposits;int notifications=0;deposits.Reconciled+=()=>notifications++;
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var changed=typeof(Remains.Networking.CoopSession).GetMethod("RaiseChanged",flags);
                void Publish()=>changed.Invoke(session,new object[] {WorldChangeKind.Actors,null});
                Publish();Require(notifications==0,"Пустое Changed дублирует уведомление внесения");
                var requests=(System.Collections.Generic.Dictionary<string,Remains.Networking.DepositRequest>)typeof(Remains.Networking.SessionDeposits).GetField("requests",flags).GetValue(deposits);
                requests.Add("proof",new Remains.Networking.DepositRequest {transaction="proof",item="stack",quantity=2});
                Publish();Require(notifications==1 && actor.inventoryReservations["stack"]==2,"Pending не создал reservation/уведомление");
                world.completedTransactions.Add("proof");Publish();
                Require(notifications==2 && !deposits.Pending && actor.inventoryReservations.Count==0,"Журнальный proof не снял reservation/уведомление");
                requests.Add("rejected",new Remains.Networking.DepositRequest {transaction="rejected",item="stack",quantity=2});
                typeof(Remains.Networking.SessionDeposits).GetMethod("ReceiveReply",flags).Invoke(deposits,new object[] {new Remains.Networking.DepositReply {transaction="rejected",accepted=false,reason="Тестовый отказ"}});
                Require(notifications==3 && !deposits.Pending,"Последний отказ с пустыми reservations потерял уведомление");
                actor.inventoryReservations["stale"]=1;Publish();
                Require(notifications==4 && actor.inventoryReservations.Count==0,"Устаревшая reservation не очищена/не опубликована");
                Publish();Require(notifications==4,"Повторный пустой Changed вызвал уведомление");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("DepositChecks: "+reason);}
    }
}
