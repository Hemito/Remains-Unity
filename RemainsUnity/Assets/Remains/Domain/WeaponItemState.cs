using System;
using System.Collections.Generic;
using Remains.Serialization;
namespace Remains.Domain
{
    [Serializable] public sealed class WeaponItemState : ExtensibleState
    {
        public long revision;
        public int rounds;
        public WeaponItemState Copy()=>StateJson.Copy(this);
    }

    // Item presence/quantity comes from coordinated grants/debits; only the surviving
    // instance's mutable state can be updated by its owner. Never resurrect an item.
    public static class WeaponItemReplication
    {
        public static bool Merge(List<ItemStack> inventory,List<ItemStack> ownerItems)
        {
            var incoming=new Dictionary<string,ItemStack>();
            foreach(var item in ownerItems)
            {
                if(item==null || string.IsNullOrEmpty(item.id) || !incoming.TryAdd(item.id,item)) return false;
            }
            foreach(var item in inventory)
            {
                if(item.weaponState!=null && !Valid(item)) return false;
                if(!incoming.TryGetValue(item.id,out var proposed)) continue;
                if(item.weaponState==null && proposed.weaponState==null) continue;
                if(item.definition!=proposed.definition || item.weaponState==null || !Valid(proposed)) return false;
                if(proposed.weaponState.revision<item.weaponState.revision) continue;
                if(proposed.weaponState.revision==item.weaponState.revision)
                {
                    if(proposed.weaponState.rounds!=item.weaponState.rounds) return false;
                    continue;
                }
                item.weaponState=proposed.weaponState.Copy();
            }
            return true;
        }
        private static bool Valid(ItemStack item)=>item.count==1 && item.weaponState!=null &&
            item.weaponState.revision>=0 && item.weaponState.rounds>=0;
    }
}
