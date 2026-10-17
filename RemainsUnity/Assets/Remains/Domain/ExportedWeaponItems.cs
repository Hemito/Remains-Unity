using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;

namespace Remains.Domain
{
    // The original's weapons connected from the export are WeaponCatalog definitions whose ID is the formal item ID
    // (weapon:<id>:grade:<n>, FirearmAuthoring): an item of that definition is the working weapon once it carries its
    // magazine state. Loot gives new ones an empty magazine; stacks saved while the weapon was still formal are
    // activated on load.
    public static class ExportedWeaponItems
    {
        public const string Prefix="weapon:";
        public static bool Working(string definition)=>definition!=null && definition.StartsWith(Prefix,StringComparison.Ordinal) &&
            WeaponCatalog.Contains(definition) && !WeaponCatalog.IsExplosive(definition);
        public static bool Needed(IEnumerable<ItemStack> items)=>items!=null && items.Any(item=>item!=null && item.weaponState==null && Working(item.definition));
        // A formal stack gets an empty magazine; a merged stack of several weapons becomes one stack per weapon
        // (the first keeps its ID, the others get "<id>:2", "<id>:3"...). Returns how many stacks changed.
        public static int Activate(List<ItemStack> items)
        {
            if(items==null) return 0;
            int changed=0;
            for(int i=0;i<items.Count;i++)
            {
                var item=items[i];
                if(item==null || item.weaponState!=null || !Working(item.definition)) continue;
                int count=item.count;
                if(count<=0) throw new InvalidOperationException($"Пустая стопка оружия: item={item.id}, definition={item.definition}.");
                item.count=1;item.weaponState=new WeaponItemState {rounds=0};changed++;
                for(int n=2;n<=count;n++)
                {
                    string id=item.id+":"+n;
                    if(items.Exists(other=>other?.id==id)) throw new InvalidOperationException($"Нельзя разделить стопку оружия: ID {id} уже занят.");
                    items.Insert(++i,new ItemStack {id=id,definition=item.definition,count=1,weaponState=new WeaponItemState {rounds=0}});
                }
            }
            return changed;
        }
    }
}
