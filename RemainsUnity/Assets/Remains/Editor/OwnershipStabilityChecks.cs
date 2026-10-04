using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class OwnershipStabilityChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var a=new ActorState {id="a",owner="a",player=true,connected=true,x=-2,level="hub"};
            var b=new ActorState {id="b",owner="b",player=true,connected=true,x=2,level="hub"};
            var npc=new ActorState {id="npc",owner="a",x=0,level="hub"};
            world.actors.AddRange(new[]{a,b,npc});
            var service=new OwnershipService();var owners=new[]{"a","b"};
            for(int i=0;i<100;i++)
            {
                b.x=2+(i%2==0?-.1f:.1f);service.Reassign(world,owners);
                Require(npc.owner=="a" && npc.generation==0,"Небольшое колебание расстояния меняет владельца.");
            }
            b.x=1;service.Reassign(world,owners);
            Require(npc.owner=="b" && npc.generation==1,"Заметно более близкий клиент не получает сущность.");
            npc.target=a.id;npc.attackBusy=true;service.Reassign(world,owners);
            Require(npc.owner=="b","Начатая атака прервана передачей.");
            npc.attackBusy=false;service.Reassign(world,owners);
            Require(npc.owner=="a","Цель NPC не получила владение после атаки.");
            npc.target=null;a.connected=false;service.Reassign(world,owners);
            Require(npc.owner=="b","Отключённый клиент удержал владение.");
            a.connected=true;a.owner="a";b.level="factory:0";service.Reassign(world,owners);
            Require(npc.owner=="a","Клиент в другом уровне удержал владение.");
            a.down=true;service.Reassign(world,owners);
            Require(npc.owner=="host","Нет корректного освобождения при отсутствии активных клиентов уровня.");
            a.down=false;b.level="hub";b.x=1.9f;npc.owner="a";
            new OwnershipService(new StableProximityOwnershipRule(0)).Reassign(world,owners);
            Require(npc.owner=="b","Настройка порога правила не применяется.");
            Debug.Log("Стабильность владения: PASS — колебания расстояния, приближение, цель, атака, отключение и смена уровня.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
