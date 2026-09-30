using System;

namespace Remains.Domain
{
    public sealed class SharedInventory
    {
        private readonly WorldState world;
        public SharedInventory(WorldState world) => this.world = world;

        // Host receipt order decides the winner. The client never grants speculatively.
        public bool PrepareClaim(string transaction, string itemId, ActorState recipient)
        {
            if (world.completedTransactions.Contains(transaction) || world.pendingGrants.Exists(p=>p.transaction==transaction)) return false;
            var item = world.container.Find(candidate => candidate.id == itemId);
            if (item == null) return false;
            world.container.Remove(item);
            world.revision++;
            world.pendingGrants.Add(new PendingGrant { transaction=transaction,recipient=recipient.id,item=item,preparedRevision=world.revision });
            return true;
        }

        public void CommitGrant(PendingGrant grant)
        {
            var recipient=world.Find(grant.recipient);
            if(recipient==null) throw new InvalidOperationException($"Нет получателя транзакции {grant.transaction}: {grant.recipient}.");
            if(!world.completedTransactions.Contains(grant.transaction))
            {
                recipient.inventory.Add(grant.item);
                recipient.revision++;
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
