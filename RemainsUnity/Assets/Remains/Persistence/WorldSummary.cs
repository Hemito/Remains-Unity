using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Persistence
{
    [Serializable]
    public sealed class WorldSummary
    {
        public string snapshot;
        public string worldId;
        public string branchId;
        public int historyVersion;
        public long sequence;
        public long revision;
        public List<WorldAncestor> ancestors;
        public static WorldSummary From(RecoverySnapshot copy)
        {
            var world=copy.CreateLiveWorld();
            return new WorldSummary {snapshot=copy.Id,worldId=world.worldId,branchId=world.branchId,
                historyVersion=world.historyVersion,sequence=world.checkpointSequence,revision=world.revision,ancestors=world.ancestors};
        }
        public WorldState Lineage() => new WorldState {worldId=worldId,branchId=branchId,
            historyVersion=historyVersion,checkpointSequence=sequence,revision=revision,ancestors=ancestors};
        public WorldRelation CompareTo(WorldSummary other) => WorldHistory.Compare(Lineage(),snapshot,other.Lineage(),other.snapshot);
    }
}
