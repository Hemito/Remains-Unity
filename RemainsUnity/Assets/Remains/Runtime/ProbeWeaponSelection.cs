using System;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;

namespace Remains.Runtime
{
    // Test fixtures own one instance of each requested definition. Production input uses IDs.
    internal static class ProbeWeaponSelection
    {
        private static ItemStack Unique(ActorState actor,string definition)
        {
            var items=actor.inventory.Where(item=>item.definition==definition && item.weaponState!=null && item.count==1).ToArray();
            if(items.Length!=1) throw new InvalidOperationException($"Проверке нужен единственный экземпляр: actor={actor.id}, definition={definition}, найдено={items.Length}.");
            return items[0];
        }
        public static PlayerCommand Resolve(ActorState actor,PlayerCommand command)
        {
            if(actor.weaponInventoryVersion!=1 || command.weapon==null) return command;
            if(command.weaponItem!=null || command.weaponShortcut!=null)
                throw new InvalidOperationException("Проверка задала одновременно тип и экземпляр оружия.");
            string item=command.weapon.Length==0?"":Unique(actor,command.weapon).id;
            if((actor.equippedWeaponItem??"")!=item) command.weaponItem=item;
            command.weapon=null;
            return command;
        }
        public static WeaponItemState Magazine(ActorState actor,string definition)
            =>actor.weaponInventoryVersion==1?Unique(actor,definition).weaponState:WeaponAmmunition.Magazine(actor,definition);
    }
}
