using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorHoldChecks
    {
        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();
            var world=new WorldState();
            var holder=new ActorState {id="A",owner="a",player=true,connected=true,x=-3};
            var victim=new ActorState {id="B",owner="b",player=true,connected=true,x=3};
            var npc=new ActorState {id="npc",owner="b",actorType="raider",anatomy="raider",x=2,health=140,target=victim.id};
            npc.inventory.Add(SharedInventory.Create("heal",3));
            NpcWeaponTestItems.Equip(npc);
            world.actors.AddRange(new[]{holder,victim,npc});
            var rules=new MassLimitedTelekinesisRule(TelekinesisSettings.Load());
            Require(rules.Rejection(holder,new TelekinesisTarget(npc),false)?.Contains("55 кг")==true,"Нет отказа по фактической массе рейдера");
            var target=TelekinesisTarget.Nearest(world,holder,new TelekinesisTarget(npc).Center);
            Require(target?.Body==npc && world.props.Count==0,"Тело выбирается копией предмета");
            NpcAttackRules.Begin(npc,victim);string attack=npc.attackId;
            ActorHoldRules.Begin(holder,npc,"first");
            var ownership=new OwnershipService();ownership.Reassign(world,new[]{"a","b"});
            Require(npc.owner=="b" && npc.hold.pendingAttack && npc.attackId==attack && npc.attackTarget==victim.id,"Передача прервала старую атаку");
            NpcWeaponTestItems.Finish(npc,victim);ownership.Reassign(world,new[]{"a","b"});
            Require(npc.owner=="a" && ActorHoldRules.Active(npc),"Владелец не передан после окончания атаки");
            Require(ActorHoldRules.Rejection(world,victim,npc)!=null,"Разрешены два держателя");
            Require(ActorHoldRules.SetTarget(npc,holder.id,npc.holdEpoch,1,new Vector2(1,4)) &&
                !ActorHoldRules.SetTarget(npc,holder.id,npc.holdEpoch,1,Vector2.zero),"Повтор цели применён дважды");
            var stale=world.Copy().Find(npc.id);stale.hold.sequence=0;
            ActorHoldRules.PreserveNewerTarget(npc,stale);
            Require(stale.hold.Target==new Vector2(1,4),"Снимок откатил цель удержания");
            Require(ActorHoldRules.Release(npc,holder.id,"first"),"Не отпущено первое удержание");
            ActorHoldRules.Begin(holder,npc,"second");
            Require(!ActorHoldRules.Release(npc,holder.id,"first"),"Старое отпускание сняло новый хват");
            ActorHoldRules.Release(npc);
            ActorHoldRules.Begin(holder,victim,"player");ownership.Reassign(world,new[]{"a","b"});
            Require(victim.owner=="b" && victim.connected,"Подключённый игрок потерял владельца симуляции");
            holder.down=true;ownership.Reassign(world,new[]{"a","b"});
            Require(victim.hold==null,"Нокдаун держателя не отпустил жертву");holder.down=false;
            foreach(string condition in new[]{"mana","disconnect","level"})
            {
                ActorHoldRules.Begin(holder,victim,"condition");
                if(condition=="mana") holder.mana=0;
                if(condition=="disconnect") holder.connected=false;
                if(condition=="level") holder.level="hub";
                ownership.Reassign(world,new[]{"a","b"});
                Require(victim.hold==null,"Не отпущено тело при условии "+condition);
                holder.mana=100;holder.connected=true;holder.owner="a";holder.level=victim.level;
            }
            victim.connected=false;victim.down=true;ActorHoldRules.Begin(holder,victim,"offline");
            ownership.Reassign(world,new[]{"a"});
            Require(victim.owner=="a" && ActorHoldRules.Active(victim),"Отключённое тело не передано держателю");
            ActorHoldRules.Release(victim);victim.connected=true;victim.owner="b";
            foreach(int fps in new[]{30,60,144})
            {
                npc.Position=new Vector2(0,4);npc.down=true;npc.health=0;npc.attackBusy=false;
                ActorHoldRules.Begin(holder,npc,"motion");
                var room=new RoomLayout {left=-10,right=10,bottom=0,top=10,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                    new RoomSurface {x=-10,y=-1,width=20,height=1},new RoomSurface {x=-3,y=2.9f,width=6,height=.1f,oneWay=true}}};
                npc.hold.targetX=0;npc.hold.targetY=1+Anatomy.Movement(npc).height*.5f;
                for(int i=0;i<fps;i++) ActorHoldMotion.Step(npc,room,1f/fps);
                Require(npc.y<1.01f && npc.health==0 && npc.inventory[0].count==3,"Спуск тела меняет здоровье/вещи или заблокирован балкой");
                ActorHoldRules.Release(npc);
            }
            ActorHoldRules.Begin(holder,npc,"reload");var restored=world.Copy();ownership.BeginSession(restored);
            var saved=restored.Find(npc.id);
            Require(saved.hold==null && saved.Position==npc.Position && saved.inventory[0].count==3,"Перезапуск потерял тело или сохранил захват");
            Debug.Log("Захват актёров: PASS — масса, один ID, атака/передача, подключённый владелец, пакеты цели, устаревшее отпускание, спуск 30/60/144 FPS и восстановление.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка захвата актёров: "+reason);}
    }
}
