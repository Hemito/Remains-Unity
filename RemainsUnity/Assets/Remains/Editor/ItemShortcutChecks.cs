using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ItemShortcutChecks
    {
        public static void Run()
        {
            Remains.Content.WeaponCatalog.Load();
            var world=new WorldState();var sender=new ActorState {id="sender",character="sender",player=true,connected=true,loadoutVersion=1,weaponInventoryVersion=1,x=0};
            var recipient=new ActorState {id="recipient",character="recipient",player=true,connected=true,loadoutVersion=1,weaponInventoryVersion=1,x=.5f};
            world.actors.Add(sender);world.actors.Add(recipient);
            var ammunition=InventoryStacks.Add(sender,SharedInventory.Create("p10",20));
            var controls=new HashSet<UnityEngine.InputSystem.Key>();
            foreach(var key in ItemShortcuts.Keys)
            {
                Require(controls.Add(ItemShortcutInput.KeyFor(key)),"Повтор физической клавиши: "+key);
                Require(ItemShortcuts.Assign(sender,key,ammunition.id,out _) && ItemShortcuts.Get(sender,key)==ammunition,"Нельзя назначить боеприпасы на ["+key+"]");
            }
            Require(ItemShortcuts.Keys.Count==12 && ItemShortcuts.Keys[9]=="0" && ItemShortcuts.Keys[11]=="=","Не соответствует ряд клавиш 1234567890-=");
            var saved=sender.Copy();Require(ItemShortcuts.Get(saved,"=")?.id==ammunition.id,"Сохранение потеряло назначение обычного предмета");
            var binding=sender.weaponShortcuts.Find(value=>value.slot=="=");
            binding.extensions=new Dictionary<string,JToken> {["mod:shortcut"]=new JObject {["mode"]="hold"}};
            Require(ItemShortcuts.Assign(sender,"=",ammunition.id,out _) && sender.weaponShortcuts.Find(value=>value.slot=="=")==binding &&
                (string)sender.Copy().weaponShortcuts.Find(value=>value.slot=="=").extensions["mod:shortcut"]["mode"]=="hold",
                "Переназначение стёрло дополнительные данные быстрой клавиши");
            Require(!ItemShortcuts.Assign(sender,"13",ammunition.id,out _) && !ItemShortcuts.Assign(sender,"5","absent",out _),"Принято неизвестное назначение");
            Require(!WeaponInventory.Select(sender,ammunition.id,out _),"Патроны превратились в стреляющее оружие");
            var actions=new StandardItemActions();Require(actions.Action(ammunition)==StandardItemActions.Toss &&
                actions.Action(SharedInventory.Create("heal",1))==StandardItemActions.Heal &&
                actions.Action(SharedInventory.Create("grenade",1))==StandardItemActions.Equip,"Не различаются действия предметов");
            var origin=new Vector2(.3f,.7f);var velocity=new Vector2(4,5);
            Require(DroppedItemTransfer.Prepare(world,sender,"toss",ammunition.id,5,origin,velocity,new NearbyDepositRule(),out _),"Бросок не подготовлен");
            var drop=world.containers.Find(value=>value.id=="drop:toss");
            Require(ammunition.count==15 && drop.items.Count==0 && world.pendingDeposits[0].item.count==5 &&
                drop.presentationItem=="p10" && drop.vx==4 && drop.vy==5 && ItemShortcuts.Get(sender,"0")==ammunition,
                "Бросок потерял количество, запуск, настоящее изображение или ссылку оставшейся стопки");
            string pending=StateJson.ToJson(world);
            Require(!DroppedItemTransfer.PruneEmpty(world) && !DroppedItemTransfer.Prepare(world,sender,"toss",ammunition.id,5,origin,velocity,new NearbyDepositRule(),out _) &&
                StateJson.ToJson(world)==pending,"Резерв удалён или повтор броска изменил состояние");
            string savePath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/item-shortcuts-persistence-"+Guid.NewGuid().ToString("N")));
            var store=new SaveStore(savePath);store.SaveWorld(world);store.SaveCharacter(sender);
            var recovered=store.LoadWorld();var loadedCharacter=store.LoadCharacter();
            Require(loadedCharacter.inventory[0].count==15 && ItemShortcuts.Get(loadedCharacter,"=")?.id==ammunition.id &&
                recovered.pendingDeposits.Count==1 && recovered.containers.Find(value=>value.id==drop.id).presentationItem=="p10",
                "Запись и загрузка потеряли резерв броска, его изображение, количество или назначение");
            Require(new SharedDeposits(recovered).Commit("toss") && !new SharedDeposits(recovered).Commit("toss"),"Восстановленный бросок потерян/применён дважды");
            var stored=recovered.containers.Find(value=>value.id==drop.id);
            var target=recovered.Find(recipient.id);InventoryStacks.Add(target,SharedInventory.Create("p10",3));
            var claims=new SharedInventory(recovered);
            Require(claims.PrepareClaim("take",stored.id,stored.items[0].id,target) && !DroppedItemTransfer.PruneEmpty(recovered),"Бросок нельзя подобрать или источник удалён до подтверждения");
            claims.CommitGrant(recovered.pendingGrants[0]);
            Require(DroppedItemTransfer.PruneEmpty(recovered) && recovered.containers.Count==0 && target.inventory.Count==1 && target.inventory[0].count==8 &&
                InventoryJournal.Count(recovered.Find(sender.id),"p10")==15,"Подбор не сохранил сумму/стопку или оставил пустой предмет");
            string before=StateJson.ToJson(recovered);var actor=recovered.Find(sender.id);
            Require(!DroppedItemTransfer.Prepare(recovered,actor,"paused",actor.inventory[0].id,1,origin,velocity,new NearbyDepositRule(),out _,true) &&
                !DroppedItemTransfer.Prepare(recovered,actor,"far",actor.inventory[0].id,1,Vector2.one*100,velocity,new NearbyDepositRule(),out _) &&
                StateJson.ToJson(recovered)==before,"Отказ броска потратил вещь или создал пустой объект");
            Debug.Log("Клавиши предметов: PASS — 12 назначений, сохранение, действия, единый резерв броска/подбора, восстановление и отсутствие повторов.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
