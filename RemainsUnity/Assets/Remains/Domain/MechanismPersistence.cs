using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    public static class MechanismPersistence
    {
        public static void ValidateWorld(WorldState world,Func<string,MovingPlatformDefinition> resolveDefinition,
            Func<string,bool> supportedRepairState=null)
        {
            if(resolveDefinition==null) throw new ArgumentNullException(nameof(resolveDefinition));
            ValidateWorld(world,(id,_)=>resolveDefinition(id),supportedRepairState);
        }
        // Without a resolver this validates saved structure, not installed content availability.
        public static void ValidateWorld(WorldState world,Func<string,int,MovingPlatformDefinition> resolveDefinition=null,
            Func<string,bool> supportedRepairState=null)
        {
            if(world==null || world.movingPlatforms==null || world.mechanismButtons==null || world.actors==null || world.props==null ||
                world.containers==null || world.doors==null || world.projectiles==null || world.grenades==null ||
                world.completedTransactions==null || world.pendingGrants==null || world.pendingDeposits==null || world.mechanismVersion is not (0 or 1) ||
                world.mechanismVersion==0 && (world.movingPlatforms.Count!=0 || world.mechanismButtons.Count!=0))
                throw new InvalidOperationException($"Не поддерживается состояние механизмов: world={world?.worldId}, version={world?.mechanismVersion}.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            var platforms=new Dictionary<string,MovingPlatformState>(StringComparer.Ordinal);
            foreach(var platform in world.movingPlatforms)
            {
                if(platform==null) throw new InvalidOperationException("Пустая запись платформы мира "+world.worldId);
                platform.Validate();
                if(platform.segment!=null && platform.segment.sequence>platform.revision)
                    throw new InvalidOperationException($"Последовательность segment превышает сохранённую revision: platform={platform.id}, sequence={platform.segment.sequence}, revision={platform.revision}.");
                Unique(platform.id);
                platforms.Add(platform.id,platform);
                if(resolveDefinition!=null)
                {
                    var type=resolveDefinition(platform.definition,platform.definitionVersion);
                    if(type==null) throw new InvalidOperationException($"Нет определения платформы {platform.definition} v{platform.definitionVersion}: world={world.worldId}, platform={platform.id}.");
                    platform.Validate(type);
                }
            }
            foreach(var button in world.mechanismButtons)
            {
                if(button==null) throw new InvalidOperationException("Пустая запись кнопки мира "+world.worldId);
                button.Validate(supportedRepairState??MechanismRepairStates.BuiltIn);Unique(button.id);
                if(!platforms.TryGetValue(button.platformId,out var platform))
                    throw new InvalidOperationException($"Нет платформы связи: button={button.id}, platform={button.platformId}, world={world.worldId}.");
                button.ValidateLink(platform,supportedRepairState??MechanismRepairStates.BuiltIn);
            }
            // Mechanism IDs must also not alias another canonical entity kind.
            foreach(var actor in world.actors) Conflict(actor?.id);
            foreach(var prop in world.props) Conflict(prop?.id);
            foreach(var container in world.containers) Conflict(container?.id);
            foreach(var door in world.doors) Conflict(door?.id);
            foreach(var projectile in world.projectiles) Conflict(projectile?.launch?.id);
            foreach(var grenade in world.grenades) Conflict(grenade?.id);
            void Unique(string id)
            {if(!ids.Add(id)) throw new InvalidOperationException($"Повтор ID механизма: world={world.worldId}, id={id}.");}
            void Conflict(string id)
            {if(id!=null && ids.Contains(id)) throw new InvalidOperationException($"ID механизма занят другим видом сущности: world={world.worldId}, id={id}.");}
        }

        // New games pass an explicit no-op callback; existing saves preserve their real source.
        public static bool Prepare(WorldState world,Action preserveSource)
        {
            ValidateWorld(world);
            if(world.mechanismVersion==1) return false;
            if(preserveSource==null) throw new ArgumentNullException(nameof(preserveSource),"Перед миграцией механизмов нужен явный callback сохранения источника.");
            if(world.revision<0 || world.revision==long.MaxValue) throw new InvalidOperationException("Некорректный/исчерпанный счётчик мира при миграции механизмов: "+world.worldId);
            preserveSource();
            world.mechanismVersion=1;world.revision++;
            return true;
        }
    }
}
