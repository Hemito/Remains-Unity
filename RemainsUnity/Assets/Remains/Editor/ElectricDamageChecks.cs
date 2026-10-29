using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ElectricDamageChecks
    {
        private sealed class FixedDamage:IDamageRule {public float Calculate(string weapon,float speed)=>100;}
        public static void Run()
        {
            var room=new RoomLayout {id="hub",left=-10,right=10,bottom=-5,top=10,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
            RoomSurfaces.Prepare(room);
            var electric=WeaponCatalog.All.Where(w=>w.damageType==DamageTypes.Electric).ToArray();
            Require(electric.Length>0,"Не найдено электрическое оружие для проверки");
            foreach(var weapon in electric)
            {
                var actor=Actor();var attack=Hit(weapon.id,"electric-contact");
                var protection=new TypeHitProtectionRule(a=>DamageTargetContact.InDryRoom(a,room));
                actor.pose="jump";Equal(protection.Resolve(actor,attack,100),100,"Пол определяет контакт независимо от картинки");
                actor.y=2;actor.pose="idle";Equal(protection.Resolve(actor,attack,100),50,"Неподвижный апекс без опоры");
                actor.vy=3;Equal(protection.Resolve(actor,attack,100),50,"Подъём в прыжке");
                actor.vy=-3;Equal(protection.Resolve(actor,attack,100),50,"Падение");
                actor.climbing=true;Equal(protection.Resolve(actor,attack,100),100,"На лестнице нет скидки");
                actor.climbing=false;actor.y=0;actor.vy=3;Equal(protection.Resolve(actor,attack,100),50,"Начало прыжка ещё касается высоты пола");
                actor.y=2;actor.vy=0;
                Equal(new TypeHitProtectionRule(_=>new DamageTargetContact(false,true,false)).Resolve(actor,attack,100),100,"Контракт контакта в воде");
                var copy=StateJson.Copy(actor);copy.owner="new-owner";copy.generation++;
                Equal(protection.Resolve(copy,attack,100),50,"После смены владельца используется текущая геометрия");
            }
            CheckOrderAndReceipt(room,electric[0].id);
            var target=Actor();target.y=2;
            var ordinary=new TypeHitProtectionRule(_=>throw new InvalidOperationException("Неэлектрический урон запросил контакт"));
            Equal(ordinary.Resolve(target,Hit("pistol","ordinary"),100),100,"Другой тип не должен зависеть от контакта");
            string before=StateJson.ToJson(target);bool rejected=false;
            try {new DamageReceiver(new FixedDamage()).Apply(target,Hit(electric[0].id,"missing-contact"),true);}
            catch(InvalidOperationException error) {rejected=error.Message.Contains("контакт цели") && error.Message.Contains(target.id);}
            Require(rejected && StateJson.ToJson(target)==before,"Отсутствие контакта не должно молча менять урон/HP");
            target.y=0;target.vy=0;
            Require(DamageTargetContact.InDryRoom(target,room).Grounded,"Fixture не стоит на опоре");
            room.platforms=Array.Empty<RoomSurface>();RoomSurfaces.Prepare(room);
            Require(!DamageTargetContact.InDryRoom(target,room).Grounded,"Исчезнувшая опора осталась в контакте");
            Debug.Log($"ElectricDamageChecks PASS: оружие={electric.Length}; пол/прыжок/апекс/падение/лестница, контракт воды, текущая геометрия, 100→50 до брони, отрицательное сопротивление, receipt и громкий отказ без контекста. Водная симуляция не добавлялась.");
        }
        private static void CheckOrderAndReceipt(RoomLayout room,string weapon)
        {
            var actor=Actor();actor.y=2;
            actor.inventory.Add(new ItemStack {id="metal",definition="armor.metal",count=1});
            Require(CosmeticEquipment.TryEquip(actor,"metal",false,out _),"Не удалось надеть металл");
            string id=Enumerable.Range(0,100).Select(i=>"electric-armor:"+i).First(i=>EquippedArmorProtection.Covered(.6f,i,actor.id));
            var attack=Hit(weapon,id);int calls=0;
            var protection=new TypeHitProtectionRule(a=>{calls++;return DamageTargetContact.InDryRoom(a,room);});
            // Metal: electric vulnerability -0.3, marmor8, covered. Correct: 100*.5*1.3-8 =57, not (130-8)*.5 =61.
            var receiver=new DamageReceiver(new FixedDamage(),protection:protection);
            Require(receiver.Apply(actor,attack,true),"Электрическое попадание отклонено");Equal(actor.health,943,"Половина должна применяться до брони");
            Require(!receiver.Apply(actor,attack,true) && calls==1 && actor.health==943,"Повтор перебросил контакт или нанёс урон");
        }
        private static ActorState Actor()=>new() {id="electric-victim",character="check",player=true,connected=true,owner="peer",level="hub",health=1000};
        private static AttackEvent Hit(string weapon,string id)=>new() {id=id,attacker="source",weapon=weapon,level="hub"};
        private static void Equal(float actual,float expected,string message)=>Require(Math.Abs(actual-expected)<.0002f,message+$": actual={actual}, expected={expected}");
        private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException("Электрический урон: "+message);}
    }
}
