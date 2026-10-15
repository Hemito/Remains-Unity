using System;

namespace Remains.Domain
{
    // What a world notification (CoopSession.Changed) changed, so a consumer recomputes only its part. A received
    // delta names its changed part lists; every other notification is All. Actors are always Actors: their objects
    // are replaced on every received world (the client changes its own actors locally).
    [Flags] public enum WorldChangeKind
    {
        None=0,
        Skeleton=1,       // fields outside the part lists (rooms, transactions, level times...)
        Containers=2,Props=4,Lamps=8,Doors=16,Actors=32,Factories=64,Projectiles=128,
        Identity=256,     // another world object or world ID: everything must be rebuilt
        All=511
    }

    public static class WorldChangeKinds
    {
        // The flag of a snapshot part list (SnapshotPartCache.ListNames).
        public static WorldChangeKind ForList(string list)=>list switch
        {
            "containers"=>WorldChangeKind.Containers,"props"=>WorldChangeKind.Props,"lamps"=>WorldChangeKind.Lamps,
            "doors"=>WorldChangeKind.Doors,"actors"=>WorldChangeKind.Actors,"factories"=>WorldChangeKind.Factories,
            "projectiles"=>WorldChangeKind.Projectiles,
            _=>throw new ArgumentException("Неизвестный список мира: "+list)
        };
        public static bool Any(this WorldChangeKind kinds,WorldChangeKind wanted)=>(kinds&(wanted|WorldChangeKind.Identity))!=0;
    }
}
