using System;
namespace Remains.Domain
{
    public static class InventoryAvailability
    {
        public static int Count(ActorState actor,ItemStack item)
        {actor.inventoryReservations.TryGetValue(item.id,out int reserved);return Math.Max(0,item.count-reserved);}
    }
}
