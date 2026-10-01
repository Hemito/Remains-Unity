using System;

namespace Remains.Domain
{
    [Serializable]
    public sealed class PendingDeposit : Remains.Serialization.ExtensibleState
    {
        public string transaction,source,containerId;
        public ItemStack item;
        public long preparedRevision;
    }
    public readonly struct DepositContext
    {
        public readonly LootAccessContext access;
        public readonly string itemId,definition;
        public readonly int count,availableCount;
        public DepositContext(ActorState source,InventorySource destination,ItemStack item,int amount)
        {access=new LootAccessContext(source,destination);itemId=item.id;definition=item.definition;count=amount;availableCount=item.count;}
    }
    public interface IDepositRule {string AccessRejection(LootAccessContext context);string Rejection(DepositContext context);}
    public sealed class NearbyDepositRule : IDepositRule
    {
        private readonly NearbyLootRule access=new();
        public string AccessRejection(LootAccessContext context)=>access.AccessRejection(context);
        public string Rejection(DepositContext context)=>null;
    }

    // Domain transaction only. The coordinator must persist/replicate Prepare before allowing Commit.
    public sealed class SharedDeposits
    {
        private readonly WorldState world;
        private readonly IDepositRule rule;
        public SharedDeposits(WorldState world,IDepositRule rule=null)
        {this.world=world;this.rule=rule??new NearbyDepositRule();}
        public bool Prepare(string transaction,ActorState source,string containerId,string itemId,int quantity,out string reason)
        {
            reason="Операция внесения уже обработана или не имеет ID.";
            if(InventoryTransactions.Contains(world,transaction)) return false;
            reason="Отправитель или контейнер больше недоступен.";
            var destination=InventorySource.Resolve(world,containerId);
            if(source==null || world.Find(source.id)!=source || destination==null) return false;
            var item=source.inventory.Find(i=>i.id==itemId);
            reason="Указанное количество недоступно. Обновите выбор.";
            if(item==null || item.count<=0 || quantity<0 || quantity>item.count) return false;
            int amount=quantity==0?item.count:quantity;
            reason=rule.AccessRejection(new LootAccessContext(source,destination))??rule.Rejection(new DepositContext(source,destination,item,amount));
            if(reason!=null) return false;
            var reserved=item.ReservedCopy(amount);
            if(!InventoryJournal.Debit(source,transaction,item.id,amount))
            {reason="Расход предмета уже зарегистрирован или состояние изменилось.";return false;}
            source.revision++;world.revision++;
            world.pendingDeposits.Add(new PendingDeposit {transaction=transaction,source=source.id,containerId=containerId,item=reserved,preparedRevision=world.revision});
            return true;
        }
        public bool Commit(string transaction)=>Finish(transaction,false);
        public bool Cancel(string transaction)=>Finish(transaction,true);
        private bool Finish(string transaction,bool cancel)
        {
            if(world.completedTransactions.Contains(transaction)) return false;
            var pending=world.pendingDeposits.Find(p=>p.transaction==transaction);
            if(pending==null) return false;
            if(cancel)
            {
                var source=world.Find(pending.source)??throw new InvalidOperationException($"Нет отправителя резерва: transaction={transaction}, actor={pending.source}.");
                source.inventory.Add(pending.item);source.revision++;
            }
            else
            {
                var destination=InventorySource.Resolve(world,pending.containerId)??throw new InvalidOperationException($"Нет контейнера резерва: transaction={transaction}, container={pending.containerId}.");
                destination.Add(pending.item);
            }
            world.pendingDeposits.Remove(pending);world.completedTransactions.Add(transaction);world.revision++;
            return true;
        }
    }
    public static class InventoryTransactions
    {
        public static bool Contains(WorldState world,string id)=>string.IsNullOrEmpty(id) || world.completedTransactions.Contains(id) ||
            world.pendingGrants.Exists(p=>p.transaction==id) || world.pendingDeposits.Exists(p=>p.transaction==id);
    }
}
