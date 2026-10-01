using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class TrainingPlacement {public string id,type;public float x,y;public int facing;}
    [Serializable] public sealed class TrainingLayout {public string patch,level;public TrainingPlacement[] actors;}
    public static class HubTrainingContent
    {
        public const string Patch="base:hub-training-1";
        public static bool NeedsInstall(WorldState world)=>!world.contentPatches.Contains(Patch);
        public static void Install(WorldState world)
        {
            if(!NeedsInstall(world)) return;
            var asset=Resources.Load<TextAsset>("Content/hub-training");
            if(asset==null) throw new InvalidOperationException("Нет расстановки Content/hub-training.");
            var layout=JsonUtility.FromJson<TrainingLayout>(asset.text);
            if(layout==null || layout.patch!=Patch || layout.level!="hub" || layout.actors==null)
                throw new InvalidOperationException("Некорректная расстановка мишеней хаба.");
            var ids=new HashSet<string>();var additions=new List<ActorState>();
            foreach(var entry in layout.actors)
            {
                if(string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) || !float.IsFinite(entry.x) || !float.IsFinite(entry.y) || Math.Abs(entry.facing)!=1)
                    throw new InvalidOperationException("Некорректное размещение мишени: "+entry.id);
                var definition=ActorTypes.Get(entry.type);
                if(definition.restoreHealthAfterHit<=0 || definition.combatAi) throw new InvalidOperationException("Неучебный тип мишени: "+entry.type);
                var existing=world.Find(entry.id);
                if(existing!=null)
                {
                    if(existing.actorType!=entry.type || existing.level!=layout.level) throw new InvalidOperationException("Конфликт ID мишени с существующим актёром: "+entry.id);
                    continue;
                }
                additions.Add(new ActorState {id=entry.id,actorType=entry.type,level=layout.level,x=entry.x,y=entry.y,
                    facing=entry.facing,health=definition.restoreHealthAfterHit,weapon=null});
            }
            world.actors.AddRange(additions);world.contentPatches.Add(Patch);world.revision++;
            Debug.Log($"Добавлена тренировочная часть хаба: world={world.worldId}, actors={additions.Count}, patch={Patch}.");
        }
    }
}
