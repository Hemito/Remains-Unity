using System;
using Remains.Content;
namespace Remains.Domain
{
    [Serializable] public sealed class WeaponShortcut : Remains.Serialization.ExtensibleState
    {public string slot,item;}
    public static class WeaponInventory
    {
        public static ItemStack Selected(ActorState actor)=>actor?.inventory.Find(item=>item.id==actor.equippedWeaponItem && item.weaponState!=null && item.count==1);
        public static bool Available(ActorState actor,string definition)=>Selected(actor) is { } item &&
            item.definition==definition && InventoryAvailability.Count(actor,item)==1;
        public static bool Select(ActorState actor,string itemId,out string reason)
        {
            reason=null;
            if(actor==null || actor.weaponInventoryVersion!=1) {reason="Инвентарь оружия ещё не подготовлен.";return false;}
            if(string.IsNullOrEmpty(itemId)) {actor.equippedWeaponItem=null;actor.weapon=null;actor.revision++;return true;}
            var item=actor.inventory.Find(candidate=>candidate.id==itemId);
            if(item?.weaponState==null || item.count!=1) {reason="Этот экземпляр оружия отсутствует в личном инвентаре.";return false;}
            if(InventoryAvailability.Count(actor,item)!=1) {reason="Оружие зарезервировано для передачи.";return false;}
            _=WeaponCatalog.Get(item.definition);
            actor.equippedWeaponItem=item.id;actor.weapon=item.definition;actor.revision++;return true;
        }
        public static bool Assign(ActorState actor,string slot,string itemId,out string reason)
        {
            reason=null;
            if(actor==null || actor.weaponInventoryVersion!=1 || string.IsNullOrWhiteSpace(slot))
            {reason="Не задан быстрый слот оружия.";return false;}
            if(!string.IsNullOrEmpty(itemId) && !actor.inventory.Exists(item=>item.id==itemId && item.weaponState!=null && item.count==1))
            {reason="Нельзя назначить отсутствующий экземпляр оружия.";return false;}
            actor.weaponShortcuts.RemoveAll(shortcut=>shortcut.slot==slot);
            if(!string.IsNullOrEmpty(itemId)) actor.weaponShortcuts.Add(new WeaponShortcut {slot=slot,item=itemId});
            actor.revision++;return true;
        }
        public static bool SelectShortcut(ActorState actor,string slot,out string reason)
        {
            var shortcut=actor?.weaponShortcuts.Find(value=>value.slot==slot);
            if(shortcut==null) {reason="Быстрый слот пуст.";return false;}
            return Select(actor,shortcut.item,out reason);
        }
    }
}
