using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Content
{
    // Explicit content publication. Schema migration/backup belongs to MechanismPersistence.
    public static class MechanismContentInstaller
    {
        public static bool NeedsInstall(WorldState world,MechanismLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static void ValidateLayout(MechanismLayout layout)
        {
            if(layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) ||
                !layout.patch.StartsWith(layout.level+":",StringComparison.Ordinal) || layout.movingPlatforms==null || layout.mechanismButtons==null ||
                layout.platformDefinitions==null || layout.buttonDefinitions==null)
                throw new InvalidOperationException("Неполная расстановка механизмов: "+layout?.patch);
            var definitionIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var type in layout.platformDefinitions)
            {
                if(type==null) throw new InvalidOperationException("Нет определения платформы: "+layout.patch);
                type.Validate();if(!definitionIds.Add(type.id)) throw new InvalidOperationException("Повтор определения механизма: "+type.id);
            }
            foreach(var type in layout.buttonDefinitions)
            {
                if(type==null) throw new InvalidOperationException("Нет определения кнопки: "+layout.patch);
                type.Validate();if(!definitionIds.Add(type.id)) throw new InvalidOperationException("Повтор определения механизма: "+type.id);
            }
            var types=layout.platformDefinitions.ToDictionary(d=>d.id,StringComparer.Ordinal);
            var controls=layout.buttonDefinitions.ToDictionary(d=>d.id,StringComparer.Ordinal);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var platform in layout.movingPlatforms)
            {
                if(platform==null || !ids.Add(platform.id) || platform.level!=layout.level || platform.removed || platform.segment!=null ||
                    platform.revision!=0 || platform.endpoint!=0 || !types.TryGetValue(platform.definition,out var type))
                    throw new InvalidOperationException("Некорректная начальная платформа: patch="+layout.patch+", id="+platform?.id);
                platform.Validate(type);
            }
            var platforms=layout.movingPlatforms.ToDictionary(p=>p.id,StringComparer.Ordinal);
            foreach(var button in layout.mechanismButtons)
            {
                if(button==null || !ids.Add(button.id) || button.level!=layout.level || button.removed || button.pressed ||
                    button.revision!=0 || !controls.ContainsKey(button.definition) || !platforms.TryGetValue(button.platformId,out var platform))
                    throw new InvalidOperationException("Некорректная начальная кнопка: patch="+layout.patch+", id="+button?.id);
                button.ValidateLink(platform);
            }
        }
        public static bool Apply(WorldState world,MechanismLayout layout)=>Apply(world,layout,MechanismRepairStates.BuiltIn);
        public static bool Apply(WorldState world,MechanismLayout layout,Func<string,bool> supportedRepairState)
        {
            if(world==null || supportedRepairState==null) throw new ArgumentNullException(world==null?nameof(world):nameof(supportedRepairState));
            ValidateLayout(layout);
            if(world.mechanismVersion!=1 || world.movingPlatforms==null || world.mechanismButtons==null)
                throw new InvalidOperationException("Установка механизмов требует явной миграции мира: world="+world.worldId+", version="+world.mechanismVersion);
            if(!NeedsInstall(world,layout)) return false;
            var selected=ContentExpansionRules.SelectedIds(world,layout.patch,layout.level,layout.expansion,
                layout.movingPlatforms.Select(p=>p.id).Concat(layout.mechanismButtons.Select(b=>b.id)));
            var foreignIds=world.actors.Select(a=>a.id).Concat(world.doors.Select(d=>d.id)).Concat(world.props.Select(p=>p.id))
                .Concat(world.containers.Select(c=>c.id)).ToHashSet(StringComparer.Ordinal);
            var savedIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var platform in world.movingPlatforms)
            {
                if(platform==null || !savedIds.Add(platform.id)) throw new InvalidOperationException("Пустая/повторная сохранённая платформа: "+world.worldId);
                platform.Validate();
            }
            foreach(var button in world.mechanismButtons)
            {
                if(button==null || !savedIds.Add(button.id)) throw new InvalidOperationException("Пустая/повторная сохранённая кнопка: "+world.worldId);
                button.Validate(supportedRepairState);
            }
            var additions=new List<MovingPlatformState>();
            var controls=new List<MechanismButtonState>();
            foreach(var initial in layout.movingPlatforms.Where(p=>selected==null || selected.Contains(p.id)))
            {
                if(foreignIds.Contains(initial.id) || world.mechanismButtons.Any(b=>b.id==initial.id))
                    throw new InvalidOperationException("ID платформы занят другим объектом: "+initial.id);
                var saved=world.movingPlatforms.SingleOrDefault(p=>p.id==initial.id);
                if(saved!=null)
                {
                    if(saved.definition!=initial.definition || saved.definitionVersion!=initial.definitionVersion ||
                        saved.level!=initial.level || saved.roomId!=initial.roomId || saved.groupId!=initial.groupId)
                        throw new InvalidOperationException("Конфликт сохранённой платформы; нужна явная миграция: "+initial.id);
                    saved.Validate(layout.platformDefinitions.Single(d=>d.id==initial.definition));
                    continue; // Preserve removed, endpoint, segment, revision, routes and extension fields byte-for-byte.
                }
                additions.Add(StateJson.Copy(initial));
            }
            foreach(var initial in layout.mechanismButtons.Where(b=>selected==null || selected.Contains(b.id)))
            {
                if(foreignIds.Contains(initial.id) || world.movingPlatforms.Any(p=>p.id==initial.id))
                    throw new InvalidOperationException("ID кнопки занят другим объектом: "+initial.id);
                var platform=world.movingPlatforms.Concat(additions).SingleOrDefault(p=>p.id==initial.platformId);
                initial.ValidateLink(platform);
                var saved=world.mechanismButtons.SingleOrDefault(b=>b.id==initial.id);
                if(saved!=null)
                {
                    if(saved.definition!=initial.definition || saved.level!=initial.level || saved.roomId!=initial.roomId ||
                        saved.groupId!=initial.groupId || saved.platformId!=initial.platformId)
                        throw new InvalidOperationException("Конфликт сохранённой кнопки; нужна явная миграция: "+initial.id);
                    saved.ValidateLink(platform,supportedRepairState);
                    continue; // No repair reset, no re-press, no recreation of removed records.
                }
                controls.Add(StateJson.Copy(initial));
            }
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке механизмов: "+world.worldId);
            world.movingPlatforms.AddRange(additions);world.mechanismButtons.AddRange(controls);world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }
}
