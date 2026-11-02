using Remains.Content;

namespace Remains.Domain
{
    // Hand grenades in an inventory (WeaponCatalog.IsGrenade: the hand-made grenade and the original's): the stack a
    // throw takes — the selected weapon when it is a grenade, else the first grenade stack in inventory order — and
    // how many there are.
    public static class GrenadeItems
    {
        public static ItemStack Next(ActorState actor)
        {
            if(actor==null) return null;
            if(!HeldItemSelection.HasSelection(actor) && WeaponInventory.Selected(actor) is { } selected && WeaponCatalog.IsGrenade(selected.definition) &&
                InventoryAvailability.Count(actor,selected)>0) return selected;
            foreach(var item in actor.inventory)
                if(WeaponCatalog.IsGrenade(item.definition) && InventoryAvailability.Count(actor,item)>0) return item;
            return null;
        }
        public static int Count(ActorState actor)
        {
            int count=0;
            if(actor!=null) foreach(var item in actor.inventory) if(WeaponCatalog.IsGrenade(item.definition)) count+=InventoryAvailability.Count(actor,item);
            return count;
        }
    }
}
