using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    public static class ItemShortcuts
    {
        private static readonly IReadOnlyList<string> keys=Array.AsReadOnly(new[]{"1","2","3","4","5","6","7","8","9","0","-","="});
        public static IReadOnlyList<string> Keys=>keys;
        public static ItemStack Get(ActorState actor,string slot)
        {
            var reference=actor?.weaponShortcuts.Find(value=>value.slot==slot);
            return reference==null?null:InventoryStacks.Find(actor,reference.item);
        }
        public static bool Assign(ActorState actor,string slot,string itemId,out string reason)
        {
            reason=null;
            if(actor==null || actor.weaponInventoryVersion!=1 || !Contains(slot))
            {reason="Не задана доступная быстрая клавиша предмета.";return false;}
            var item=string.IsNullOrEmpty(itemId)?null:InventoryStacks.Find(actor,itemId);
            if(!string.IsNullOrEmpty(itemId) && (item==null || item.count<=0))
            {reason="Нельзя назначить отсутствующий предмет.";return false;}
            var reference=actor.weaponShortcuts.Find(value=>value.slot==slot);
            actor.weaponShortcuts.RemoveAll(value=>value.slot==slot);
            if(item!=null)
            {
                reference??=new WeaponShortcut {slot=slot};
                reference.item=item.id;actor.weaponShortcuts.Add(reference);
            }
            actor.weaponSelectionRevision++;actor.revision++;return true;
        }
        public static bool Contains(string slot)
        {for(int i=0;i<keys.Count;i++) if(keys[i]==slot) return true;return false;}
    }
    public interface IItemActionRule {string Action(ItemStack item);}
    public sealed class StandardItemActions : IItemActionRule
    {
        public const string Equip="equip",Heal="heal",Hold="hold",Toss="toss",Wear="wear";
        public string Action(ItemStack item)=>item==null?null:WeaponInventory.IsEquippable(item)?Equip:
            item.definition=="heal"?Heal:CosmeticEquipment.IsWearable(item)?Wear:Hold;
    }
}
