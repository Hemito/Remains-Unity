using System;

namespace Remains.Domain
{
    public sealed class SharedInventory
    {
        private readonly WorldState world;
        public SharedInventory(WorldState world) => this.world = world;

        // Host receipt order decides the winner. The client never grants speculatively.
        public bool PrepareClaim(string transaction, string containerId,string itemId, ActorState recipient)
        {
            var container=world.containers.Find(c=>c.id==containerId);
            if(string.IsNullOrEmpty(transaction) || ContainerAccess.Rejection(recipient,container)!=null) return false;
            if (world.completedTransactions.Contains(transaction) || world.pendingGrants.Exists(p=>p.transaction==transaction)) return false;
            var item = container.items.Find(candidate => candidate.id == itemId);
            if (item == null) return false;
            container.items.Remove(item);
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
