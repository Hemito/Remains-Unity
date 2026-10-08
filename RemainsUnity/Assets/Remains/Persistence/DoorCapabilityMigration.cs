using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Loading only. All proposals are validated before source preservation and mutation.
    // This never appends/recreates a missing door or replaces a saved integrity graph.
    public static class DoorCapabilityMigration
    {
        public static bool Prepare(WorldState world,IEnumerable<DoorCapabilityLayout> layouts,Action preserveSource)
        {
            if(world==null || layouts==null || world.contentPatches==null)
                throw new InvalidOperationException("Обновление прочности дверей: нет мира, регистраций или журнала версий.");
            var proposals=layouts.Select(layout=>layout==null?null:StateJson.Copy(layout)).ToArray();
            var patches=new HashSet<string>(StringComparer.Ordinal);
            foreach(var layout in proposals)
            {
                if(layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.doors==null || layout.doors.Length==0 || !patches.Add(layout.patch))
                    throw new InvalidOperationException($"Обновление прочности дверей: неполная или повторная регистрация; world={world.worldId}, patch={layout?.patch}.");
                foreach(var initial in layout.doors)
                {
                    if(initial==null) throw new InvalidOperationException("Обновление прочности дверей: пустая исходная дверь; patch="+layout.patch);
                    initial.Validate();
                    if(initial.level!=layout.level || !initial.id.StartsWith(layout.level+":",StringComparison.Ordinal) || initial.integrity==null ||
                        initial.integrity.health!=initial.integrity.maxHealth || initial.integrity.resolvedAttacks.Count!=0 || initial.Destroyed)
                        throw new InvalidOperationException($"Обновление прочности дверей: требуется полная начальная прочность той же расстановки; id={initial.id}, patch={layout.patch}.");
                }
            }
            ContentEntityIds.Preflight(world,proposals.SelectMany(p=>p.doors).Select(d=>new ContentEntityIdentity(ContentEntityKind.Door,d.id)),"явная миграция прочности");
            var saved=world.doors.ToDictionary(d=>d.id,StringComparer.Ordinal);
            var additions=new List<string>();
            var changes=new List<(DoorState saved,DoorIntegrity integrity)>();
            foreach(var layout in proposals)
            {
                int recorded=world.contentPatches.Count(p=>p==layout.patch);
                if(recorded>1) throw new InvalidOperationException("Обновление прочности дверей: повтор сохранённой версии "+layout.patch);
                if(recorded==1) continue;
                foreach(var initial in layout.doors)
                {
                    if(!saved.TryGetValue(initial.id,out var door)) continue; // Saved absence remains absence.
                    door.Validate();
                    if(door.definition!=initial.definition || door.level!=initial.level || door.x!=initial.x || door.y!=initial.y ||
                        door.width!=initial.width || door.height!=initial.height || door.surfaceTexture!=initial.surfaceTexture)
                        throw new InvalidOperationException($"Обновление прочности дверей: сохранённая расстановка отличается; world={world.worldId}, patch={layout.patch}, id={door.id}. Неизвестные данные не заменены.");
                    if(door.integrity==null) changes.Add((door,StateJson.Copy(initial.integrity)));
                }
                additions.Add(layout.patch);
            }
            if(additions.Count==0) return false;
            if(preserveSource==null) throw new ArgumentNullException(nameof(preserveSource),"Перед обновлением дверей нужен явный callback сохранения исходного мира.");
            if(world.revision<0 || world.revision==long.MaxValue) throw new InvalidOperationException("Обновление прочности дверей: некорректный или исчерпанный счётчик мира "+world.worldId);
            // Reserve before mutation. The callback must preserve bytes only, not update this world.
            world.contentPatches.Capacity=Math.Max(world.contentPatches.Capacity,checked(world.contentPatches.Count+additions.Count));
            preserveSource();
            foreach(var change in changes) change.saved.integrity=change.integrity;
            world.contentPatches.AddRange(additions);world.revision++;
            return true;
        }
    }
}
