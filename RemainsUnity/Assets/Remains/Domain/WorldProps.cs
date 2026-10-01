using System;
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
            var prop=world.props.Find(p=>p.id==id);
            var container=world.containers.Find(c=>c.id==id);
            if(prop!=null && container!=null) throw new InvalidOperationException($"Повтор ID физического объекта и контейнера: {id}.");
            return prop??container;
        }
    }
}
