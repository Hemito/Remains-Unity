using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ContractChecks
    {
        public static void Run()
        {
            Anatomy.Load();
            var actor=new ActorState { id="victim",owner="client-b",player=true,connected=true };
            var damage=new DamageReceiver(new PrototypeDamageRule());
            var attack=new AttackEvent { id="shot-1",weapon="pistol",attacker="client-a",originX=-2,originY=0.7f,endX=2,endY=0.7f };
            Require(HitGeometry.Contacts(actor,attack),"Сегмент должен пересекать корпус");
            Require(!damage.Apply(actor,attack,false) && actor.health==100,"Пострадавший может отклонить попадание");
            Require(damage.Apply(actor,attack,true) && actor.health==65,"Одно принятое попадание");
            Require(!damage.Apply(actor,attack,true) && actor.health==65,"Повтор события не наносит урон");
            actor.down=true;
            attack.id="shot-2";
            damage.Apply(actor,attack,true);
            Require(actor.down,"Урон не поднимает отключённое тело");

            var world=new WorldState();world.actors.Add(actor);
            var second=new ActorState {id="second",owner="client-c",player=true,connected=true,x=5};world.actors.Add(second);
            var npc=new ActorState {id="npc",owner="client-b",target=actor.id,x=4,attackBusy=true};world.actors.Add(npc);
            var ownership=new OwnershipService();
            ownership.Reassign(world,new[]{"client-b","client-c"});
            Require(npc.owner=="client-b","Начатая атака сохраняет владельца");
            npc.attackBusy=false;ownership.Reassign(world,new[]{"client-b","client-c"});
            Require(npc.owner=="client-c" && npc.generation==1,"После атаки ближайший активный клиент получает NPC");

            var item=SharedInventory.Create("caps",20);world.container.Add(item);
            var inventory=new SharedInventory(world);
            Require(inventory.PrepareClaim("claim-1",item.id,actor),"Первый запрос резервирует предмет");
            Require(actor.inventory.Count==0 && world.pendingGrants.Count==1,"Резерв не выдаёт предмет до записи копий");
            Require(!inventory.PrepareClaim("claim-2",item.id,second),"Другой клиент не получает резерв");
            inventory.CommitGrant(world.pendingGrants[0]);
            Require(actor.inventory.Count==1 && second.inventory.Count==0,"Общий предмет получает один игрок");

            var directory=Path.Combine(Path.GetTempPath(),"Remains-contracts-"+Guid.NewGuid().ToString("N"));
            var saves=new SaveStore(directory);saves.SaveWorld(world);saves.SaveWorld(world);
            var restored=saves.LoadWorld();
            Require(restored.worldId==world.worldId && restored.Find(actor.id).down && restored.Find(actor.id).health==30,
                "Сохранение восстанавливает идентичность мира и состояние тела");
            Require(restored.completedTransactions.Contains("claim-1") && File.Exists(Path.Combine(directory,"world.json.previous")),
                "Сохранение сохраняет транзакцию и предыдущую версию");
            Debug.Log("Проверки контрактов: PASS — урон пострадавшего, дедупликация, тело, передача владельца, резерв/выдача, сохранение.");
        }
        private static void Require(bool condition,string detail)
        {
            if(!condition) throw new InvalidOperationException("Проверка контракта не прошла: "+detail);
        }
    }
}
