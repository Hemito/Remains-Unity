using System;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class ContainerChecks
    {
        public static void Run()
        {
            var world=new WorldState();var container=new ContainerState {x=0};world.containers.Add(container);
            var actor=new ActorState {id="player",player=true,connected=true,level="hub"};world.actors.Add(actor);
            var item=SharedInventory.Create("caps",20);container.items.Add(item);var inventory=new SharedInventory(world);
            Require(!inventory.PrepareClaim("cross",container.id,item.id,actor),"Нет подбора из другой локации");
            actor.level=container.level;actor.x=3;
            Require(!inventory.PrepareClaim("far",container.id,item.id,actor),"Нет подбора издалека");
            actor.x=0;actor.down=true;
            Require(!inventory.PrepareClaim("down",container.id,item.id,actor),"Нет подбора без сознания");
            actor.down=false;
            Require(inventory.PrepareClaim("one",container.id,item.id,actor) && actor.inventory.Count==0,"Резерв не выдаёт предмет до подтверждения");
            var other=new ActorState {id="other",connected=true,player=true};world.actors.Add(other);
            Require(!inventory.PrepareClaim("two",container.id,item.id,other),"Второй клиент не получает тот же резерв");
            var pending=world.pendingGrants[0];world.actors.Remove(actor);inventory.CommitGrant(pending);inventory.CommitGrant(pending);
            Require(container.items.Count==1 && container.items[0].id==item.id && other.inventory.Count==0,"Исчезнувший получатель возвращает один предмет в источник");
            Require(inventory.PrepareClaim("three",container.id,item.id,other),"Возвращённый предмет доступен другому игроку");
            inventory.CommitGrant(world.pendingGrants[0]);Require(other.inventory.Count==1 && container.items.Count==0,"Выдача после возврата не дублирует предмет");
            var hub=new ActorState {id="hub-player",player=true,connected=true,owner="gone",level="hub"};world.actors.Add(hub);
            var hubContainer=new ContainerState {id="hub-container",level="hub",x=0};world.containers.Add(hubContainer);
            var reserved=SharedInventory.Create("heal",3);hubContainer.items.Add(reserved);
            Require(inventory.PrepareClaim("hub-grant",hubContainer.id,reserved.id,hub),"Резерв персонажа хаба");
            ActorSessionRules.Disconnect(hub);new OwnershipService().Reassign(world,Array.Empty<string>());
            Require(!ActorPresence.IsPresent(hub) && !hub.down && hub.owner==null,"Отключённый хаб сохраняет запись без тела и чужого владельца");
            inventory.CommitGrant(world.pendingGrants[0]);
            var recovered=hub.Copy();recovered.connected=true;
            Require(ActorPresence.IsPresent(recovered) && recovered.inventory.Count==1 && recovered.inventory[0].id==reserved.id,"Завершённая выдача сохраняется в отсутствующей записи и возвращается с персонажем");
            ActorSessionRules.Disconnect(other);Require(other.down && ActorPresence.IsPresent(other),"Отключение вне хаба сохраняет тело без сознания");
            string path=Path.Combine(Path.GetTempPath(),"Remains-containers-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
            try
            {
                var legacy=new WorldState {container=new(){SharedInventory.Create("heal",3)}};
                File.WriteAllText(Path.Combine(path,"world.json"),JsonUtility.ToJson(legacy));
                var saves=new SaveStore(path);var migrated=saves.LoadWorld();
                Require(migrated.containers.Count==1 && migrated.containers[0].items.Count==1,"Старый глобальный список перенесён без потери");
                saves.SaveWorld(migrated);var again=saves.LoadWorld();
                Require(again.containers.Count==1 && again.containers[0].items.Count==1,"Повторная загрузка не повторяет миграцию");
            }
            finally {Directory.Delete(path,true);}
            Debug.Log("ContainerChecks PASS: доступ, резерв, конкуренция, исчезновение получателя и миграция.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("ContainerChecks: "+reason);}
    }
}
