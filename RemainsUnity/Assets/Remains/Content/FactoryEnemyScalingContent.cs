using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Prepared separately from append-only RoomContentPublication: legacy wounds and references survive.
    public static class FactoryEnemyScalingContent
    {
        private static bool Npcs(NpcLayout layout)=>layout?.patch==layout?.level+":"+FactoryCreatureAdapter.Patch;
        private static bool Hazards(HazardLayout layout)=>layout?.patch==layout?.level+":"+FactoryHazardAdapter.Patch;
        private static bool Mines(MineLayout layout)=>layout?.patch==layout?.level+":"+FactoryMineAdapter.Patch;
        public static bool NeedsApply(WorldState world,RoomContentBatch batch)
        {
            if(world==null || batch==null) return false;
            // Even an unchanged version must validate its stored provenance before being skipped.
            return new Plan(world,batch).HasChanges;
        }
        public sealed class Plan
        {
            private readonly string worldId;
            private readonly int seed;
            private readonly List<ActorChange> actors=new();
            private readonly List<HazardChange> hazards=new();
            private readonly List<MineChange> mines=new();
            internal bool HasChanges=>actors.Count+hazards.Count+mines.Count>0;
            private sealed class ActorChange {public string id,type,level;public float before,after;public FactoryEnemyScaleState scale,previousScale;}
            private sealed class HazardChange {public string id,type,level;public float before,after;public FactoryEnemyScaleState scale;}
            private sealed class MineChange {public string id,type,level;public FactoryEnemyScaleState scale;}
            internal Plan(WorldState world,RoomContentBatch batch)
            {
                worldId=world.worldId;seed=world.seed;
                var actorIndex=world.actors.ToDictionary(a=>a.id,StringComparer.Ordinal);
                var hazardIndex=world.hazards.ToDictionary(a=>a.id,StringComparer.Ordinal);
                var mineIndex=world.mines.ToDictionary(a=>a.id,StringComparer.Ordinal);
                if(Npcs(batch.npcs)) foreach(var source in batch.npcs.actors)
                {
                    if(!actorIndex.TryGetValue(source.id,out var actor)) continue; // Removed NPCs stay removed.
                    if(actor.player || actor.actorType!=source.actorType || actor.level!=source.level)
                        throw new InvalidOperationException("Масштабирование: конфликт сохранённого NPC "+source.id);
                    bool turret=ActorTypes.Get(source.actorType).turret!=null;
                    int floor=FactoryEnemyScaling.PlacementFloor(source.id,source.level);
                    if(actor.factoryEnemy!=null)
                    {
                        FactoryEnemyScaling.ValidatePlacement(actor.factoryEnemy,seed,source.id,floor,turret?FactoryEnemyScaling.Turret:FactoryEnemyScaling.Unit,source.health);
                        if(!turret || actor.factoryEnemy.version!=1) continue;
                    }
                    var scale=actor.factoryEnemy!=null?FactoryEnemyScaling.UpgradeTurret(actor.factoryEnemy):
                        FactoryEnemyScaling.Roll(seed,source.id,FactoryEnemyScaling.PlacementFloor(source.id,source.level),turret?FactoryEnemyScaling.Turret:FactoryEnemyScaling.Unit,source.health);
                    actors.Add(new ActorChange {id=source.id,type=source.actorType,level=source.level,before=actor.health,
                        after=FactoryEnemyScaling.PreserveHealth(actor.health,actor.factoryEnemy?.maxHealth??source.health,scale.maxHealth),scale=scale,
                        previousScale=FactoryEnemyScaling.Capture(actor.factoryEnemy)});
                }
                if(Hazards(batch.hazards)) foreach(var source in batch.hazards.hazards)
                {
                    if(!hazardIndex.TryGetValue(source.id,out var hazard)) continue;
                    if(hazard.definition!=source.definition || hazard.level!=source.level)
                        throw new InvalidOperationException("Масштабирование: конфликт сохранённой ловушки "+source.id);
                    var definition=HazardRules.Profile(source);
                    var kind=definition.kind==HazardRules.Trap?FactoryEnemyScaling.Trap:FactoryEnemyScaling.Charge;
                    if(hazard.factoryEnemy!=null)
                    {FactoryEnemyScaling.ValidatePlacement(hazard.factoryEnemy,seed,source.id,FactoryEnemyScaling.PlacementFloor(source.id,source.level),kind,definition.health);continue;}
                    var scale=FactoryEnemyScaling.Roll(seed,source.id,FactoryEnemyScaling.PlacementFloor(source.id,source.level),kind,definition.health);
                    hazards.Add(new HazardChange {id=source.id,type=source.definition,level=source.level,before=hazard.health,
                        after=FactoryEnemyScaling.PreserveHealth(hazard.health,definition.health,scale.maxHealth),scale=scale});
                }
                if(Mines(batch.mines)) foreach(var source in batch.mines.mines)
                {
                    if(!mineIndex.TryGetValue(source.id,out var mine)) continue;
                    if(mine.definition!=source.definition || mine.level!=source.level)
                        throw new InvalidOperationException("Масштабирование: конфликт сохранённой мины "+source.id);
                    if(mine.factoryEnemy!=null)
                    {FactoryEnemyScaling.ValidatePlacement(mine.factoryEnemy,seed,source.id,FactoryEnemyScaling.PlacementFloor(source.id,source.level),FactoryEnemyScaling.Mine,0);continue;}
                    var scale=FactoryEnemyScaling.Roll(seed,source.id,FactoryEnemyScaling.PlacementFloor(source.id,source.level),FactoryEnemyScaling.Mine,0);
                    mines.Add(new MineChange {id=source.id,type=source.definition,level=source.level,scale=scale});
                }
            }
            public bool Apply(WorldState world)
            {
                if(world.worldId!=worldId || world.seed!=seed) throw new InvalidOperationException("Изменился мир масштабирования противников.");
                if(actors.Count+hazards.Count+mines.Count==0) return false;
                var actorIndex=world.actors.ToDictionary(a=>a.id,StringComparer.Ordinal);
                var hazardIndex=world.hazards.ToDictionary(a=>a.id,StringComparer.Ordinal);
                var mineIndex=world.mines.ToDictionary(a=>a.id,StringComparer.Ordinal);
                foreach(var change in actors)
                    if(!actorIndex.TryGetValue(change.id,out var actor) || actor.actorType!=change.type || actor.level!=change.level || !FactoryEnemyScaling.Same(actor.factoryEnemy,change.previousScale) || actor.health!=change.before || actor.revision==long.MaxValue)
                        throw new InvalidOperationException("Изменился NPC до публикации масштабирования: "+change.id);
                foreach(var change in hazards)
                    if(!hazardIndex.TryGetValue(change.id,out var hazard) || hazard.definition!=change.type || hazard.level!=change.level || hazard.factoryEnemy!=null || hazard.health!=change.before)
                        throw new InvalidOperationException("Изменилась ловушка до публикации масштабирования: "+change.id);
                foreach(var change in mines)
                    if(!mineIndex.TryGetValue(change.id,out var mine) || mine.definition!=change.type || mine.level!=change.level || mine.factoryEnemy!=null)
                        throw new InvalidOperationException("Изменилась мина до публикации масштабирования: "+change.id);
                if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпана ревизия мира при масштабировании противников.");
                foreach(var change in actors)
                {var actor=actorIndex[change.id];actor.factoryEnemy=change.scale;actor.health=change.after;actor.revision++;WorldChanges.Mark(world,actor);}
                foreach(var change in hazards)
                {var hazard=hazardIndex[change.id];hazard.factoryEnemy=change.scale;hazard.health=change.after;WorldChanges.Mark(world,hazard);}
                foreach(var change in mines)
                {var mine=mineIndex[change.id];mine.factoryEnemy=change.scale;WorldChanges.Mark(world,mine);}
                world.revision++;
                var rolled=actors.Select(c=>c.scale).Concat(hazards.Select(c=>c.scale)).Concat(mines.Select(c=>c.scale)).Where(s=>s.rolledChampion>0).ToArray();
                if(rolled.Length>0)
                    Debug.LogWarning($"Масштабирование завода: world={worldId}, seed={seed}, version={FactoryEnemyScaling.Version}; чемпионский бросок сохранён, но свойства/окраска чемпионов пока не представлены (TD-28); объектов={rolled.Length}; "+
                        string.Join("; ",rolled.Take(5).Select(s=>$"id={s.placement}, floor={s.floor}, level={s.enemyLevel}, hero={s.rolledChampion}")));
                Debug.LogWarning($"Масштабирование завода: world={worldId}, seed={seed}, version={FactoryEnemyScaling.Version}, NPC={actors.Count}, hazards={hazards.Count}, mines={mines.Count}; критический шанс, радиация, запас брони, наблюдательность, навык оружия и урон стенам не представлены, рост этих систем отложен (TD-28). Базовый уровень=0, глобальная сложность=2; подробности docs/design/npc.md.");
                return true;
            }
        }
        public static Plan Prepare(WorldState world,RoomContentBatch batch)=>new(world,batch);
    }
}
