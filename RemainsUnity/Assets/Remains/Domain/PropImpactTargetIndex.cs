using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Event-updated actor buckets. A local actor event replaces just that actor; a new replica graph
    // refreshes references once. Player movement selects only its neighbouring buckets.
    public sealed class PropImpactTargetIndex
    {
        private readonly Dictionary<string,ActorState> actors=new(StringComparer.Ordinal),players=new(StringComparer.Ordinal),active=new(StringComparer.Ordinal);
        private readonly Dictionary<string,(string level,int cell)> keys=new(StringComparer.Ordinal);
        private readonly Dictionary<(string level,int cell),HashSet<string>> buckets=new();
        private readonly HashSet<string> levels=new(StringComparer.Ordinal);
        private WorldState graph;
        public IEnumerable<ActorState> Active=>active.Values;
        public IEnumerable<string> Levels=>levels;
        public bool HasLevel(string level)=>levels.Contains(level);
        public bool IntersectsActiveZone(string level,Rect area)
        {
            foreach(var player in players.Values)
                if(player.level==level && area.xMax>=player.x-NpcActivity.HalfWidth && area.xMin<=player.x+NpcActivity.HalfWidth &&
                    area.yMax>=player.y-NpcActivity.HalfHeight && area.yMin<=player.y+NpcActivity.HalfHeight) return true;
            return false;
        }
        public void Query(string level,Rect area,List<ActorState> output)
        {
            output.Clear();LastVisited=0;
            // Actor widths vary: selecting centre buckets needs the prepared maximum half width.
            float reach=maxHalfWidth;
            for(int cell=Cell(area.xMin-reach);cell<=Cell(area.xMax+reach);cell++)
                if(buckets.TryGetValue((level,cell),out var bucket)) foreach(string id in bucket)
                {LastVisited++;if(active.TryGetValue(id,out var actor)) output.Add(actor);}
        }
        private float maxHalfWidth;
        private readonly List<ActorState> moving=new();
        public int LastSynchronized {get;private set;}
        // Simulation mutates actor positions before publishing Changed. Refresh only active bodies
        // immediately before collision sampling, so crossing a bucket cannot miss a current contact.
        public void SynchronizeActive()
        {
            moving.Clear();LastSynchronized=0;
            foreach(var actor in active.Values) moving.Add(actor);
            bool playerMoved=false;
            foreach(var actor in moving)
            {
                LastSynchronized++;
                var next=(actor.level,Cell(actor.x));
                if(keys.TryGetValue(actor.id,out var previous) && previous!=next)
                {
                    var bucket=buckets[previous];bucket.Remove(actor.id);if(bucket.Count==0) buckets.Remove(previous);
                    keys[actor.id]=next;
                    if(!buckets.TryGetValue(next,out var destination)) buckets.Add(next,destination=new HashSet<string>(StringComparer.Ordinal));
                    destination.Add(actor.id);
                }
                playerMoved|=actor.player;
            }
            // Player movement can expose neighbouring sleepers; selection visits neighbouring buckets only.
            if(playerMoved) SelectActive();
        }
        public int LastVisited {get;private set;}
        private static int Cell(float x)=>Mathf.FloorToInt(x/PropSupportIndex.CellWidth);
        public void Refresh(WorldState world,IReadOnlyCollection<string> parts=null)
        {
            LastVisited=0;
            if(world==null) {graph=null;actors.Clear();players.Clear();active.Clear();keys.Clear();buckets.Clear();levels.Clear();return;}
            if(graph!=world || parts==null)
            {
                graph=world;actors.Clear();players.Clear();active.Clear();keys.Clear();buckets.Clear();maxHalfWidth=0;
                foreach(var actor in world.actors) {Add(actor);LastVisited++;}
                SelectActive();return;
            }
            bool playerChanged=false;
            foreach(string part in parts)
            {
                if(!part.StartsWith("actors:",StringComparison.Ordinal)) continue;
                string id=part.Substring(7);playerChanged|=players.ContainsKey(id);
                Remove(id);var actor=world.Find(id);
                if(actor!=null) {Add(actor);playerChanged|=actor.player;}
                LastVisited++;
                if(actor!=null && Eligible(actor)) active[id]=actor;else active.Remove(id);
            }
            if(playerChanged) SelectActive();
        }
        private void Add(ActorState actor)
        {
            actors[actor.id]=actor;var key=(actor.level,Cell(actor.x));keys[actor.id]=key;
            maxHalfWidth=Mathf.Max(maxHalfWidth,Anatomy.Movement(actor).width*.5f);
            if(!buckets.TryGetValue(key,out var bucket)) buckets.Add(key,bucket=new HashSet<string>(StringComparer.Ordinal));
            bucket.Add(actor.id);
            if(actor.player && actor.connected) players[actor.id]=actor;
        }
        private void Remove(string id)
        {
            if(keys.TryGetValue(id,out var key)) {var bucket=buckets[key];bucket.Remove(id);if(bucket.Count==0) buckets.Remove(key);keys.Remove(id);}
            actors.Remove(id);players.Remove(id);
        }
        private bool Eligible(ActorState actor)
        {
            if(actor.health<=0 || !ActorPresence.IsPresent(actor)) return false;
            foreach(var player in players.Values)
                if(player.level==actor.level && (actor.player || Mathf.Abs(player.x-actor.x)<=NpcActivity.HalfWidth && Mathf.Abs(player.y-actor.y)<=NpcActivity.HalfHeight)) return true;
            return false;
        }
        private void SelectActive()
        {
            active.Clear();levels.Clear();
            foreach(var player in players.Values)
            {
                levels.Add(player.level);
                for(int cell=Cell(player.x-NpcActivity.HalfWidth);cell<=Cell(player.x+NpcActivity.HalfWidth);cell++)
                    if(buckets.TryGetValue((player.level,cell),out var bucket)) foreach(string id in bucket)
                    {LastVisited++;var actor=actors[id];if(Eligible(actor)) active[id]=actor;}
            }
        }
    }
}
