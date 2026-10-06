using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    public static class ContainerAppearancePersistence
    {
        public static void ValidateWorld(WorldState world)
        {
            if(world?.containers==null) throw new InvalidOperationException("Нет списка контейнеров для проверки вида мира.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var container in world.containers)
            {
                if(container==null || string.IsNullOrWhiteSpace(container.id) || !ids.Add(container.id) || container.appearance==null)
                    throw new InvalidOperationException($"Неполное/повторное состояние вида контейнера: world={world.worldId}, id={container?.id}.");
                container.appearance.Validate(container.id);
            }
        }
        // The complete preflight precedes BOTH the backup callback and any conversion.
        public static bool Prepare(WorldState world,Action preserveSource)
        {
            ValidateWorld(world);
            bool legacy=world.containers.Exists(container=>container.appearance.version==0);
            if(!legacy) return false;
            if(preserveSource==null) throw new ArgumentNullException(nameof(preserveSource),"Перед миграцией вида контейнеров нужен явный callback сохранения источника.");
            if(world.revision<0 || world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпан/невалиден счётчик мира при миграции вида контейнеров.");
            preserveSource();
            foreach(var container in world.containers)
                if(container.appearance.version==0) container.appearance.version=1;
            world.revision++;
            return true;
        }
        // A detached newly installed object is not a migration of an existing save.
        public static void InitializeNew(ContainerState container)
        {
            if(container?.appearance==null) throw new InvalidOperationException("Нет состояния вида нового контейнера: "+container?.id);
            container.appearance.Validate(container.id);
            container.appearance.version=1;
        }
    }
}
