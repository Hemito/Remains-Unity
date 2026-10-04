using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Remains.Domain
{
    // A stack keeps its current ID. Absorbed IDs remain scoped references in the
    // owning inventory, so delayed debits/claims can address the same quantity.
    public static class InventoryStacks
    {
        public static bool Compatible(ItemStack first,ItemStack second)
        {
            if(first==null || second==null || first.weaponState!=null || second.weaponState!=null ||
                first.definition!=second.definition) return false;
            int fields=first.extensions?.Count??0;
            if(fields!=(second.extensions?.Count??0)) return false;
            if(fields==0) return true;
            return first.extensions.All(pair=>second.extensions.TryGetValue(pair.Key,out var value) && JToken.DeepEquals(pair.Value,value));
        }

        public static string ResolveId(Dictionary<string,string> aliases,string id)
        {
            if(string.IsNullOrEmpty(id)) return id;
            if(aliases==null) throw new InvalidDataException("У инвентаря отсутствует таблица идентификаторов стопок.");
            for(int step=0;step<=aliases.Count;step++)
            {
                if(!aliases.TryGetValue(id,out string next)) return id;
                if(string.IsNullOrEmpty(next)) throw new InvalidDataException("Пустая ссылка стопки: item="+id);
                id=next;
            }
            throw new InvalidDataException("Циклические ссылки стопок: item="+id);
        }
        public static ItemStack Find(ActorState actor,string id)=>actor==null?null:Find(actor.inventory,actor.inventoryAliases,id);
        public static ItemStack Find(List<ItemStack> items,Dictionary<string,string> aliases,string id)
        {
            string current=ResolveId(aliases,id);
            return items.Find(item=>item.id==current);
        }
        public static ItemStack Add(ActorState actor,ItemStack item)=>Add(actor.inventory,actor.inventoryAliases,item);
        public static ItemStack Add(ContainerState container,ItemStack item)=>Add(container.items,container.itemAliases,item);
        public static ItemStack Add(List<ItemStack> items,Dictionary<string,string> aliases,ItemStack item)
        {
            ValidateItem(item);
            if(aliases==null) throw new InvalidDataException("У инвентаря отсутствует таблица идентификаторов стопок.");
            if(items.Exists(current=>current.id==item.id))
                throw new InvalidDataException("Повторный живой ID предмета при добавлении: item="+item.id);
            var target=items.Find(current=>Compatible(current,item));
            if(target==null)
            {
                // A previously transferred whole stack can return with its ID.
                aliases.Remove(item.id);
                var added=item.Copy();items.Add(added);return added;
            }
            int total=checked(target.count+item.count); // Check before any mutation.
            aliases[item.id]=target.id;
            foreach(string old in aliases.Keys.ToArray())
                if(aliases[old]==item.id) aliases[old]=target.id;
            target.count=total;return target;
        }

        public static bool NeedsConsolidation(List<ItemStack> items)
        {
            for(int i=0;i<items.Count;i++)
                for(int j=i+1;j<items.Count;j++) if(Compatible(items[i],items[j])) return true;
            return false;
        }
        public static int Consolidate(List<ItemStack> items,Dictionary<string,string> aliases)
        {
            Validate(items,aliases);
            var merged=new List<ItemStack>();var references=new Dictionary<string,string>(aliases);
            foreach(var item in items) Add(merged,references,item);
            int removed=items.Count-merged.Count;
            // Publish only after the complete conversion has passed validation.
            Validate(merged,references);
            items.Clear();items.AddRange(merged);aliases.Clear();
            foreach(var pair in references) aliases.Add(pair.Key,pair.Value);
            return removed;
        }
        public static void Validate(List<ItemStack> items,Dictionary<string,string> aliases)
        {
            if(items==null || aliases==null) throw new InvalidDataException("Отсутствуют предметы или ссылки стопок.");
            var live=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in items)
            {
                ValidateItem(item);
                if(!live.Add(item.id)) throw new InvalidDataException("Повторный живой ID предмета: item="+item.id);
            }
            foreach(var pair in aliases)
            {
                if(string.IsNullOrEmpty(pair.Key) || live.Contains(pair.Key))
                    throw new InvalidDataException("Ссылка конфликтует с живым ID стопки: item="+pair.Key);
                ResolveId(aliases,pair.Key);
            }
        }
        private static void ValidateItem(ItemStack item)
        {
            if(item==null || string.IsNullOrEmpty(item.id) || string.IsNullOrEmpty(item.definition) || item.count<=0 ||
                item.weaponState!=null && item.count!=1)
                throw new InvalidDataException($"Некорректная стопка: item={item?.id}, definition={item?.definition}, count={item?.count}.");
        }
    }
}
