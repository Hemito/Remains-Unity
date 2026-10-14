using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Remains.Domain
{
    // Change journal of a world (host-migration.md, «репликация дельтами»): code that changes shared state marks the
    // changed part (a container, body, lamp, door or actor object). Delta replication and snapshot composition reuse
    // the stored text of unmarked parts. Until every mutation site marks its part, SnapshotPartCache audits the
    // marks against full serialization and reports unmarked changes (stage 1, audit mode).
    public static class WorldChanges
    {
        private sealed class ByReference : IEqualityComparer<object>
        {
            public static readonly ByReference Instance=new();
            public new bool Equals(object a,object b)=>ReferenceEquals(a,b);
            public int GetHashCode(object value)=>RuntimeHelpers.GetHashCode(value);
        }
        private static readonly ConditionalWeakTable<WorldState,HashSet<object>> marks=new();

        public static void Mark(WorldState world,object part)
        {
            if(world==null || part==null) return;
            marks.GetValue(world,_=>new HashSet<object>(ByReference.Instance)).Add(part);
        }

        public static bool IsMarked(WorldState world,object part)=>world!=null && part!=null &&
            marks.TryGetValue(world,out var set) && set.Contains(part);

        // After a composition consumed the marks.
        public static void Clear(WorldState world)
        {
            if(world!=null && marks.TryGetValue(world,out var set)) set.Clear();
        }
    }
}
