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
            var container=world.containers.Find(c=>c.id==containerId);
            reason="Операция подбора уже обработана или не имеет ID.";
            if(string.IsNullOrEmpty(transaction) || world.completedTransactions.Contains(transaction) || world.pendingGrants.Exists(p=>p.transaction==transaction)) return false;
            var item = container?.items.Find(candidate => candidate.id == itemId);
            reason=ContainerAccess.ClaimRejection(recipient,container,item,rule,quantity);
            if(reason!=null) return false;
            int amount=quantity==0?item.count:quantity;
            if(amount==item.count) container.items.Remove(item);
            else
            {
                var reserved=item.Copy();reserved.id=Guid.NewGuid().ToString("N");reserved.count=amount;
                item.count-=amount;item=reserved;
            }
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
                    var container=world.containers.Find(c=>c.id==grant.containerId);
                    if(container==null) throw new InvalidOperationException($"Нет источника резерва: transaction={grant.transaction}, container={grant.containerId}.");
                    container.items.Add(grant.item);
                    UnityEngine.Debug.LogWarning($"Резерв возвращён в контейнер: transaction={grant.transaction}, recipient={grant.recipient}, container={container.id}.");
                }
                world.completedTransactions.Add(grant.transaction);
            }
            world.pendingGrants.Remove(grant);
            world.revision++;
        }

        public static bool Consume(ActorState actor, string definition)
        {
            var item = actor.inventory.Find(candidate => candidate.definition == definition && candidate.count > 0);
            if (item == null) return false;
            if (--item.count == 0) actor.inventory.Remove(item);
            return true;
        }

        public static ItemStack Create(string definition, int count) => new()
        {
            id = Guid.NewGuid().ToString("N"), definition = definition, count = count
        };
    }
}
