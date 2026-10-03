using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Serialization;
using Newtonsoft.Json.Linq;
namespace Remains.Editor
{
    public static class WeaponItemStateChecks
    {
        private static ItemStack Pistol(int rounds)=>new() {id=Guid.NewGuid().ToString("N"),definition="pistol",count=1,
            weaponState=new WeaponItemState {rounds=rounds,revision=1,extensions=new Dictionary<string,JToken> {{"test:mark","kept"}}}};
        public static void Run()
        {
            var world=new WorldState();
            var donor=new ActorState {id="weapon-donor",player=true,connected=true};
            var recipient=new ActorState {id="weapon-recipient",player=true,connected=true};
            world.actors.Add(donor);world.actors.Add(recipient);
            var container=new ContainerState();world.containers.Add(container);
            var first=Pistol(7);var second=Pistol(2);donor.inventory.Add(first);donor.inventory.Add(second);
            var owner=donor.Copy();owner.inventory[0].weaponState.rounds=6;owner.inventory[0].weaponState.revision++;
            Require(InventoryJournal.Merge(donor,owner,out var merged,out _),"Не принят расход магазина владельцем");
            Require(merged[0].weaponState.rounds==6 && merged[1].weaponState.rounds==2 && first.weaponState.rounds==7,
                "Изменён другой экземпляр либо исходное состояние");
            donor.inventory=merged;
            Require(InventoryJournal.Merge(donor,owner,out merged,out _) && merged[0].weaponState.rounds==6,"Повтор изменил заряд");
            var conflict=owner.Copy();conflict.inventory[0].weaponState.rounds=5;
            Require(!InventoryJournal.Merge(donor,conflict,out _,out _),"Один номер состояния разрешил разные заряды");
            var stale=donor.Copy();stale.inventory[0].weaponState.revision=1;stale.inventory[0].weaponState.rounds=7;
            Require(InventoryJournal.Merge(donor,stale,out merged,out _) && merged[0].weaponState.rounds==6,"Старый снимок вернул патрон");
            var deposits=new SharedDeposits(world);
            Require(deposits.Prepare("weapon-deposit",donor,container.id,first.id,1,out _),"Не зарезервировано оружие");
            Require(!deposits.Prepare("weapon-deposit",donor,container.id,first.id,1,out _),"Повтор создал резерв");
            Require(InventoryJournal.Merge(donor,owner,out merged,out _) && merged.Count==1 && merged[0].id==second.id,
                "Старый владелец восстановил переданное оружие");
            world=StateJson.Copy(world);donor=world.Find(donor.id);recipient=world.Find(recipient.id);container=world.containers[0];
            deposits=new SharedDeposits(world);
            Require(deposits.Commit("weapon-deposit") && !deposits.Commit("weapon-deposit"),"Повторное подтверждение передачи");
            var claims=new SharedInventory(world);
            Require(claims.PrepareClaim("weapon-claim",container.id,first.id,recipient),"Не подготовлен подбор оружия");
            var grant=world.pendingGrants[0];claims.CommitGrant(grant);claims.CommitGrant(grant);
            Require(recipient.inventory.Count==1 && recipient.inventory[0].id==first.id && recipient.inventory[0].weaponState.rounds==6 &&
                (string)recipient.inventory[0].weaponState.extensions["test:mark"]=="kept","Потерян экземпляр, заряд или данные мода");
            Require(new SharedDeposits(world).Prepare("weapon-cancel",recipient,container.id,first.id,1,out _),"Нет резерва для отмены");
            Require(new SharedDeposits(world).Cancel("weapon-cancel") && recipient.inventory.Count==1 && recipient.inventory[0].weaponState.rounds==6,
                "Отмена не вернула оружие с зарядом");
            var malformed=Pistol(3);malformed.count=2;
            bool rejected=false;try {malformed.ReservedCopy(1);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Оружие разделено как стопка");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException(message);}
    }
}
