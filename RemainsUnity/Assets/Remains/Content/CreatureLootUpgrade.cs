using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;

namespace Remains.Content
{
    // One-time repair of living generated creatures created before lootDrop tables existed.
    // Removed actors, corpses and any previously edited inventory are never reconstructed.
    public static class CreatureLootUpgrade
    {
        public const string TableVersion="source-lootDrop-v1";
        private static string Patch(NpcLayout layout)=>layout.level+":base:factory-generated:creature-loot-v1";
        public static bool NeedsApply(WorldState world,NpcLayout layout)=>world!=null && layout!=null &&
            layout.patch==layout.level+":"+FactoryCreatureAdapter.Patch &&
            layout.loot?.Any(b=>b?.table?.version==TableVersion)==true && !world.contentPatches.Contains(Patch(layout));

        public sealed class Plan
        {
            private readonly string patch,worldId,level;
            private readonly Entry[] entries;
            private sealed class Entry
            {public string actor,type;public ItemStack[] items;}
            private Plan(string patch,string worldId,string level,Entry[] entries)
            {this.patch=patch;this.worldId=worldId;this.level=level;this.entries=entries;}
            public bool Apply(WorldState world)
            {
                if(world==null || world.worldId!=worldId) throw new InvalidOperationException("Мир обновления добычи существ изменился.");
                if(world.contentPatches.Contains(patch)) return false;
                var targets=Validate(world);
                foreach(var target in targets)
                {
                    if(target.entry.items.Length==0) continue;
                    target.actor.inventory.AddRange(target.entry.items.Select(i=>i.Copy()));
                    InventoryStacks.Consolidate(target.actor.inventory,target.actor.inventoryAliases);
                    target.actor.revision++;WorldChanges.Mark(world,target.actor);
                }
                world.contentPatches.Add(patch);world.revision++;
                return true;
            }
            private List<(ActorState actor,Entry entry)> Validate(WorldState world)
            {
                var targets=new List<(ActorState actor,Entry entry)>();
                var ids=ItemIds(world);
                foreach(var entry in entries)
                {
                    var matches=world.actors.Where(a=>a.id==entry.actor).ToArray();
                    if(matches.Length!=1 || !Eligible(matches[0],entry.type,level))
                        throw new InvalidOperationException("Изменилось живое существо обновления добычи: "+entry.actor);
                    if(matches[0].revision==long.MaxValue) throw new InvalidOperationException("Исчерпана ревизия существа: "+entry.actor);
                    foreach(var item in entry.items)
                        if(!ids.Add(item.id)) throw new InvalidOperationException("Предмет обновления добычи уже существует: "+item.id);
                    targets.Add((matches[0],entry));
                }
                if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпана ревизия мира при обновлении добычи существ.");
                return targets;
            }
            internal static Plan Create(WorldState world,NpcLayout layout)
            {
                var entries=new List<Entry>();var bindings=new HashSet<string>(StringComparer.Ordinal);
                foreach(var binding in layout.loot.Where(b=>b?.table?.version==TableVersion))
                {
                    binding.table.Validate();
                    if(!bindings.Add(binding.actor)) throw new InvalidOperationException("Повтор привязки обновления добычи: "+binding.actor);
                    var authored=layout.actors.Where(a=>a.id==binding.actor).ToArray();
                    if(authored.Length!=1) throw new InvalidOperationException("Нет единственного исходного существа обновления добычи: "+binding.actor);
                    var existing=world.actors.Where(a=>a.id==binding.actor).ToArray();
                    if(existing.Length>1) throw new InvalidOperationException("Повтор сохранённого существа: "+binding.actor);
                    if(existing.Length==0 || !Eligible(existing[0],authored[0].actorType,layout.level)) continue;
                    entries.Add(new Entry {actor=binding.actor,type=authored[0].actorType,
                        items=binding.table.Generate(world.worldId,binding.actor).ToArray()});
                }
                var result=new Plan(Patch(layout),world.worldId,layout.level,entries.ToArray());
                // Validate collisions and all targets before the caller publishes any content.
                result.Validate(world);
                return result;
            }
        }
        public static Plan Prepare(WorldState world,NpcLayout layout)=>NeedsApply(world,layout)?Plan.Create(world,layout):null;
        private static bool Eligible(ActorState actor,string type,string level)=>actor!=null && !actor.player && !actor.down &&
            actor.health>0 && actor.actorType==type && actor.level==level && string.IsNullOrEmpty(actor.weapon) &&
            actor.inventory?.Count==0 && actor.inventoryAliases?.Count==0;
        private static HashSet<string> ItemIds(WorldState world)=>new(world.actors.SelectMany(a=>a.inventory).Concat(world.containers.SelectMany(c=>c.items))
            .Select(i=>i.id),StringComparer.Ordinal);
    }
}
