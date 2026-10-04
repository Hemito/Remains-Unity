using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class RevivalChecks
    {
        public static void Run()
        {
            CheckSelectedStack();
            var healer=new ActorState {id="healer",player=true,connected=true};
            healer.inventory.Add(SharedInventory.Create("heal",2));
            var target=new ActorState {id="target",player=true,connected=true,down=true,health=0,x=1};
            var timer=new RevivalProgress();
            Require(!timer.Step(healer,target,true,.7f),"Неполное удержание не завершает лечение");
            timer.Step(healer,target,false,.1f);Require(timer.Progress==0,"Отпускание прерывает удержание");
            timer.Step(healer,target,true,.7f);healer.x=5;timer.Step(healer,target,true,.1f);
            Require(timer.Progress==0 && InventoryJournal.Count(healer,"heal")==2,"Отход не расходует предмет");healer.x=0;
            timer.Step(healer,target,true,.7f);healer.damageSequence++;healer.health=100;
            timer.Step(healer,target,true,.1f);Require(timer.Progress==0,"Урон прерывает лечение даже после восстановления HP");
            timer.Step(healer,target,true,.7f);target.generation++;timer.Step(healer,target,true,.1f);
            Require(timer.Progress==0,"Переподключение цели отменяет старое удержание");
            Require(timer.Step(healer,target,true,1.5f),"Непрерывное удержание завершается");
            Require(HealingRules.Apply(healer,target,false) && target.health==35 && !target.down && InventoryJournal.Count(healer,"heal")==1,"Успех расходует один предмет и поднимает с 35 HP");
            Require(!HealingRules.Apply(healer,target,false) && InventoryJournal.Count(healer,"heal")==1,"Повтор не расходует второй предмет");
            healer.inventory.Clear();target.down=true;target.health=0;
            Require(!timer.Step(healer,target,true,2) && timer.Progress==0,"Без предмета действие не запускается");
            var blast=new AttackEvent {attacker=healer.id,sourcePlayer=true,level=healer.level,weapon="grenade"};
            Require(AttackScope.CanAffect(blast,healer,false),"Собственный взрыв не исключает пострадавшего");
            Require(!AttackScope.CanAffect(blast,target,false) && AttackScope.CanAffect(blast,target,true),"Дружественный огонь управляет уроном союзникам");
            blast.weapon="pistol";Require(!AttackScope.CanAffect(blast,healer,true),"Прямой выстрел не попадает в автора");
            Debug.Log("RevivalChecks PASS: удержание, отпускание, расстояние, урон, поколение, однократный расход.");
        }
        private static void CheckSelectedStack()
        {
            var actor=new ActorState {id="selected-heal",player=true,connected=true,health=20};
            var first=SharedInventory.Create("heal",2);var chosen=SharedInventory.Create("heal",3);
            var ammo=SharedInventory.Create("p10",5);
            actor.inventory.Add(first);actor.inventory.Add(chosen);actor.inventory.Add(ammo);
            Require(HealingRules.Apply(actor,actor,true,chosen.id) && chosen.count==2 && first.count==2 && actor.health==55,
                "Лечение списало первую стопку вместо выбранной");
            actor.inventoryReservations[chosen.id]=2;
            foreach(var id in new[]{chosen.id,"missing-stack",ammo.id,""})
                Require(!HealingRules.Apply(actor,actor,true,id) && actor.health==55 && first.count==2 && chosen.count==2 && ammo.count==5,
                    "Недоступная выбранная стопка заменилась другой или изменила здоровье: "+id);
            Require(HealingRules.Apply(actor,actor,true) && first.count==1 && actor.health==90,
                "Лечение горячей клавишей перестало выбирать доступную стопку");
            Debug.Log("Выбранная аптечка: PASS — точный расход, резерв/чужой тип/нет ID без подмены, горячая клавиша.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("RevivalChecks: "+reason);}
    }
}
