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
            CheckKnockdownDuringAttack();
            var world=new WorldState();
            var a=new ActorState {id="a",player=true,connected=true,owner="a",x=2};
            var b=new ActorState {id="b",player=true,connected=true,owner="b",x=8};
            var npc=new ActorState {id="npc",owner="a",target=a.id};
            world.actors.Add(a);world.actors.Add(b);world.actors.Add(npc);
            var ownership=new OwnershipService();NpcAttackRules.Begin(npc,a);string id=npc.attackId;
            b.x=1;npc.target=b.id;
            Require(!ownership.ReassignAndReportChanges(world,new[]{"a","b"}),"Незавершённая атака ошибочно требует нового снимка владения");
            Require(npc.owner=="a" && npc.target==b.id && npc.attackTarget==a.id,"Начатая атака сохраняет свою цель и владельца при новом решении AI");
            var attack=NpcAttackRules.Complete(npc,a);
            Require(ownership.ReassignAndReportChanges(world,new[]{"a","b"}),"Передача NPC не отмечена для сохранения");
            Require(!ownership.ReassignAndReportChanges(world,new[]{"a","b"}),"Неизменное владение требует повторного сохранения");
            Require(attack.id==id && npc.owner=="b" && npc.target==b.id && npc.attackCooldown==2,"Завершение передаёт владельца с сохранённой паузой");
            var restored=npc.Copy();Require(restored.attackCooldown==2 && restored.attackId==id,"Пауза и ID переживают сериализацию");
            NpcAttackRules.Begin(npc,b);b.connected=false;ownership.Reassign(world,new[]{"a"});
            Require(npc.owner=="a" && !npc.attackBusy && npc.attackTarget==null && npc.attackId==null && npc.attackCooldown==2,"Обрыв отменяет старую атаку и оставляет паузу новому владельцу");
            var old=npc.Copy();NpcAttackRules.Begin(npc,a);npc.attackRemaining=.2f;
            NpcAttackRules.CopyProgress(old,npc);Require(npc.attackRemaining==.2f,"Поздний прогресс предыдущей атаки не переписывает новую");
            Debug.Log("NpcAttackChecks PASS: цель, передача после завершения, пауза, сериализация и отмена при обрыве.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("NpcAttackChecks: "+reason);}
        private static void CheckKnockdownDuringAttack()
        {
            var world=new WorldState();
            var far=new ActorState {id="far",player=true,connected=true,owner="far",x=8};
            var near=new ActorState {id="near",player=true,connected=true,owner="near",x=1};
            var npc=new ActorState {id="wounded",actorType="raider",owner="far",target=far.id,health=1,x=0};
            world.actors.Add(far);world.actors.Add(near);world.actors.Add(npc);
            NpcAttackRules.Begin(npc,far);
            var receiver=new DamageReceiver(new PrototypeDamageRule());
            var hit=new AttackEvent {id="interrupt",attacker=near.id,weapon="pistol",level=npc.level};
            Require(receiver.Apply(npc,hit,true),"Попадание по атакующему NPC не применено");
            Require(npc.down && npc.target==null && !npc.attackBusy && npc.attackId==null && npc.attackTarget==null && npc.attackRemaining==0,
                "Нокаут сохранил незавершённую атаку или цель NPC");
            Require(!receiver.Apply(npc,hit,true),"Повторное попадание применилось к телу");
            Require(new OwnershipService().ReassignAndReportChanges(world,new[]{"far","near"}) && npc.owner=="near",
                "Незавершённая атака удерживает тело у прежнего владельца");
            Require(NpcAttackRules.Complete(npc,far)==null,"Позднее завершение атаки тела создало выстрел");
            // A recovered legacy snapshot can still contain a down actor with a pending attack.
            npc.attackBusy=true;npc.attackId="stale";npc.attackTarget=far.id;npc.attackRemaining=.1f;
            Require(NpcAttackRules.Complete(npc,far)==null && !npc.attackBusy && npc.attackId==null,
                "Позднее завершение из сохранённого состояния создало выстрел тела");
        }
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
                geometry.Reconcile(world);
                var controller=new Remains.Gameplay.NpcController(session,geometry);
                var cache=(System.Collections.Generic.Dictionary<string,Remains.Gameplay.MovementController>)typeof(Remains.Gameplay.NpcController)
                    .GetField("movement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                cache.Add(npc.level,new Remains.Gameplay.MovementController(geometry.Get(npc.level)));
                for(int i=0;i<12;i++) controller.Step(1f/60);
                Require(npc.vx==0 && npc.pose=="idle" && npc.attackBusy && npc.x>0 && npc.x<.15f,"Подготовка выстрела сохраняет бег на месте или не тормозит");
                npc.y=1;npc.vy=0;controller.Step(.1f);
                Require(npc.y<1 && npc.vy<0 && npc.pose=="jump" && npc.attackBusy,"Подготовка выстрела отключает гравитацию");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}

