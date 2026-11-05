using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;

namespace Remains.Domain
{
    public readonly struct DebugItem
    {
        public readonly string id,category;
        public DebugItem(string id,string category) {this.id=id;this.category=category;}
    }

    // The hidden item menu (user 2026-11-03, PT-20261103-26: instead of giving new characters everything): every item the
    // game knows, by its inventory definition, as the loot would create it. A weapon comes with a full magazine; items the
    // game does not act on yet are listed apart. The host builds the same list to check a request.
    public static class DebugItems
    {
        public const string Weapons="Оружие",Grenades="Гранаты",Ammo="Патроны",Wear="Снаряжение",Other="Прочее",Formal="Не подключено";
        public static readonly string[] Categories={Weapons,Grenades,Ammo,Wear,Other,Formal};
        public const int MaxCount=99;
        private const string GradeRole="item-image-grade-";
        private static readonly string[] HandMade={"heal","grenade","spear","crowbar","pistol","energy"};

        public static List<DebugItem> Build()
        {
            var result=new List<DebugItem>();var seen=new HashSet<string>(StringComparer.Ordinal);
            var ammunition=new HashSet<string>(WeaponCatalog.All.Where(w=>!string.IsNullOrEmpty(w.ammo)).Select(w=>w.ammo),StringComparer.Ordinal);
            var cosmetics=new HashSet<string>(CosmeticCatalog.Current.Definitions.Select(d=>d.Id),StringComparer.Ordinal);
            void Add(string id)
            {
                if(!seen.Add(id)) return;
                string category=WeaponCatalog.IsGrenade(id)?Grenades:WeaponCatalog.Contains(id)?Weapons:ammunition.Contains(id)?Ammo:
                    cosmetics.Contains(id)?Wear:id.StartsWith(ExportedWeaponItems.Prefix,StringComparison.Ordinal)?Formal:Other;
                result.Add(new DebugItem(id,category));
            }
            foreach(string id in HandMade) Add(id);
            var exported=ExportedContentCatalog.Load();
            foreach(var entry in exported.items.OrderBy(e=>e.key,StringComparer.Ordinal))
            {
                if(entry.category=="weapon")
                {
                    // Thrown weapons live in the inventory as items (item:<id>); creature weapons have no item image.
                    if(entry.sourceId=="grenade" || WeaponCatalog.IsGrenade("item:"+entry.sourceId)) continue;
                    foreach(var binding in entry.graphics??Array.Empty<ExportedBinding>())
                        if(binding.role!=null && binding.role.StartsWith(GradeRole,StringComparison.Ordinal) && int.TryParse(binding.role.Substring(GradeRole.Length),out int grade))
                            Add(OriginalWeaponIds.Runtime(entry.sourceId,grade));
                }
                else if(entry.category=="item")
                {
                    if(entry.sourceId=="grenade") continue;
                    // As the factory's loot: working ammunition under its plain ID, every other item under its formal one.
                    Add(ammunition.Contains(entry.sourceId)?entry.sourceId:entry.key);
                }
            }
            foreach(var definition in CosmeticCatalog.Current.Definitions) Add(definition.Id);
            return result;
        }

        // A stack of a listed item; a weapon is always one, with a full magazine.
        public static ItemStack Create(string id,int count)
        {
            if(count<1 || count>MaxCount) throw new ArgumentOutOfRangeException(nameof(count),$"Выдача {id}: количество {count}.");
            if(WeaponCatalog.Contains(id) && !WeaponCatalog.IsGrenade(id))
            {
                var item=SharedInventory.Create(id,1);
                item.weaponState=new WeaponItemState {rounds=WeaponCatalog.Get(id).magazineSize};
                return item;
            }
            return SharedInventory.Create(id,id.StartsWith(ExportedWeaponItems.Prefix,StringComparison.Ordinal)?1:count);
        }
        // Host: what a request adds to the actor; null for an unlisted item.
        public static ItemStack Grant(ActorState actor,string id,int count)
        {
            if(actor==null || string.IsNullOrWhiteSpace(id) || count<1 || count>MaxCount || !Build().Exists(item=>item.id==id)) return null;
            var stack=Create(id,count);
            if(stack.weaponState!=null) {actor.inventory.Add(stack);actor.revision++;return stack;}
            var added=InventoryStacks.Add(actor,stack);actor.revision++;
            return added;
        }
    }

    // A word typed key by key: a wrong key starts it over (or counts as its first letter).
    public sealed class SecretWord
    {
        private readonly string word;private int next;
        public SecretWord(string word) {this.word=word;}
        public void Reset()=>next=0;
        public bool Feed(char letter)
        {
            next=letter==word[next]?next+1:letter==word[0]?1:0;
            if(next<word.Length) return false;
            next=0;return true;
        }
    }
}
