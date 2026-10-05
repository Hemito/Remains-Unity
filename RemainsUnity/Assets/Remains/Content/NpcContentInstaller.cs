using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Content
{
    public static class NpcContentInstaller
    {
        public static bool NeedsInstall(WorldState world,NpcLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static bool Apply(WorldState world,NpcLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.actors==null)
                throw new InvalidOperationException("Неполная расстановка NPC.");
            if(!NeedsInstall(world,layout)) return false;
            layout=StateJson.Copy(layout);
            var selected=ContentExpansionRules.SelectedIds(world,layout.patch,layout.level,layout.expansion,layout.actors.Select(a=>a?.id));
            if(selected!=null)
            {layout.actors=layout.actors.Where(a=>selected.Contains(a.id)).ToArray();layout.loot=layout.loot?.Where(b=>selected.Contains(b?.actor)).ToArray();}
            if(layout.loot==null) throw new InvalidOperationException("Нет списка начальных инвентарей NPC.");
            var bound=new HashSet<string>(StringComparer.Ordinal);
            foreach(var binding in layout.loot)
            {
                if(binding==null || string.IsNullOrWhiteSpace(binding.actor) || !bound.Add(binding.actor) || binding.table==null)
                    throw new InvalidOperationException("Некорректная привязка начального инвентаря NPC.");
                binding.table.Validate();
                var targets=layout.actors.Where(a=>a!=null && a.id==binding.actor).ToArray();
                if(targets.Length!=1 || targets[0].inventory==null) throw new InvalidOperationException("Нет единственного NPC таблицы лута: "+binding.actor);
                if(world.Find(binding.actor)!=null) continue;
                var target=targets[0];target.inventory.AddRange(binding.table.Generate(world.worldId,target.id));
                InventoryStacks.Consolidate(target.inventory,target.inventoryAliases);
                if(!string.IsNullOrEmpty(target.weapon))
                {
                    var weapons=target.inventory.Where(i=>i.definition==target.weapon && i.weaponState!=null).ToArray();
                    if(weapons.Length!=1) throw new InvalidOperationException("Начальное оружие NPC отсутствует или неоднозначно в инвентаре: "+target.id);
                    target.weaponInventoryVersion=1;
                    if(!WeaponInventory.Select(target,weapons[0].id,out string reason)) throw new InvalidOperationException($"Не выбрано оружие NPC {target.id}: {reason}");
                }
            }
            var ids=new HashSet<string>(StringComparer.Ordinal);var additions=new List<ActorState>();
            var itemIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var actor in layout.actors)
            {
                if(actor==null || string.IsNullOrWhiteSpace(actor.id) || !ids.Add(actor.id) || actor.level!=layout.level || actor.player || actor.connected ||
                    actor.owner!=null || actor.target!=null || actor.down || actor.attackBusy || string.IsNullOrWhiteSpace(actor.actorType) ||
                    string.IsNullOrWhiteSpace(actor.anatomy) || actor.inventory==null || !float.IsFinite(actor.x) || !float.IsFinite(actor.y) ||
                    !float.IsFinite(actor.health) || actor.health<=0 || Math.Abs(actor.facing)!=1)
                    throw new InvalidOperationException("Некорректный NPC расстановки: "+actor?.id);
                if(world.props.Exists(p=>p.id==actor.id) || world.containers.Exists(c=>c.id==actor.id) || world.doors.Exists(d=>d.id==actor.id))
                    throw new InvalidOperationException("ID NPC занят другим объектом: "+actor.id);
                var existing=world.actors.FindAll(a=>a.id==actor.id);
                if(existing.Count>1 || existing.Count==1 && (existing[0].player || existing[0].actorType!=actor.actorType || existing[0].level!=actor.level))
                    throw new InvalidOperationException("Конфликт сохранённого NPC: "+actor.id);
                if(existing.Count==0)
                {
                    foreach(var item in actor.inventory)
                        if(item==null || string.IsNullOrWhiteSpace(item.id) || !itemIds.Add(item.id) || string.IsNullOrWhiteSpace(item.definition) ||
                            item.count<=0 || item.weaponState!=null && (item.count!=1 || item.weaponState.rounds<0 || item.weaponState.revision<0) ||
                            world.actors.Any(a=>a.inventory.Any(i=>i.id==item.id)) || world.containers.Any(c=>c.items.Any(i=>i.id==item.id)))
                            throw new InvalidOperationException("Начальный предмет NPC уже существует или неверен: "+actor.id+", item="+item?.id);
                    var added=actor.Copy();InventoryStacks.Consolidate(added.inventory,added.inventoryAliases);additions.Add(added);
                }
            }
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке NPC.");
            world.actors.AddRange(additions);world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }
}
