using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // A small immutable snapshot of binding strings. Compare stored JSON strings directly;
    // don't parse or hash the whole layout on every world notification.
    public sealed class FactoryContentIdentity
    {
        private readonly string[][] bindings;
        private readonly long? seed;
        private FactoryContentIdentity(WorldState world,List<WorldFactory> factories)
        {
            seed=world?.seed;
            if(factories==null) throw new ArgumentException("Мир: отсутствует список заводов.");
            bindings=new string[factories.Count][];
            for(int i=0;i<factories.Count;i++)
            {
                var factory=factories[i]??throw new ArgumentException("Мир: пустая запись завода.");
                bindings[i]=new[]{factory.level,factory.definition,factory.contentFingerprint,factory.layout};
            }
        }
        public static FactoryContentIdentity Capture(WorldState world)=>new FactoryContentIdentity(world,world==null?new List<WorldFactory>():world.factories);
        public bool Matches(WorldState world)
        {
            if(seed!=world?.seed) return false;
            var factories=world?.factories;
            if(factories==null) return world==null && bindings.Length==0;
            if(factories.Count!=bindings.Length) return false;
            for(int i=0;i<bindings.Length;i++)
            {
                var factory=factories[i];var saved=bindings[i];
                if(factory==null || !string.Equals(saved[0],factory.level,StringComparison.Ordinal) ||
                    !string.Equals(saved[1],factory.definition,StringComparison.Ordinal) ||
                    !string.Equals(saved[2],factory.contentFingerprint,StringComparison.Ordinal) ||
                    !string.Equals(saved[3],factory.layout,StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
