using System;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Editor
{
    public static class InventoryPauseChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var actor=new ActorState {id="paused-inventory",player=true,connected=true,level="hub"};world.actors.Add(actor);
            var container=new ContainerState {id="paused-container",level="hub"};world.containers.Add(container);
            var carried=SharedInventory.Create("heal",5);actor.inventory.Add(carried);
            var stored=SharedInventory.Create("p10",5);container.items.Add(stored);
            var deposits=new SharedDeposits(world);var claims=new SharedInventory(world);
            string before=StateJson.ToJson(world);
            Require(!deposits.Prepare("blocked-deposit",actor,container.id,carried.id,1,out var reason,true) && reason==InventoryTransactions.PausedReason,"Внесение во время паузы принято");
            Require(!claims.PrepareClaim("blocked-claim",container.id,stored.id,actor,out reason,1,true) && reason==InventoryTransactions.PausedReason,"Подбор во время паузы принят");
            Require(StateJson.ToJson(world)==before,"Отказ во время паузы изменил вещи, ревизию или журнал");
            Require(deposits.Prepare("deposit-before-pause",actor,container.id,carried.id,1,out _) &&
                claims.PrepareClaim("claim-before-pause",container.id,stored.id,actor,out _,1),"Активная область не принимает операции");
            var grant=world.pendingGrants[0];
            before=StateJson.ToJson(world);
            Require(!deposits.Prepare("deposit-before-pause",actor,container.id,carried.id,1,out _,true) &&
                !claims.PrepareClaim("claim-before-pause",container.id,stored.id,actor,out _,1,true) && StateJson.ToJson(world)==before,
                "Повтор после остановки области изменил принятые резервы");
            Require(deposits.Commit("deposit-before-pause"),"Принятое внесение не завершается");claims.CommitGrant(grant);
            Require(InventoryJournal.Count(actor,"heal")==4 && InventoryJournal.Count(actor,"p10")==1 &&
                container.items.Exists(item=>item.definition=="heal" && item.count==1) && stored.count==4 &&
                world.pendingDeposits.Count==0 && world.pendingGrants.Count==0,"Завершение принятого резерва потеряло или удвоило вещи");
            Require(deposits.Prepare("after-resume",actor,container.id,carried.id,1,out _),"Возобновление не разрешило новое внесение");
            Require(deposits.Cancel("after-resume") && InventoryJournal.Count(actor,"heal")==4,"Отмена после возобновления не вернула резерв");
            UnityEngine.Debug.Log("Общие вещи и пауза: PASS — новые операции отклонены без изменений, принятые завершаются, возобновление разрешает новые операции.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
