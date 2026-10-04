using System;
using System.Linq;
using Remains.Domain;

namespace Remains.Persistence
{
    // Caller preserves the exact source before this explicit legacy conversion.
    public static class InventoryStackMigration
    {
        public static bool Needed(WorldState world)=>world.actors.Any(Needed) || world.containers.Any(container=>InventoryStacks.NeedsConsolidation(container.items));
        public static bool Needed(ActorState actor)=>InventoryStacks.NeedsConsolidation(actor.inventory);
        public static int Apply(ActorState actor)
        {
            int removed=InventoryStacks.Consolidate(actor.inventory,actor.inventoryAliases);
            if(removed==0) return 0;
            actor.equippedWeaponItem=InventoryStacks.ResolveId(actor.inventoryAliases,actor.equippedWeaponItem);
            foreach(var shortcut in actor.weaponShortcuts) shortcut.item=InventoryStacks.ResolveId(actor.inventoryAliases,shortcut.item);
            actor.revision++;return removed;
        }
        public static WorldState Prepare(WorldState source,out int removed)
        {
            var world=source.Copy();removed=0;
            foreach(var actor in world.actors) removed+=Apply(actor);
            foreach(var container in world.containers) removed+=InventoryStacks.Consolidate(container.items,container.itemAliases);
            if(removed>0) world.revision++;
            return world;
        }
    }
}
