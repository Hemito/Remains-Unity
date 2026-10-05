using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Explicit loading/authoring operation. A missing record is never created by a time read.
    public static class LevelTimeMigration
    {
        public static void ValidateWorld(WorldState world)
        {
            if(world==null || world.levelTimes==null || world.levelTimeVersion is not (0 or 1) ||
                world.levelTimeVersion==0 && world.levelTimes.Count!=0)
                throw new InvalidOperationException($"Не поддерживается набор часов мира: world={world?.worldId}, version={world?.levelTimeVersion}, records={world?.levelTimes?.Count}.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in world.levelTimes)
            {
                if(record==null) throw new InvalidOperationException("Пустая запись часов мира "+world.worldId);
                record.Validate();
                if(!ids.Add(record.level)) throw new InvalidOperationException("Повтор часов уровня: world="+world.worldId+", level="+record.level);
            }
        }

        public static bool Prepare(WorldState world,IEnumerable<string> levels,Action preserveSource=null)
        {
            ValidateWorld(world);
            if(levels==null) throw new ArgumentNullException(nameof(levels));
            var existing=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in world.levelTimes) existing.Add(record.level);
            var additions=new List<LevelSimulationTime>();
            foreach(string level in levels)
            {
                if(string.IsNullOrWhiteSpace(level)) throw new InvalidOperationException("Не указан ID уровня для часов мира "+world.worldId);
                if(existing.Add(level)) additions.Add(new LevelSimulationTime {level=level});
            }
            if(world.levelTimeVersion==1 && additions.Count==0) return false;
            if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан счётчик мира при установке часов: "+world.worldId);
            // Finish all validation and preserve the source before changing live state.
            preserveSource?.Invoke();
            world.levelTimes.AddRange(additions);world.levelTimeVersion=1;world.revision++;
            return true;
        }
    }
}
