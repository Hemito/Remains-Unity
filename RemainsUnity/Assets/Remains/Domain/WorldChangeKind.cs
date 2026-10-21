using System;

namespace Remains.Domain
{
    // What a world notification (CoopSession.Changed) changed, so a consumer recomputes only its part. A received
    // delta names its changed part lists and all replaced live actor projections, including journal healing.
    // Full snapshots/composition changes use null part keys so consumers rebuild their dependent state.
    [Flags] public enum WorldChangeKind
    {
        None=0,
        Skeleton=1,       // fields outside the part lists (rooms, transactions, level times...)
        Containers=2,Props=4,Lamps=8,Doors=16,Actors=32,Factories=64,Projectiles=128,
        Identity=256,     // another world object or world ID: everything must be rebuilt
        Mines=512,Hazards=1024,
        All=2047
    }

    public static class WorldChangeKinds
    {
        // The flag of a snapshot part list (SnapshotPartCache.ListNames).
        public static WorldChangeKind ForList(string list)=>list switch
        {
            "containers"=>WorldChangeKind.Containers,"props"=>WorldChangeKind.Props,"lamps"=>WorldChangeKind.Lamps,
            "doors"=>WorldChangeKind.Doors,"actors"=>WorldChangeKind.Actors,"factories"=>WorldChangeKind.Factories,
            "projectiles"=>WorldChangeKind.Projectiles,"mines"=>WorldChangeKind.Mines,"hazards"=>WorldChangeKind.Hazards,
            _=>throw new ArgumentException("Неизвестный список мира: "+list)
        };
        public static bool Any(this WorldChangeKind kinds,WorldChangeKind wanted)=>(kinds&(wanted|WorldChangeKind.Identity))!=0;
    }
}
