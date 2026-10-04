using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class InventoryStackChecks
    {
        public static void Run()
        {
            var actor=Actor("actor");var a=SharedInventory.Create("heal",5);var b=SharedInventory.Create("heal",3);
            actor.inventory.Add(a);actor.inventory.Add(b);
            var owner=actor.Copy();
            Require(InventoryJournal.Debit(owner,"old-id-use",b.id,2),"Расход до объединения");
            Require(InventoryStackMigration.Apply(actor)==1 && actor.inventory.Single().count==8 && InventoryStacks.Find(actor,b.id).id==a.id,
                "Старые стопки/ID не объединены");
            InventoryStacks.Add(actor,SharedInventory.Create("heal",4));
            Require(InventoryJournal.Merge(actor,owner,out var rebased,out var uses) && rebased.Single().count==10,
                "Одновременная выдача и поздний расход старого ID не согласованы");
            actor.inventory=rebased;actor.inventoryUses=uses;
            Require(InventoryJournal.Merge(actor,owner,out rebased,out _) && rebased.Single().count==10,
                "Повторный пакет повторил расход");
            actor.inventoryReservations[a.id]=3;
            Require(InventoryAvailability.Count(actor,actor.inventory.Single())==7,"Резерв не привязан к единой стопке");
            actor.inventoryReservations.Clear();

            var one=new ItemStack {id="pistol-one",definition="pistol",count=1,weaponState=new WeaponItemState {rounds=7}};
            var two=one.Copy();two.id="pistol-two";
            InventoryStacks.Add(actor,one);InventoryStacks.Add(actor,two);
            Require(actor.inventory.Count(item=>item.weaponState!=null)==2,"Экземпляры оружия склеены");
            var modified=SharedInventory.Create("heal",2);modified.extensions=new Dictionary<string,JToken>{{"quality",4}};
            InventoryStacks.Add(actor,modified);var same=modified.Copy();same.id="same-quality";InventoryStacks.Add(actor,same);
            Require(actor.inventory.Count(item=>item.definition=="heal")==2 && InventoryStacks.Find(actor,"same-quality").count==4,
                "Данные мода потеряны либо равные варианты не объединены");

            var world=new WorldState();var donor=Actor("donor");var recipient=Actor("recipient");
            world.actors.Add(donor);world.actors.Add(recipient);
            InventoryStacks.Add(donor,SharedInventory.Create("heal",6));InventoryStacks.Add(recipient,SharedInventory.Create("heal",2));
            var box=new ContainerState {id="box"};world.containers.Add(box);
            var original=SharedInventory.Create("heal",3);var absorbed=SharedInventory.Create("heal",2);
            InventoryStacks.Add(box,original);InventoryStacks.Add(box,absorbed);
            var claims=new SharedInventory(world);
            Require(claims.PrepareClaim("first",box.id,absorbed.id,recipient,out _,1) &&
                claims.PrepareClaim("second",box.id,original.id,donor,out _,1),"Одновременные запросы количества по двум ID одной стопки");
            var pending=world.pendingGrants.First(p=>p.transaction=="first");claims.CommitGrant(pending);claims.CommitGrant(pending);
            claims.CommitGrant(world.pendingGrants.Single());
            Require(recipient.inventory.Single().count==3 && donor.inventory.Single().count==7 && box.items.Single().count==3,
                "Подбор создал отдельную стопку, потерял количество или применился дважды");
            var deposits=new SharedDeposits(world);string donorItem=donor.inventory.Single().id;
            Require(deposits.Prepare("deposit",donor,box.id,donorItem,2,out _) && deposits.Commit("deposit") &&
                !deposits.Commit("deposit") && box.items.Single().count==5,"Внесение не объединилось ровно один раз");
            Require(deposits.Prepare("cancel",donor,box.id,donorItem,2,out _) && deposits.Cancel("cancel") &&
                donor.inventory.Single().count==5,"Отмена создала отдельную стопку");
            Require(claims.PrepareClaim("whole",box.id,absorbed.id,recipient) &&
                !claims.PrepareClaim("late-whole",box.id,original.id,donor),"После полного резерва вторая ссылка выдала копию");
            var revived=StateJson.Copy(world);var recovered=new SharedInventory(revived);
            recovered.CommitGrant(revived.pendingGrants.Single());
            Require(revived.Find(recipient.id).inventory.Single().count==8 && revived.containers.Single().items.Count==0,
                "Перезапуск незавершённого подбора нарушил единую стопку");

            var grenadeActor=Actor("grenadier");var g1=SharedInventory.Create("grenade",2);var g2=SharedInventory.Create("grenade",3);
            grenadeActor.inventory.Add(g1);grenadeActor.inventory.Add(g2);grenadeActor.equippedWeaponItem=g2.id;
            grenadeActor.weaponShortcuts.Add(new WeaponShortcut {slot="4",item=g2.id});InventoryStackMigration.Apply(grenadeActor);
            Require(WeaponInventory.Selected(grenadeActor)?.count==5 && WeaponInventory.Shortcut(grenadeActor,"4")?.id==g1.id &&
                SharedInventory.Consume(grenadeActor,"grenade",g2.id) && grenadeActor.inventory.Single().count==4,
                "Выбранная граната/назначение потерялись после объединения");

            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/inventory-stack-"+Guid.NewGuid().ToString("N")));
            var saves=new SaveStore(root);var legacy=Actor("legacy");
            legacy.inventory.Add(SharedInventory.Create("heal",2));legacy.inventory.Add(SharedInventory.Create("heal",5));
            saves.SaveCharacter(legacy);string source=saves.ReadCharacterSource();var loaded=saves.LoadCharacter();
            Require(loaded.inventory.Single().count==7 && Directory.EnumerateFiles(Path.Combine(root,"Recovery"),"*.json").Any(path=>File.ReadAllText(path)==source),
                "Преобразование персонажа не сохранило точный исходник");
            var savedWorld=new WorldState();savedWorld.actors.Add(legacy);var legacyBox=new ContainerState {id="legacy-box"};
            legacyBox.items.Add(SharedInventory.Create("caps",2));legacyBox.items.Add(SharedInventory.Create("caps",4));savedWorld.containers.Add(legacyBox);
            saves.SaveWorld(savedWorld);string originalWorld=File.ReadAllText(Path.Combine(root,"world.json"));
            var loadedWorld=saves.LoadWorld();
            Require(loadedWorld.actors.Single().inventory.Single().count==7 && loadedWorld.containers.Single().items.Single().count==6 &&
                Directory.EnumerateFiles(Path.Combine(root,"Recovery"),"*.json").Any(path=>File.ReadAllText(path)==originalWorld),
                "Преобразование мира/контейнера не сохранило исходник");
            saves.SaveWorld(loadedWorld);var reloaded=saves.LoadWorld();
            Require(reloaded.actors.Single().inventory.Single().count==7 && reloaded.containers.Single().items.Single().count==6,
                "Повторная загрузка повторила объединение");
            var corrupt=loaded.Copy();corrupt.inventoryAliases["cycle-one"]="cycle-two";corrupt.inventoryAliases["cycle-two"]="cycle-one";
            bool rejected=false;try {InventoryStacks.Validate(corrupt.inventory,corrupt.inventoryAliases);} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Циклические ссылки не отклонены");
            var full=Actor("full");InventoryStacks.Add(full,SharedInventory.Create("caps",int.MaxValue));string before=StateJson.ToJson(full);
            rejected=false;try {InventoryStacks.Add(full,SharedInventory.Create("caps",1));} catch(OverflowException) {rejected=true;}
            Require(rejected && StateJson.ToJson(full)==before,"Переполнение изменило количество или создало скрытый лимит стопки");
            Debug.Log("InventoryStackChecks PASS: единые стопки, старые ID, поздний расход, резерв/повтор, отмена, моды/оружие, граната, backup и перезапуск.");
        }
        private static ActorState Actor(string id)=>new() {id=id,character=id,connected=true,loadoutVersion=1,weaponInventoryVersion=1};
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("InventoryStackChecks: "+reason);}
    }
}
