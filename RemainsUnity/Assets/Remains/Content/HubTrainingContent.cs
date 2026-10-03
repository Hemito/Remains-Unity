using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class TrainingPlacement {public string id,type;public float x,y;public int facing;}
    [Serializable] public sealed class TrainingLayout {public string patch,level;public TrainingPlacement[] actors,containers;}
    public static class HubTrainingContent
    {
        public const string Patch="base:hub-training-3";
        public static bool NeedsInstall(WorldState world)=>!world.contentPatches.Contains(Patch);
        public static void Install(WorldState world)
        {
            if(!NeedsInstall(world)) return;
            var asset=Resources.Load<TextAsset>("Content/hub-training");
            if(asset==null) throw new InvalidOperationException("Нет расстановки Content/hub-training.");
            var layout=JsonUtility.FromJson<TrainingLayout>(asset.text);
            if(layout==null || layout.patch!=Patch || layout.level!="hub" || layout.actors==null || layout.containers==null)
                throw new InvalidOperationException("Некорректная расстановка мишеней хаба.");
            var ids=new HashSet<string>();var additions=new List<ActorState>();
            var relocations=new List<(ActorState actor,TrainingPlacement placement)>();
            foreach(var entry in layout.actors)
            {
                if(string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) || !float.IsFinite(entry.x) || !float.IsFinite(entry.y) || Math.Abs(entry.facing)!=1)
                    throw new InvalidOperationException("Некорректное размещение мишени: "+entry.id);
                var definition=ActorTypes.Get(entry.type);
                if(world.containers.Exists(c=>c.id==entry.id)) throw new InvalidOperationException("ID мишени занят контейнером: "+entry.id);
                if(definition.restoreHealthAfterHit<=0 || definition.combatAi) throw new InvalidOperationException("Неучебный тип мишени: "+entry.type);
                var existing=world.Find(entry.id);
                if(existing!=null)
                {
                    if(existing.actorType!=entry.type || existing.level!=layout.level) throw new InvalidOperationException("Конфликт ID мишени с существующим актёром: "+entry.id);
                    relocations.Add((existing,entry));
                    continue;
                }
                additions.Add(new ActorState {id=entry.id,actorType=entry.type,level=layout.level,x=entry.x,y=entry.y,
                    facing=entry.facing,health=definition.restoreHealthAfterHit,weapon=null});
            }
            var containers=new List<ContainerState>();
            foreach(var entry in layout.containers)
            {
                if(string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) || !float.IsFinite(entry.x) || !float.IsFinite(entry.y))
                    throw new InvalidOperationException("Некорректное размещение ящика: "+entry.id);
                var definition=PropCatalog.Get(entry.type);var existing=world.containers.Find(c=>c.id==entry.id);
                if(world.Find(entry.id)!=null) throw new InvalidOperationException("ID ящика занят актёром: "+entry.id);
                if(existing!=null)
                {
                    if(existing.definition!=entry.type || existing.level!=layout.level) throw new InvalidOperationException("Конфликт ID тренировочного ящика: "+entry.id);
                    continue;
                }
                containers.Add(new ContainerState {id=entry.id,definition=entry.type,level=layout.level,x=entry.x,y=entry.y,
                    mass=definition.mass,width=definition.width,height=definition.height,anchorHeight=0});
            }
            foreach(var move in relocations)
            {move.actor.x=move.placement.x;move.actor.y=move.placement.y;move.actor.facing=move.placement.facing;move.actor.vx=move.actor.vy=0;}
            world.actors.AddRange(additions);world.containers.AddRange(containers);world.contentPatches.Add(Patch);world.revision++;
            Debug.Log($"Добавлена тренировочная часть хаба: world={world.worldId}, actors={additions.Count}, patch={Patch}.");
        }
    }
}
