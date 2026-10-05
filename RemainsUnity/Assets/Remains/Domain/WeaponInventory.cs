using System;
using Remains.Content;
namespace Remains.Domain
{
    [Serializable] public sealed class WeaponShortcut : Remains.Serialization.ExtensibleState
    {public string slot,item;}
    public static class WeaponInventory
    {
        public static bool IsEquippable(ItemStack item)=>item!=null &&
            (item.weaponState!=null && item.count==1 || item.weaponState==null && item.definition=="grenade" && item.count>0);
        public static ItemStack Selected(ActorState actor)=>Equippable(actor,actor?.equippedWeaponItem);
        private static ItemStack Equippable(ActorState actor,string id)
        {var item=InventoryStacks.Find(actor,id);return IsEquippable(item)?item:null;}
        public static ItemStack Shortcut(ActorState actor,string slot)
        {
            var reference=actor?.weaponShortcuts.Find(value=>value.slot==slot);
            return reference==null?null:Equippable(actor,reference.item);
        }
        public static bool Available(ActorState actor,string definition)=>Selected(actor) is { } item &&
            item.definition==definition && InventoryAvailability.Count(actor,item)>0;
        public static bool Select(ActorState actor,string itemId,out string reason)
        {
            reason=null;
            if(actor==null || actor.weaponInventoryVersion!=1) {reason="Инвентарь оружия ещё не подготовлен.";return false;}
            if(string.IsNullOrEmpty(itemId)) {actor.equippedWeaponItem=null;actor.weapon=null;actor.weaponSelectionRevision++;actor.revision++;return true;}
            var item=InventoryStacks.Find(actor,itemId);
            if(!IsEquippable(item)) {reason="Этот предмет нельзя взять в копыта.";return false;}
            if(InventoryAvailability.Count(actor,item)<=0) {reason="Предмет зарезервирован для передачи.";return false;}
            _=WeaponCatalog.Get(item.definition);
            actor.equippedWeaponItem=item.id;actor.weapon=item.definition;actor.weaponSelectionRevision++;actor.revision++;return true;
        }
        public static bool Assign(ActorState actor,string slot,string itemId,out string reason)
            =>ItemShortcuts.Assign(actor,slot,itemId,out reason);
        public static void PreserveSelection(ActorState current,ActorState incoming)
        {
            if(incoming.weaponInventoryVersion!=1) return;
            if(ActorMotionReplication.SameLifetime(current,incoming) && current.weaponInventoryVersion==1 &&
                current.weaponSelectionRevision>incoming.weaponSelectionRevision)
            {
                incoming.equippedWeaponItem=current.equippedWeaponItem;
                incoming.weaponSelectionRevision=current.weaponSelectionRevision;
                incoming.weaponShortcuts=Remains.Serialization.StateJson.Copy(current.weaponShortcuts);
            }
            incoming.weapon=Selected(incoming)?.definition;
        }
        public static bool SelectShortcut(ActorState actor,string slot,out string reason)
        {
            var shortcut=actor?.weaponShortcuts.Find(value=>value.slot==slot);
            if(shortcut==null) {reason="Быстрый слот пуст.";return false;}
            return Select(actor,shortcut.item,out reason);
        }
    }
}
