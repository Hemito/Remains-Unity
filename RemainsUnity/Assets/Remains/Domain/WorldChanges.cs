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
        private static readonly ConditionalWeakTable<WorldState,object> tracked=new();

        // Opt-in: only a world whose every shared mutation goes through marking code (the host's session world)
        // reuses stored part texts. Tests, tools, previews and saves mutate worlds directly and stay fully serialized.
        public static void Track(WorldState world) {if(world!=null) tracked.GetValue(world,_=>new object());}
        public static bool IsTracked(WorldState world)=>world!=null && tracked.TryGetValue(world,out _);

        public static void Mark(WorldState world,object part)
        {
            if(world==null || part==null) return;
            marks.GetValue(world,_=>new HashSet<object>(ByReference.Instance)).Add(part);
        }

        public static bool IsMarked(WorldState world,object part)=>world!=null && part!=null &&
            marks.TryGetValue(world,out var set) && set.Contains(part);

        public static void Unmark(WorldState world,object part)
        {
            if(world!=null && part!=null && marks.TryGetValue(world,out var set)) set.Remove(part);
        }
        // After a composition consumed the marks (both kinds: the snapshot composition serializes motion marks too).
        public static void Clear(WorldState world)
        {
            if(world!=null && marks.TryGetValue(world,out var set)) set.Clear();
            if(world!=null && motion.TryGetValue(world,out var moved)) moved.Clear();
        }
        // The state marks only; motion marks wait for their spread refresh (world ticks).
        public static void ClearState(WorldState world)
        {
            if(world!=null && marks.TryGetValue(world,out var set)) set.Clear();
        }

        // Motion marks: an actor whose only change is its motion sample (position, velocity, pose clock), which already
        // reached every client in its own PoseMessage. World ticks refresh such actors spread over ticks instead of all
        // at once; a snapshot composition treats them as ordinary marks.
        private static readonly ConditionalWeakTable<WorldState,HashSet<object>> motion=new();
        public static void MarkMotion(WorldState world,object part)
        {
            if(world==null || part==null) return;
            motion.GetValue(world,_=>new HashSet<object>(ByReference.Instance)).Add(part);
        }
        public static bool IsMotionMarked(WorldState world,object part)=>world!=null && part!=null &&
            motion.TryGetValue(world,out var set) && set.Contains(part);
        public static void UnmarkMotion(WorldState world,object part)
        {
            if(world!=null && part!=null && motion.TryGetValue(world,out var set)) set.Remove(part);
        }
        // Drops motion marks of parts that left the world (removed or replaced actors).
        public static void RetainMotion(WorldState world,IEnumerable<object> present)
        {
            if(world!=null && motion.TryGetValue(world,out var set) && set.Count>0) set.IntersectWith(present);
        }
    }
}
