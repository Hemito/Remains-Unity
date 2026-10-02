using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcAttackChecks
    {
        public static void Run()
        {
            CheckWindupMovement();
            var world=new WorldState();
            var a=new ActorState {id="a",player=true,connected=true,owner="a",x=2};
            var b=new ActorState {id="b",player=true,connected=true,owner="b",x=8};
            var npc=new ActorState {id="npc",owner="a",target=a.id};
            world.actors.Add(a);world.actors.Add(b);world.actors.Add(npc);
            var ownership=new OwnershipService();NpcAttackRules.Begin(npc,a);string id=npc.attackId;
            b.x=1;ownership.Reassign(world,new[]{"a","b"});
            Require(npc.owner=="a" && npc.target==a.id && npc.attackTarget==a.id,"Начатая атака сохраняет цель и владельца");
            var attack=NpcAttackRules.Complete(npc,a);ownership.Reassign(world,new[]{"a","b"});
            Require(attack.id==id && npc.owner=="b" && npc.target==b.id && npc.attackCooldown==2,"Завершение передаёт владельца с сохранённой паузой");
            var restored=npc.Copy();Require(restored.attackCooldown==2 && restored.attackId==id,"Пауза и ID переживают сериализацию");
            NpcAttackRules.Begin(npc,b);b.connected=false;ownership.Reassign(world,new[]{"a"});
            Require(npc.owner=="a" && !npc.attackBusy && npc.attackTarget==null && npc.attackId==null && npc.attackCooldown==2,"Обрыв отменяет старую атаку и оставляет паузу новому владельцу");
            var old=npc.Copy();NpcAttackRules.Begin(npc,a);npc.attackRemaining=.2f;
            NpcAttackRules.CopyProgress(old,npc);Require(npc.attackRemaining==.2f,"Поздний прогресс предыдущей атаки не переписывает новую");
            Debug.Log("NpcAttackChecks PASS: цель, передача после завершения, пауза, сериализация и отмена при обрыве.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("NpcAttackChecks: "+reason);}
        private static void CheckWindupMovement()
        {
            var root=new GameObject("Проверка движения при подготовке выстрела");
            try
            {
                var session=root.AddComponent<Remains.Networking.CoopSession>();
                var world=new WorldState();
                var player=new ActorState {id="target",player=true,connected=true,owner="owner",x=4};
                var npc=new ActorState {id="guard",actorType="raider",anatomy="raider",weapon="pistol",owner="owner",x=0,vx=2.1f,pose="walk",
                    attackBusy=true,attackRemaining=10,attackTarget=player.id};
                world.actors.Add(player);world.actors.Add(npc);
                typeof(Remains.Networking.CoopSession).GetProperty("World").SetValue(session,world);
                typeof(Remains.Networking.CoopSession).GetProperty("LocalOwner").SetValue(session,"owner");
                var geometry=new Remains.Gameplay.SimulationGeometry(session);
                var controller=new Remains.Gameplay.NpcController(session,geometry);
                var cache=(System.Collections.Generic.Dictionary<string,Remains.Gameplay.MovementController>)typeof(Remains.Gameplay.NpcController)
                    .GetField("movement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                cache.Add(npc.level,new Remains.Gameplay.MovementController(RoomLayout.Load()));
                for(int i=0;i<12;i++) controller.Step(1f/60);
                Require(npc.vx==0 && npc.pose=="idle" && npc.attackBusy && npc.x>0 && npc.x<.15f,"Подготовка выстрела сохраняет бег на месте или не тормозит");
                npc.y=1;npc.vy=0;controller.Step(.1f);
                Require(npc.y<1 && npc.vy<0 && npc.pose=="jump" && npc.attackBusy,"Подготовка выстрела отключает гравитацию");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}

