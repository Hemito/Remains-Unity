using System;

namespace Remains.Domain
{
    public sealed class SharedInventory
    {
        private readonly WorldState world;
        private readonly ILootRule rule;
        public SharedInventory(WorldState world,ILootRule rule=null) {this.world=world;this.rule=rule??new NearbyLootRule();}

        // Host receipt order decides the winner. The client never grants speculatively.
        public bool PrepareClaim(string transaction, string containerId,string itemId, ActorState recipient)
            =>PrepareClaim(transaction,containerId,itemId,recipient,out _);
        public bool PrepareClaim(string transaction, string containerId,string itemId,ActorState recipient,out string reason,int quantity=0)
        {
            var container=InventorySource.Resolve(world,containerId);
            reason="Операция подбора уже обработана или не имеет ID.";
            if(InventoryTransactions.Contains(world,transaction)) return false;
            var item = container?.items.Find(candidate => candidate.id == itemId);
            reason=ContainerAccess.ClaimRejection(recipient,container,item,rule,quantity);
            if(reason!=null) return false;
            int amount=quantity==0?item.count:quantity;
            if(!container.Reserve(transaction,item,amount,out var reserved))
            {reason="Предмет зарезервирован или состояние источника изменилось.";return false;}
            item=reserved;
            world.revision++;
            world.pendingGrants.Add(new PendingGrant { transaction=transaction,recipient=recipient.id,containerId=containerId,item=item,preparedRevision=world.revision });
            return true;
        }

        public void CommitGrant(PendingGrant grant)
        {
            var recipient=world.Find(grant.recipient);
            if(!world.completedTransactions.Contains(grant.transaction))
            {
                if(recipient!=null) {recipient.inventory.Add(grant.item);recipient.revision++;}
                else
                {
                    var container=InventorySource.Resolve(world,grant.containerId);
                    if(container==null) throw new InvalidOperationException($"Нет источника резерва: transaction={grant.transaction}, container={grant.containerId}.");
                    container.Add(grant.item);
                    UnityEngine.Debug.LogWarning($"Резерв возвращён в контейнер: transaction={grant.transaction}, recipient={grant.recipient}, container={container.id}.");
                }
                world.completedTransactions.Add(grant.transaction);
            }
            world.pendingGrants.Remove(grant);
            world.revision++;
        }

        public static bool Consume(ActorState actor, string definition,string itemId=null)
        {
            var item = actor.inventory.Find(candidate => candidate.definition == definition && (itemId==null || candidate.id==itemId) && InventoryAvailability.Count(actor,candidate)>0);
            if (item == null) return false;
            if (--item.count == 0)
            {
                actor.inventory.Remove(item);
                if(actor.weaponInventoryVersion==1 && actor.equippedWeaponItem==item.id)
                    WeaponInventory.Select(actor,null,out _);
            }
            return true;
        }

        public static ItemStack Create(string definition, int count) => new()
        {
            id = Guid.NewGuid().ToString("N"), definition = definition, count = count
        };
    }
}
