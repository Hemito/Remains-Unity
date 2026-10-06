using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Apply before simulation starts. Validate the entire patch before changing the world.
    public static class TrainingContentInstaller
    {
        public static void Apply(WorldState world,TrainingLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.actors==null || layout.containers==null)
                throw new InvalidOperationException("Неполная расстановка учебных объектов.");
            if(world.contentPatches.Contains(layout.patch)) return;
            var ids=new HashSet<string>();var additions=new List<ActorState>();
            var relocations=new List<(ActorState actor,TrainingPlacement placement)>();
            foreach(var entry in layout.actors)
            {
                if(entry==null || string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) || !float.IsFinite(entry.x) || !float.IsFinite(entry.y) || Math.Abs(entry.facing)!=1)
                    throw new InvalidOperationException("Некорректное размещение мишени: "+entry?.id);
                var definition=ActorTypes.Get(entry.type);
                if(world.containers.Exists(c=>c.id==entry.id) || world.props.Exists(p=>p.id==entry.id)) throw new InvalidOperationException("ID мишени занят контейнером: "+entry?.id);
                if(definition.restoreHealthAfterHit<=0 || definition.combatAi) throw new InvalidOperationException("Неучебный тип мишени: "+entry.type);
                var existing=world.Find(entry.id);
                if(existing!=null)
                {
                    if(existing.actorType!=entry.type || existing.level!=layout.level) throw new InvalidOperationException("Конфликт ID мишени с существующим актёром: "+entry?.id);
                    relocations.Add((existing,entry));
                    continue;
                }
                additions.Add(new ActorState {id=entry.id,actorType=entry.type,level=layout.level,x=entry.x,y=entry.y,
                    facing=entry.facing,health=definition.restoreHealthAfterHit,weapon=null});
            }
            var containers=new List<ContainerState>();
            var containerMoves=new List<(ContainerState container,TrainingPlacement placement)>();
            foreach(var entry in layout.containers)
            {
                if(entry==null || string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) || !float.IsFinite(entry.x) || !float.IsFinite(entry.y))
                    throw new InvalidOperationException("Некорректное размещение ящика: "+entry?.id);
                var definition=PropCatalog.Get(entry.type);var existing=world.containers.Find(c=>c.id==entry.id);
                if(world.Find(entry.id)!=null || world.props.Exists(p=>p.id==entry.id)) throw new InvalidOperationException("ID ящика занят актёром: "+entry?.id);
                if(existing!=null)
                {
                    if(existing.definition!=entry.type || existing.level!=layout.level) throw new InvalidOperationException("Конфликт ID тренировочного ящика: "+entry?.id);
                    if(entry.relocateExisting)
                    {
                        if(existing.held) throw new InvalidOperationException("Нельзя переносить удерживаемый контейнер: "+entry.id);
                        containerMoves.Add((existing,entry));
                    }
                    continue;
                }
                var container=new ContainerState {id=entry.id,definition=entry.type,level=layout.level,x=entry.x,y=entry.y,
                    mass=definition.mass,width=definition.width,height=definition.height,anchorHeight=0,
                    movable=definition.movable,hasPhysicalBody=definition.hasPhysicalBody};
                PropBodyRules.Validate(container);ContainerAppearancePersistence.InitializeNew(container);containers.Add(container);
            }
            foreach(var move in relocations)
            {move.actor.x=move.placement.x;move.actor.y=move.placement.y;move.actor.facing=move.placement.facing;move.actor.vx=move.actor.vy=0;}
            foreach(var move in containerMoves)
            {move.container.x=move.placement.x;move.container.y=move.placement.y;move.container.vx=move.container.vy=0;}
            world.actors.AddRange(additions);world.containers.AddRange(containers);world.contentPatches.Add(layout.patch);world.revision++;
            Debug.Log($"Добавлена тренировочная часть хаба: world={world.worldId}, actors={additions.Count}, patch={layout.patch}.");
        }
    }
}
