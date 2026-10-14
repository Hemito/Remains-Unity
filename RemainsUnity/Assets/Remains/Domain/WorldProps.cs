using System;
using System.Collections;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Live references only: a container's pose and items stay in the same saved object.
    public static class WorldProps
    {
        public static IEnumerable<PropState> All(WorldState world)
        {
            foreach(var prop in world.props) yield return prop;
            foreach(var container in world.containers) yield return container;
        }
        // By ID through a per-world index (pose packets, snapshot merge, telekinesis called the former linear scan
        // over all ~1400 bodies). The index is checked on every hit (same object at the recorded position) and
        // rebuilt when list sizes change, a hit is stale or an ID is missing — lists are mutated in place elsewhere.
        public static PropState Find(WorldState world,string id)
        {
            if(world==null || id==null) return null;
            var index=indexes.GetValue(world,_=>new Index());
            if(index.TryGet(world,id,out var found)) return found;
            index.Build(world);
            return index.TryGet(world,id,out found)?found:null;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState,Index> indexes=new();
        private sealed class Index
        {
            private readonly Dictionary<string,(bool container,int position)> entries=new(StringComparer.Ordinal);
            private int props=-1,containers=-1;
            public bool TryGet(WorldState world,string id,out PropState found)
            {
                found=null;
                if(props!=world.props.Count || containers!=world.containers.Count || !entries.TryGetValue(id,out var entry)) return false;
                PropState candidate=entry.container?(entry.position<world.containers.Count?world.containers[entry.position]:null):
                    entry.position<world.props.Count?world.props[entry.position]:null;
                if(candidate==null || candidate.id!=id) return false;
                found=candidate;return true;
            }
            public void Build(WorldState world)
            {
                entries.Clear();
                for(int i=0;i<world.props.Count;i++) if(world.props[i]?.id!=null) entries.TryAdd(world.props[i].id,(false,i));   // first, as the scan
                for(int i=0;i<world.containers.Count;i++)
                {
                    string id=world.containers[i]?.id;if(id==null) continue;
                    if(entries.TryGetValue(id,out var other) && !other.container)
                        throw new InvalidOperationException($"Повтор ID физического объекта и контейнера: {id}.");
                    entries.TryAdd(id,(true,i));
                }
                props=world.props.Count;containers=world.containers.Count;
            }
        }
    }

    // Cache this view, not a copied list: replacements and additions remain authoritative.
    public sealed class WorldPropView : IReadOnlyList<PropState>
    {
        public WorldState World {get;}
        public WorldPropView(WorldState world)=>World=world??throw new ArgumentNullException(nameof(world));
        public int Count=>World.props.Count+World.containers.Count;
        public PropState this[int index]=>index<World.props.Count?World.props[index]:World.containers[index-World.props.Count];
        public Enumerator GetEnumerator()=>new(this);
        // General IEnumerable consumers retain the existing reference iterator;
        // hot-path consumers use GetEnumerator directly without boxing the struct.
        IEnumerator<PropState> IEnumerable<PropState>.GetEnumerator()=>WorldProps.All(World).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>WorldProps.All(World).GetEnumerator();
        public struct Enumerator : IEnumerator<PropState>
        {
            private List<PropState>.Enumerator props;
            private List<ContainerState>.Enumerator containers;
            private bool readingProps;
            internal Enumerator(WorldPropView view)
            {props=view.World.props.GetEnumerator();containers=view.World.containers.GetEnumerator();readingProps=true;Current=null;}
            public PropState Current {get;private set;}
            object IEnumerator.Current=>Current;
            public bool MoveNext()
            {
                if(readingProps && props.MoveNext()) {Current=props.Current;return true;}
                readingProps=false;
                if(containers.MoveNext()) {Current=containers.Current;return true;}
                Current=null;return false;
            }
            public void Reset()=>throw new NotSupportedException("Повторный обход требует нового enumerator живых объектов.");
            public void Dispose() {props.Dispose();containers.Dispose();}
        }
    }
}
