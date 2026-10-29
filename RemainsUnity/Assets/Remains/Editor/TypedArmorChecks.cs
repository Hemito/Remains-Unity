using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class TypedArmorChecks
    {
        private sealed class FixedDamage:IDamageRule,INaturalAttackDamageRule
        {public float Calculate(string weapon,float speed)=>100;public float Calculate(string type)=>100;}
        public static void Run()
        {
            ArmorProtectionCatalogChecks.Run();
            var armor=new ArmorProtectionProfile("check",10,25,1,new Dictionary<string,float>
                {{DamageTypes.Bullet,.2f},{DamageTypes.Laser,.5f},{DamageTypes.Acid,-.25f},{DamageTypes.Necrotic,.4f}});
            Equal(EquippedArmorProtection.Resolve(armor,DamageTypes.Bullet,100,10,"hit","target"),60,"Сопротивление должно применяться до физического поглощения");
            Equal(EquippedArmorProtection.Resolve(armor,DamageTypes.Laser,100,10,"hit","target"),15,"Лазер должен использовать энергетическую защиту");
            Equal(EquippedArmorProtection.Resolve(armor,DamageTypes.Acid,100,10,"hit","target"),105,"Кислота и отрицательное сопротивление");
            Equal(EquippedArmorProtection.Resolve(armor,DamageTypes.Necrotic,100,10,"hit","target"),60,"Особый тип обходит flat-защиту, но сохраняет сопротивление");
            Equal(EquippedArmorProtection.Resolve(armor,DamageTypes.Bullet,1,10,"hit","target"),0,"Нет обязательного минимального урона или лечения");
            int covered=0;for(int i=0;i<10000;i++) if(EquippedArmorProtection.Covered(.6f,"hit:"+i,"victim")) covered++;
            Require(covered>5800 && covered<6200,"Качество должно быть вероятностью, а не множителем поглощения: "+covered);
            Require(!EquippedArmorProtection.Covered(0,"hit","victim") && EquippedArmorProtection.Covered(1,"hit","victim"),"Крайние вероятности покрытия");
            foreach(var weapon in WeaponCatalog.All) DamageTypes.Validate(weapon.damageType);
            CheckEquipmentAndDamage();CheckNatural();CheckPhysical();CheckExplosion();
            ElectricDamageChecks.Run();
            Require(Remains.Presentation.ArmorProtectionDescription.For("armor.metal").Contains("уязвимость 30%"),"UI потерял отрицательное сопротивление");
            Require(WeaponCatalog.ManifestCopy().gameplayAssets.Any(input=>input.id==ArmorProtectionCatalog.Resource),"Защита не входит в манифест");
            Debug.Log($"TypedArmorChecks PASS: порядок сопротивления/поглощения, физический/энергетический/особый канал, покрытие {covered}/10000; оружие/укус/ящик/взрыв, надевание/снятие/последняя вещь/сохранение/handoff/дедупликация и UI.");
        }
        private static ActorState Actor()=>new() {id="armor-victim",character="check",player=true,connected=true,owner="peer",level="hub",health=1000,
            inventory={new ItemStack {id="outfit",definition="armor.metal",count=1}}};
        private static void Equip(ActorState actor)=>Require(CosmeticEquipment.TryEquip(actor,"outfit",false,out var reason),reason);
        private static AttackEvent Hit(string id,string weapon="pistol")=>new() {id=id,attacker="source",weapon=weapon,level="hub"};
        private static float Expected(ActorState actor,string type,string hit)
        {
            // Independent source formula for the actual metal base grade, with stable coverage admitted separately.
            var profile=ArmorProtectionCatalog.Current.Get("armor.metal");
            float flat=DamageTypes.Channel(type)==DamageArmorChannel.Physical?12:8;
            float resistance=profile.Resistance(type);
            return Math.Max(0,100*(1-resistance)-(EquippedArmorProtection.Covered(.6f,hit,actor.id)?flat:0));
        }
        private static void CheckEquipmentAndDamage()
        {
            var actor=Actor();var rule=new TypeHitProtectionRule();var attack=Hit("metal-shot");
            Equal(rule.Resolve(actor,attack,100),100,"Броня в сумке не защищает");
            Equip(actor);string before=StateJson.ToJson(actor);
            float expected=Expected(actor,DamageTypes.Bullet,attack.id);
            Equal(rule.Resolve(actor,attack,100),expected,"Надетая броня не защищает");
            Require(StateJson.ToJson(actor)==before,"Расчёт изменил состояние или расходовал вещь");
            var copy=StateJson.Copy(actor);copy.owner="new-owner";copy.generation++;
            Equal(rule.Resolve(copy,attack,100),expected,"Сохранение/handoff изменили покрытие");
            var receiver=new DamageReceiver(new FixedDamage());
            Require(receiver.Apply(actor,attack,true),"Реальное попадание отклонено");Equal(actor.health,1000-expected,"Применение урона обходит броню");
            Require(!receiver.Apply(actor,attack,true),"Повторный урон прошёл дважды");
            var laser=Hit("metal-laser","energy");Equal(rule.Resolve(actor,laser,100),Expected(actor,DamageTypes.Laser,laser.id),"Энергетическое оружие обходит тип");
            Require(CosmeticEquipment.TryEquip(actor,"outfit",true,out _),"Не удалось снять броню");
            Equal(rule.Resolve(actor,attack,100),100,"Снятая броня продолжает защищать");
            Equip(actor);actor.inventory.Clear();CosmeticEquipment.ReconcileInventory(actor);
            Equal(rule.Resolve(actor,attack,100),100,"Последний переданный предмет продолжает защищать");
        }
        private static void CheckNatural()
        {
            // Read the prepared registered actor set rather than infer damage from an effect or sound.
            string type=ActorTypesForChecks().First(id=>NpcNaturalAttack.Definition(id).damageType==DamageTypes.Fang);
            var actor=Actor();Equip(actor);
            var attack=Hit("fang-check","");attack.naturalAttackType=type;attack.naturalAttackTarget=actor.id;
            var definition=NpcNaturalAttack.Definition(type);attack.radius=definition.radius;attack.endX=definition.reach;
            var receiver=new DamageReceiver(new FixedDamage(),null,null,new FixedDamage());
            Require(receiver.Apply(actor,attack,true),"Укус отклонён");Equal(actor.health,1000-Expected(actor,DamageTypes.Fang,attack.id),"Естественная атака обходит броню");
        }
        private static IEnumerable<string> ActorTypesForChecks()
            =>EntityAssetCatalog.Load().CreateActors().Where(a=>a.naturalAttack!=null).Select(a=>a.id);
        private static void CheckPhysical()
        {
            var actor=Actor();Equip(actor);
            var sample=new PropImpactSample {id="box-sample",world="world",prop="box",owner="peer",level=actor.level,
                generation=1,sequence=1,at=1,width=1,height=1,anchorHeight=.5f,mass=50,vx=6};
            sample.id=PropImpactRules.SampleId(sample.world,sample.prop,sample.generation,sample.sequence);
            var contact=new PropImpactContact {source=sample,id=sample.id+":"+actor.id+":0",target=actor.id,generationBefore=1,generationAfter=2,
                until=2,damage=PropImpactRules.Damage(sample.mass,sample.Velocity)};
            float incoming=contact.damage;
            float expected=Math.Max(0,incoming-(EquippedArmorProtection.Covered(.6f,contact.id,actor.id)?12:0));
            var receiver=new DamageReceiver(new FixedDamage());Require(receiver.ApplyPhysical(actor,contact),"Ящик отклонён");
            Equal(actor.health,1000-expected,"Ящик обходит физическую защиту");Require(!receiver.ApplyPhysical(actor,contact),"Ящик повторил урон");
        }
        private static void CheckExplosion()
        {
            var actor=Actor();Equip(actor);
            var attack=Hit("explosion-check","grenade");attack.originX=actor.x;attack.originY=actor.y;attack.radius=100;
            var explosion=new ExplosionState {attack=attack,players=new List<string>{actor.id}};
            var receiver=new DamageReceiver(new FixedDamage());
            Require(GrenadeRules.Resolve(actor,explosion,receiver,actor.Position),"Взрыв отклонён");
            Equal(actor.health,1000-Expected(actor,DamageTypes.Explosion,attack.id),"Взрыв обходит защиту");
        }
        private static void Equal(float a,float b,string message)=>Require(Math.Abs(a-b)<.0002f,message+$": actual={a}, expected={b}");
        private static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException("Типы урона и броня: "+message);}
    }
}
