using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Content
{
    public static class PhysicalContentInstaller
    {
        public static bool Apply(WorldState world,PhysicalLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.props==null || layout.containers==null)
                throw new InvalidOperationException("Неполная расстановка физических объектов.");
            if(world.contentPatches.Contains(layout.patch)) return false;
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
                if(container) containers.Add(StateJson.Copy((ContainerState)entry));
                else props.Add(StateJson.Copy(entry));
            }
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке объектов.");
            world.props.AddRange(props);world.containers.AddRange(containers);world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }
}
