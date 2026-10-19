using System;
using System.Collections.Generic;
using System.Linq;

namespace Remains.Domain
{
    [Serializable] public sealed class ItemDebit : Remains.Serialization.ExtensibleState { public string item; public int count; }
    [Serializable] public sealed class InventoryUse : Remains.Serialization.ExtensibleState { public string id; public List<ItemDebit> debits=new(); }

    public static class InventoryJournal
    {
        public static int Count(ActorState actor,string definition,string compatibleDefinition=null) => actor.inventory.Where(i=>i.definition==definition || compatibleDefinition!=null && i.definition==compatibleDefinition).Sum(i=>InventoryAvailability.Count(actor,i));

        public static bool Consume(ActorState actor,string definition,int count,string compatibleDefinition=null)
        {
            CosmeticEquipment.Validate(actor);
            if(count<=0 || Count(actor,definition,compatibleDefinition)<count) return false;
            var use=new InventoryUse {id=Guid.NewGuid().ToString("N")};
            int remaining=count;
            foreach(var item in actor.inventory.Where(i=>(i.definition==definition || compatibleDefinition!=null && i.definition==compatibleDefinition) && i.count>0))
            {
                int take=Math.Min(remaining,InventoryAvailability.Count(actor,item));
                if(take==0) continue;
                use.debits.Add(new ItemDebit {item=item.id,count=take});remaining-=take;
                if(remaining==0) break;
            }
            if(!Apply(actor.inventory,actor.inventoryAliases,use)) return false;
            actor.inventoryUses.Add(use);
            CosmeticEquipment.ReconcileInventory(actor);
            return true;
        }

        public static bool Debit(ActorState actor,string transaction,string item,int count)
        {
            CosmeticEquipment.Validate(actor);
            if(string.IsNullOrEmpty(transaction) || actor.inventoryUses.Exists(use=>use.id==transaction)) return false;
            var source=InventoryStacks.Find(actor,item);
            if(source==null || InventoryAvailability.Count(actor,source)<count) return false;
            var use=new InventoryUse {id=transaction,debits=new List<ItemDebit> {new() {item=item,count=count}}};
            if(!Apply(actor.inventory,actor.inventoryAliases,use)) return false;
            actor.inventoryUses.Add(use);CosmeticEquipment.ReconcileInventory(actor);return true;
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
                if(!Apply(inventory,authoritative.inventoryAliases,use)) return false;
                uses.Add(use);
            }
            return WeaponItemReplication.Merge(inventory,owner.inventory);
        }

        private static bool Apply(List<ItemStack> inventory,Dictionary<string,string> aliases,InventoryUse use)
        {
            if(use.debits==null || use.debits.Count==0) return false;
            var totals=new Dictionary<string,int>();
            foreach(var debit in use.debits)
            {
                if(string.IsNullOrEmpty(debit.item) || debit.count<=0) return false;
                string current=InventoryStacks.ResolveId(aliases,debit.item);
                totals.TryGetValue(current,out int previous);
                if(debit.count>int.MaxValue-previous) return false;
                totals[current]=previous+debit.count;
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
