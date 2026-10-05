using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Content
{
    public static class DoorContentInstaller
    {
        public static bool NeedsInstall(WorldState world,DoorLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static bool Apply(WorldState world,DoorLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.doors==null)
                throw new InvalidOperationException("Неполная расстановка дверей.");
            if(!NeedsInstall(world,layout)) return false;
            layout=StateJson.Copy(layout);
            var selected=ContentExpansionRules.SelectedIds(world,layout.patch,layout.level,layout.expansion,layout.doors.Select(d=>d?.id));
            if(selected!=null) layout.doors=layout.doors.Where(d=>selected.Contains(d.id)).ToArray();
            var ids=new HashSet<string>(StringComparer.Ordinal);var additions=new List<DoorState>();
            foreach(var door in layout.doors)
            {
                if(door==null) throw new InvalidOperationException("Пустая дверь в расстановке "+layout.patch);
                door.Validate();
                if(door.level!=layout.level || !ids.Add(door.id)) throw new InvalidOperationException("Неверный уровень или повтор ID двери: "+door.id);
                if(world.actors.Exists(a=>a.id==door.id) || world.props.Exists(p=>p.id==door.id) || world.containers.Exists(c=>c.id==door.id))
                    throw new InvalidOperationException("ID двери занят другим объектом: "+door.id);
                var existing=world.doors.FindAll(d=>d.id==door.id);
                if(existing.Count>1) throw new InvalidOperationException("Повтор двери в сохранении: "+door.id);
                if(existing.Count==1)
                {
                    var saved=existing[0];saved.Validate();
                    if(saved.definition!=door.definition || saved.level!=door.level || saved.x!=door.x || saved.y!=door.y ||
                        saved.width!=door.width || saved.height!=door.height || saved.surfaceTexture!=door.surfaceTexture)
                        throw new InvalidOperationException("Изменена расстановка сохранённой двери; нужна явная миграция: "+door.id);
                    continue; // Preserve open, revision and mod-owned data.
                }
                additions.Add(StateJson.FromJson<DoorState>(StateJson.ToJson(door)));
            }
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке дверей.");
            world.doors.AddRange(additions);world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }
}
