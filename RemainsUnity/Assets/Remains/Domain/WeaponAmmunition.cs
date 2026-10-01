using System;
using System.Collections.Generic;
using Remains.Content;

namespace Remains.Domain
{
    [Serializable] public sealed class WeaponMagazine : Remains.Serialization.ExtensibleState { public string weapon; public int rounds; }

    public static class WeaponAmmunition
    {
        public static WeaponMagazine Magazine(ActorState actor,string weapon) => actor.magazines.Find(m=>m.weapon==weapon);

        public static void Initialize(ActorState actor,bool starter)
        {
            actor.magazines ??= new List<WeaponMagazine>();
            actor.inventoryUses ??= new List<InventoryUse>();
            foreach(var definition in WeaponCatalog.All)
            {
                if(string.IsNullOrEmpty(definition.ammo) || Magazine(actor,definition.id)!=null) continue;
                actor.magazines.Add(new WeaponMagazine {weapon=definition.id,rounds=starter?definition.magazineSize:0});
                if(starter) actor.inventory.Add(SharedInventory.Create(definition.ammo,definition.magazineSize*3));
            }
            actor.loadoutVersion=1;
        }

        public static bool SpendShot(ActorState actor,WeaponDefinition definition,out string reason)
        {
            reason=null;
            if(actor.mana<definition.manaCost) {reason="Недостаточно маны.";return false;}
            if(!string.IsNullOrEmpty(definition.ammo))
            {
                var magazine=Magazine(actor,definition.id);
                if(magazine==null) {reason=$"Нет магазина оружия {definition.id}. Проверьте сохранение и набор модов.";return false;}
                if(magazine.rounds<definition.ammoPerShot) {reason="Магазин пуст. Нажмите R для перезарядки.";return false;}
                magazine.rounds-=definition.ammoPerShot;
            }
            actor.mana-=definition.manaCost;
            return true;
        }

        public static int ReloadAmount(ActorState actor,WeaponDefinition definition)
        {
            var magazine=Magazine(actor,definition.id);
            if(magazine==null || string.IsNullOrEmpty(definition.ammo)) return 0;
            return Math.Max(0,Math.Min(definition.magazineSize-magazine.rounds,InventoryJournal.Count(actor,definition.ammo)));
        }

        public static bool CompleteReload(ActorState actor,WeaponDefinition definition)
        {
            int count=ReloadAmount(actor,definition);
            if(count==0 || !InventoryJournal.Consume(actor,definition.ammo,count)) return false;
            Magazine(actor,definition.id).rounds+=count;
            actor.revision++;
            return true;
        }
    }
}
