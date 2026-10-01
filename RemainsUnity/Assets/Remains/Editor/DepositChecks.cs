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
        {public string Rejection(DepositContext context)=>"Тест: контейнер не принимает предмет.";}
        public static void Run()
        {
            var world=new WorldState();var source=new ActorState {id="donor",player=true,connected=true};
            world.actors.Add(source);var container=new ContainerState {x=0};world.containers.Add(container);
            var item=SharedInventory.Create("heal",5);source.inventory.Add(item);
            item.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"quality",3}};
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
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("DepositChecks: "+reason);}
    }
}
