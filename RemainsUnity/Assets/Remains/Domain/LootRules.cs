namespace Remains.Domain
{
    public readonly struct LootAccessContext
    {
        public readonly string actorId,actorLevel,containerId,containerLevel;
        public readonly bool connected,down,body,bodyDown,bodyPresent;
        public readonly string bodyActorId;
        public readonly float distance;
        public LootAccessContext(ActorState actor,InventorySource container)
        {
            body=container.Body!=null;bodyDown=container.Body?.down??false;bodyPresent=ActorPresence.IsPresent(container.Body);bodyActorId=container.Body?.id;
            actorId=actor.id;actorLevel=actor.level;containerId=container.id;containerLevel=container.level;
            connected=actor.connected;down=actor.down;distance=UnityEngine.Vector2.Distance(actor.Position,container.Position);
        }
    }
    public readonly struct LootClaimContext
    {
        public readonly LootAccessContext access;
        public readonly string itemId,definition;
        public readonly int count,availableCount;
        public LootClaimContext(LootAccessContext access,ItemStack item,int quantity=0)
        {this.access=access;itemId=item.id;definition=item.definition;availableCount=item.count;count=quantity==0?item.count:quantity;}
    }
    // Pure decisions. Reservation, ordering, durable confirmation and delivery stay in SharedInventory.
    public interface ILootRule
    {
        string AccessRejection(LootAccessContext context);
        string ClaimRejection(LootClaimContext context);
    }
    public sealed class NearbyLootRule : ILootRule
    {
        public string AccessRejection(LootAccessContext context)
        {
            if(context.body && (!context.bodyDown || !context.bodyPresent || context.bodyActorId==context.actorId)) return "Инвентарь этого персонажа недоступен.";
            if(!context.connected || context.down) return "Подбор недоступен: персонаж не может действовать.";
            if(context.actorLevel!=context.containerLevel) return "Контейнер находится в другой локации.";
            if(context.distance>2) return "Подойдите ближе к контейнеру.";
            return null;
        }
        public string ClaimRejection(LootClaimContext context)=>null;
    }
}
