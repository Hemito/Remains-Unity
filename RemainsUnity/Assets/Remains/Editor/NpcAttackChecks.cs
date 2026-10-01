using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcAttackChecks
    {
        public static void Run()
        {
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
    }
}
