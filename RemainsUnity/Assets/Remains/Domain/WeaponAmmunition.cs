using System;
using System.Collections.Generic;
using Remains.Content;

namespace Remains.Domain
{
    [Serializable] public sealed class WeaponMagazine : WeaponItemState { public string weapon; }

    public static class WeaponAmmunition
    {
        // Imported loot uses item:<id>; earlier loadouts used <id>. Both refer to the same ammunition,
        // and a single journal debit consumes available quantities atomically without replacing stack IDs.
        private static string CompatibleAmmo(string id)=>string.IsNullOrEmpty(id)?null:id.StartsWith("item:",StringComparison.Ordinal)?id.Substring(5):"item:"+id;
        public static int Reserve(ActorState actor,WeaponDefinition definition)=>string.IsNullOrEmpty(definition.ammo)?0:
            InventoryJournal.Count(actor,definition.ammo,CompatibleAmmo(definition.ammo));
        public static WeaponItemState Magazine(ActorState actor,string weapon)
        {
            if(actor.weaponInventoryVersion==0) return actor.magazines.Find(m=>m.weapon==weapon);
            if(actor.weaponInventoryVersion!=1) throw new InvalidOperationException($"Неизвестная версия инвентаря оружия: actor={actor.id}, version={actor.weaponInventoryVersion}.");
            var selected=WeaponInventory.Selected(actor);
            return selected?.definition==weapon?selected.weaponState:null;
        }

        public static void Initialize(ActorState actor,bool starter)
        {
            if(actor.weaponInventoryVersion!=0) throw new InvalidOperationException("Старое оснащение нельзя выдавать поверх экземпляров оружия.");
            actor.magazines ??= new List<WeaponMagazine>();
            actor.inventoryUses ??= new List<InventoryUse>();
            foreach(var definition in WeaponCatalog.All)
            {
                // The legacy loadout covers the prototype's own weapons, not the original's arsenal connected as items.
                if(string.IsNullOrEmpty(definition.ammo) || Magazine(actor,definition.id)!=null || definition.id.StartsWith(ExportedWeaponItems.Prefix,StringComparison.Ordinal)) continue;
                actor.magazines.Add(new WeaponMagazine {weapon=definition.id,rounds=starter?definition.magazineSize:0});
                if(starter) InventoryStacks.Add(actor,SharedInventory.Create(definition.ammo,definition.magazineSize*3));
            }
            actor.loadoutVersion=1;
        }

        public static bool CanSpendShot(ActorState actor,WeaponDefinition definition,out string reason)
        {
            reason=null;
            if(actor.weaponInventoryVersion!=0 && !WeaponInventory.Available(actor,definition.id))
            {reason="Выбранное оружие отсутствует или ожидает передачи.";return false;}
            if(actor.mana<definition.manaCost) {reason="Недостаточно маны.";return false;}
            if(definition.UsesMagazine)
            {
                var magazine=Magazine(actor,definition.id);
                if(magazine==null) {reason=$"Нет магазина оружия {definition.id}. Проверьте сохранение и набор модов.";return false;}
                if(magazine.rounds<definition.ammoPerShot) {reason=definition.rechargeSeconds>0?"Оружие подзаряжается.":"Магазин пуст. Нажмите R для перезарядки.";return false;}
                if(magazine.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан номер состояния магазина.");
            }
            return true;
        }
        public static bool SpendShot(ActorState actor,WeaponDefinition definition,out string reason)
        {
            if(!CanSpendShot(actor,definition,out reason)) return false;
            if(definition.UsesMagazine)
            {
                var magazine=Magazine(actor,definition.id);
                magazine.rounds-=definition.ammoPerShot;magazine.revision++;
            }
            actor.mana-=definition.manaCost;
            return true;
        }

        public static int ReloadAmount(ActorState actor,WeaponDefinition definition)
        {
            if(actor.weaponInventoryVersion!=0 && !WeaponInventory.Available(actor,definition.id)) return 0;
            var magazine=Magazine(actor,definition.id);
            if(magazine==null || !definition.UsesMagazine || definition.rechargeSeconds>0) return 0;
            if(definition.unlimitedReserve) return Math.Max(0,definition.magazineSize-magazine.rounds);
            return Math.Max(0,Math.Min(definition.magazineSize-magazine.rounds,Reserve(actor,definition)));
        }

        public static bool CompleteReload(ActorState actor,WeaponDefinition definition)
        {
            int count=ReloadAmount(actor,definition);
            if(count==0) return false;
            var magazine=Magazine(actor,definition.id);
            if(magazine.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан номер состояния магазина.");
            if(!definition.unlimitedReserve && !InventoryJournal.Consume(actor,definition.ammo,count,CompatibleAmmo(definition.ammo))) return false;
            magazine.rounds+=count;magazine.revision++;
            actor.revision++;
            return true;
        }

        // Weapon.actions: t_rech counts only while below holder and t_attack == 0; each interval adds one round.
        public static bool AdvanceRecharge(WeaponItemState magazine,WeaponDefinition definition,ref float remaining,float dt)
        {
            if(!float.IsFinite(dt) || dt<0) throw new InvalidOperationException("Некорректный шаг самоподзарядки: "+dt);
            if(definition.rechargeSeconds<=0 || magazine.rounds>=definition.magazineSize || dt==0) return false;
            if(!float.IsFinite(remaining) || remaining<=0 || remaining>definition.rechargeSeconds)
                throw new InvalidOperationException($"Некорректный таймер самоподзарядки: weapon={definition.id}, remaining={remaining}.");
            remaining-=dt;
            if(remaining>0) return false;
            int count=Math.Min(definition.magazineSize-magazine.rounds,1+(int)Math.Floor(-remaining/definition.rechargeSeconds));
            if(magazine.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан номер состояния магазина.");
            magazine.rounds+=count;magazine.revision++;
            remaining=magazine.rounds==definition.magazineSize?definition.rechargeSeconds:remaining+count*definition.rechargeSeconds;
            return true;
        }
    }
}
