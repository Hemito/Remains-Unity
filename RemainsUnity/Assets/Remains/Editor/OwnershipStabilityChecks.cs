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
            CheckRestingBodies();
            Debug.Log("Стабильность владения: PASS — колебания расстояния, приближение, цель, атака, отключение и смена уровня.");
        }
        // Two players walking across a level full of resting bodies must not hand them back and forth.
        private static void CheckRestingBodies()
        {
            var world=new WorldState();
            var a=new ActorState {id="rest-a",owner="a",player=true,connected=true,x=0,level="factory:0"};
            var b=new ActorState {id="rest-b",owner="b",player=true,connected=true,x=100,level="factory:0"};
            world.actors.AddRange(new[]{a,b});
            for(int i=0;i<300;i++) world.containers.Add(new ContainerState {id="rest-box-"+i,owner=i%2==0?"a":"b",level="factory:0",x=i*.33f,generation=1});
            var moving=new PropState {id="rest-moving",owner="a",level="factory:0",x=60,vx=2,generation=1};
            world.props.Add(moving);
            var service=new OwnershipService();var owners=new[]{"a","b"};
            for(int step=0;step<=100;step++)
            {
                a.x=step;b.x=100-step;
                service.Reassign(world,owners);
            }
            Require(world.containers.TrueForAll(box=>box.generation==1),"Покоящиеся тела меняли владельца, пока игроки ходили.");
            Require(moving.owner=="a" && moving.generation>=3,"Движущееся тело не переходило ближайшему игроку.");
            var lying=world.containers[11];lying.held=true;
            service.Reassign(world,owners);
            Require(lying.owner=="b" && lying.held,"Удерживаемое тело потеряло держателя.");
            lying.held=false;
            b.level="hub";
            service.Reassign(world,owners);
            Require(world.containers.TrueForAll(box=>box.owner=="a"),"Тела ушедшего с уровня игрока не перешли оставшемуся.");
            int transferred=world.containers.FindAll(box=>box.generation==2).Count;
            Require(transferred==150,"Уход игрока передал не только его тела: "+transferred+".");
            service.Reassign(world,owners);
            Require(world.containers.FindAll(box=>box.generation==2).Count==150,"Повторное распределение снова передало тела.");
            b.level="factory:0";b.down=true;a.down=false;
            service.Reassign(world,owners);
            Require(world.containers.TrueForAll(box=>box.owner=="a"),"Вернувшийся игрок забрал покоящиеся тела.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
