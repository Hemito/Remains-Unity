using System;
using System.Collections.Generic;
using System.Linq;

namespace Remains.Domain
{
    [Serializable] public sealed class ItemDebit : Remains.Serialization.ExtensibleState { public string item; public int count; }
    [Serializable] public sealed class InventoryUse : Remains.Serialization.ExtensibleState { public string id; public List<ItemDebit> debits=new(); }

    public static class InventoryJournal
    {
        public static int Count(ActorState actor,string definition) => actor.inventory.Where(i=>i.definition==definition).Sum(i=>i.count);

        public static bool Consume(ActorState actor,string definition,int count)
        {
            if(count<=0 || Count(actor,definition)<count) return false;
            var use=new InventoryUse {id=Guid.NewGuid().ToString("N")};
            int remaining=count;
            foreach(var item in actor.inventory.Where(i=>i.definition==definition && i.count>0))
            {
                int take=Math.Min(remaining,item.count);
                use.debits.Add(new ItemDebit {item=item.id,count=take});remaining-=take;
                if(remaining==0) break;
            }
            if(!Apply(actor.inventory,use)) return false;
            actor.inventoryUses.Add(use);
            return true;
        }

        // Rebase owner debits on the authoritative inventory, preserving concurrent grants.
        public static bool Merge(ActorState authoritative,ActorState owner,out List<ItemStack> inventory,out List<InventoryUse> uses)
        {
            inventory=authoritative.inventory.Select(i=>i.Copy()).ToList();
            uses=new List<InventoryUse>(authoritative.inventoryUses);
            var accepted=new HashSet<string>(uses.Select(u=>u.id));
            foreach(var use in owner.inventoryUses)
            {
                if(string.IsNullOrEmpty(use.id)) return false;
                if(!accepted.Add(use.id)) continue;
                if(!Apply(inventory,use)) return false;
                uses.Add(use);
            }
            return true;
        }

        private static bool Apply(List<ItemStack> inventory,InventoryUse use)
        {
            if(use.debits==null || use.debits.Count==0) return false;
            var totals=new Dictionary<string,int>();
            foreach(var debit in use.debits)
            {
                if(string.IsNullOrEmpty(debit.item) || debit.count<=0) return false;
                totals.TryGetValue(debit.item,out int previous);
                if(debit.count>int.MaxValue-previous) return false;
                totals[debit.item]=previous+debit.count;
            }
            foreach(var pair in totals)
                if(inventory.Find(i=>i.id==pair.Key) is not { } item || item.count<pair.Value) return false;
            foreach(var pair in totals)
            {
                var item=inventory.Find(i=>i.id==pair.Key);item.count-=pair.Value;
                if(item.count==0) inventory.Remove(item);
            }
            return true;
        }
    }
}
