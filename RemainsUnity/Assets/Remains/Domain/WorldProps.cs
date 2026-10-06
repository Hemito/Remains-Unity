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
        public static PropState Find(WorldState world,string id)
        {
            if(world==null) return null;
            PropState prop=null,container=null;
            for(int i=0;i<world.props.Count;i++) if(world.props[i].id==id) {prop=world.props[i];break;}
            for(int i=0;i<world.containers.Count;i++) if(world.containers[i].id==id) {container=world.containers[i];break;}
            if(prop!=null && container!=null) throw new InvalidOperationException($"Повтор ID физического объекта и контейнера: {id}.");
            return prop??container;
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
