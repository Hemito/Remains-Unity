using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Sleeping NPCs can enter a frame through a nearby bucket, an authoritative pose,
    // or a state change. Only previously active actors can move locally between events.
    public sealed class ActorFrameIndex
    {
        private const float CellSize=24;
        private sealed class Entry
        {
            public ActorState Actor;
            public (string Level,int X,int Y) Cell;
            public long Order;
        }
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly Dictionary<(string Level,int X,int Y),HashSet<Entry>> cells=new();
        private readonly HashSet<Entry> always=new();
        private readonly HashSet<Entry> burning=new();
        private readonly List<ActorState> players=new(),frame=new();
        private readonly HashSet<Entry> selected=new();
        private readonly List<Entry> ordered=new();
        private string worldId;
        private long order;
        public IReadOnlyList<ActorState> Frame=>frame;
        public IReadOnlyList<ActorState> Players=>players;
        public int Visited {get;private set;}
        public ActorState Find(string id)=>id!=null && entries.TryGetValue(id,out var entry)?entry.Actor:null;
        public void CollectBurning(List<ActorState> output)
        {
            output.Clear();
            foreach(var entry in burning) output.Add(entry.Actor);
        }

        public void Reconcile(WorldState world,IReadOnlyCollection<string> parts=null)
        {
            if(world==null || worldId!=world.worldId || parts==null)
            {
                if(worldId!=world?.worldId) frame.Clear();
                worldId=world?.worldId;
                entries.Clear();cells.Clear();always.Clear();burning.Clear();players.Clear();order=0;
                if(world!=null) foreach(var actor in world.actors) Add(actor);
                return;
            }
            foreach(string part in parts)
            {
                if(!part.StartsWith("actors:",StringComparison.Ordinal)) continue;
                string id=part.Substring(7);
                var actor=world.Find(id);
                if(!entries.TryGetValue(id,out var entry))
                {
                    if(actor!=null) Add(actor);
                    continue;
                }
                if(ReferenceEquals(actor,entry.Actor)) {Observe(actor);continue;}
                if(entry.Actor.player) players.Remove(entry.Actor);
                if(actor==null)
                {
                    Unlink(entry);entries.Remove(id);always.Remove(entry);burning.Remove(entry);
                    continue;
                }
                entry.Actor=actor;
                if(actor.player) players.Add(actor);
                Observe(actor);
            }
        }

        public void Observe(ActorState actor)
        {
            if(actor==null || !entries.TryGetValue(actor.id,out var entry) || !ReferenceEquals(entry.Actor,actor)) return;
            var cell=Key(actor);
            if(cell!=entry.Cell) {Unlink(entry);Link(entry,cell);}
            UpdateAlways(entry);
        }

        public void Select(ActorActivity activity,string visibleLevel,Rect visibleArea)
        {
            foreach(var actor in frame) Observe(actor);
            frame.Clear();selected.Clear();ordered.Clear();Visited=0;
            foreach(var entry in always)
                if(activity.Active(entry.Actor)) selected.Add(entry);
            var connected=activity.Connected.Players;
            for(int i=0;i<connected.Count;i++)
            {
                var player=connected[i];
                Query(player.level,Rect.MinMaxRect(player.x-NpcActivity.HalfWidth,player.y-NpcActivity.HalfHeight,
                    player.x+NpcActivity.HalfWidth,player.y+NpcActivity.HalfHeight),activity);
            }
            if(visibleLevel!=null) Query(visibleLevel,visibleArea,activity);
            ordered.AddRange(selected);
            ordered.Sort((a,b)=>a.Order.CompareTo(b.Order));
            foreach(var entry in ordered) frame.Add(entry.Actor);
        }

        private void Query(string level,Rect area,ActorActivity activity)
        {
            int right=Cell(area.xMax),top=Cell(area.yMax);
            for(int x=Cell(area.xMin);x<=right;x++)
            for(int y=Cell(area.yMin);y<=top;y++)
                if(cells.TryGetValue((level,x,y),out var bucket)) foreach(var entry in bucket)
                {
                    Visited++;
                    if(activity.Active(entry.Actor)) selected.Add(entry);
                }
        }

        private void Add(ActorState actor)
        {
            var entry=new Entry {Actor=actor,Order=order++};
            entries.Add(actor.id,entry);
            if(actor.player) players.Add(actor);
            Link(entry,Key(actor));UpdateAlways(entry);
        }
        private void UpdateAlways(Entry entry)
        {
            var actor=entry.Actor;
            if(actor.player || actor.attackBusy || ActorSessionRules.IsPassive(actor) || ActorHoldRules.Active(actor)) always.Add(entry);
            else always.Remove(entry);
            if(string.IsNullOrEmpty(actor.burnLevel)) burning.Remove(entry);else burning.Add(entry);
        }
        private static int Cell(float position)=>Mathf.FloorToInt(position/CellSize);
        private static (string Level,int X,int Y) Key(ActorState actor)=>(actor.level,Cell(actor.x),Cell(actor.y));
        private void Link(Entry entry,(string Level,int X,int Y) cell)
        {
            entry.Cell=cell;
            if(!cells.TryGetValue(cell,out var bucket)) cells.Add(cell,bucket=new());
            bucket.Add(entry);
        }
        private void Unlink(Entry entry)
        {
            var bucket=cells[entry.Cell];
            bucket.Remove(entry);
            if(bucket.Count==0) cells.Remove(entry.Cell);
        }
    }
}
