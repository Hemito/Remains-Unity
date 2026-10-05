using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Content
{
    public static class PhysicalContentInstaller
    {
        public static bool NeedsInstall(WorldState world,PhysicalLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static bool Apply(WorldState world,PhysicalLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.props==null || layout.containers==null)
                throw new InvalidOperationException("Неполная расстановка физических объектов.");
            if(!NeedsInstall(world,layout)) return false;
            // Work on a detached description; no generated items leak into the authored manifest.
            layout=StateJson.Copy(layout);
            if(layout.expansion!=null)
            {
                var expansion=layout.expansion;
                var known=layout.props.Cast<PropState>().Concat(layout.containers).Select(p=>p?.id).ToHashSet(StringComparer.Ordinal);
                if(layout.props.Any(p=>p==null) || layout.containers.Any(c=>c==null) ||
                    string.IsNullOrWhiteSpace(expansion.previousPatch) || !expansion.previousPatch.StartsWith(layout.level+":",StringComparison.Ordinal) ||
                    expansion.previousPatch==layout.patch || expansion.additions==null ||
                    expansion.additions.Length==0 || expansion.additions.Any(id=>string.IsNullOrWhiteSpace(id) || !known.Contains(id)) ||
                    expansion.additions.Distinct(StringComparer.Ordinal).Count()!=expansion.additions.Length)
                    throw new InvalidOperationException("Некорректное добавочное обновление расстановки: "+layout.patch);
                if(world.contentPatches.Contains(expansion.previousPatch))
                {
                    var additions=expansion.additions.ToHashSet(StringComparer.Ordinal);
                    layout.props=layout.props.Where(p=>additions.Contains(p.id)).ToArray();
                    layout.containers=layout.containers.Where(c=>additions.Contains(c.id)).ToArray();
                    layout.loot=layout.loot?.Where(b=>additions.Contains(b?.container)).ToArray();
                }
            }
            var bound=new HashSet<string>(StringComparer.Ordinal);
            if(layout.loot==null) throw new InvalidOperationException("Нет списка таблиц лута расстановки.");
            foreach(var binding in layout.loot)
            {
                if(binding==null || string.IsNullOrWhiteSpace(binding.container) || !bound.Add(binding.container) || binding.table==null)
                    throw new InvalidOperationException("Некорректная привязка таблицы лута.");
                binding.table.Validate();
                var targets=layout.containers.Where(c=>c!=null && c.id==binding.container).ToArray();
                if(targets.Length!=1 || targets[0].items==null) throw new InvalidOperationException("Нет единственного контейнера таблицы лута: "+binding.container);
                if(!world.containers.Exists(c=>c.id==binding.container))
                    targets[0].items.AddRange(binding.table.Generate(world.worldId,binding.container));
            }
            var ids=new HashSet<string>(StringComparer.Ordinal);var itemIds=new HashSet<string>(StringComparer.Ordinal);
            var props=new List<PropState>();var containers=new List<ContainerState>();
            foreach(var entry in layout.props.Cast<PropState>().Concat(layout.containers))
            {
                if(entry==null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id) || entry.level!=layout.level ||
                    string.IsNullOrWhiteSpace(entry.definition) || !float.IsFinite(entry.x) || !float.IsFinite(entry.y) ||
                    !float.IsFinite(entry.mass) || entry.mass<=0 || !float.IsFinite(entry.width) || entry.width<=0 ||
                    !float.IsFinite(entry.height) || entry.height<=0 || entry.anchorHeight!=0 || entry.held || entry.owner!=null)
                    throw new InvalidOperationException("Некорректный физический объект расстановки: "+entry?.id);
                bool container=entry is ContainerState;
                if(!container && entry.GetType()!=typeof(PropState)) throw new InvalidOperationException("Неизвестный тип расстановки: "+entry.id);
                if(world.actors.Exists(a=>a.id==entry.id) || world.doors.Exists(d=>d.id==entry.id) ||
                    (container?world.props.Exists(p=>p.id==entry.id):world.containers.Exists(c=>c.id==entry.id)))
                    throw new InvalidOperationException("ID расстановки занят другим типом объекта: "+entry.id);
                if(container)
                {
                    var source=(ContainerState)entry;
                    if(source.items==null) throw new InvalidOperationException("Нет списка содержимого: "+entry.id);
                    foreach(var item in source.items)
                        if(item==null || string.IsNullOrWhiteSpace(item.id) || !itemIds.Add(item.id) || string.IsNullOrWhiteSpace(item.definition) ||
                            item.count<=0 || item.weaponState!=null && item.count!=1)
                            throw new InvalidOperationException("Некорректный начальный предмет: container="+entry.id+", item="+item?.id);
                }
                var saved=(container?world.containers.Cast<PropState>():world.props).Where(p=>p.id==entry.id).ToArray();
                if(saved.Length>1 || saved.Length==1 && (saved[0].definition!=entry.definition || saved[0].level!=entry.level))
                    throw new InvalidOperationException("Конфликт сохранённого физического объекта: "+entry.id);
                if(saved.Length==1) continue; // Keep moved position, held state, inventory and extensions.
                if(container)
                    foreach(var item in ((ContainerState)entry).items)
                        if(world.containers.Any(c=>c.items.Any(i=>i.id==item.id)) || world.actors.Any(a=>a.inventory.Any(i=>i.id==item.id)))
                            throw new InvalidOperationException("Начальный предмет уже существует в мире: "+item.id);
                if(container)
                {
                    var source=(ContainerState)entry;
                    InventoryStacks.Consolidate(source.items,source.itemAliases);
                    containers.Add(StateJson.Copy(source));
                }
                else props.Add(StateJson.Copy(entry));
            }
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке объектов.");
            world.props.AddRange(props);world.containers.AddRange(containers);world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }
}
