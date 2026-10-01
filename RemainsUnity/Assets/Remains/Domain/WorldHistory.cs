using System;
using System.Collections.Generic;
using System.Linq;

namespace Remains.Domain
{
    [Serializable]
    public sealed class WorldAncestor
    {
        public string branchId;
        public long sequence;
    }

    public enum WorldRelation { Identical, Newer, Older, Divergent, Unknown, DifferentWorld }

    public static class WorldHistory
    {
        // A new host session always forks from the selected complete copy.
        // Its ancestry contains fork points, not one entry per network snapshot.
        public static void BeginContinuation(WorldState world)
        {
            world.ancestors ??= new List<WorldAncestor>();
            if(world.historyVersion==1)
                world.ancestors.Add(new WorldAncestor {branchId=world.branchId,sequence=world.checkpointSequence});
            else world.ancestors.Clear();
            world.branchId=Guid.NewGuid().ToString("N");
            world.checkpointSequence=0;
            world.historyVersion=1;
        }

        public static WorldRelation Compare(WorldState candidate,string candidateHash,WorldState selected,string selectedHash)
        {
            if(candidate.worldId!=selected.worldId) return WorldRelation.DifferentWorld;
            if(candidateHash==selectedHash) return WorldRelation.Identical;
            if(candidate.historyVersion!=1 || selected.historyVersion!=1) return WorldRelation.Unknown;
            if(candidate.branchId==selected.branchId)
            {
                if(candidate.checkpointSequence==selected.checkpointSequence) return WorldRelation.Unknown;
                return candidate.checkpointSequence>selected.checkpointSequence?WorldRelation.Newer:WorldRelation.Older;
            }
            if(Contains(candidate,selected)) return WorldRelation.Newer;
            if(Contains(selected,candidate)) return WorldRelation.Older;
            return WorldRelation.Divergent;
        }

        private static bool Contains(WorldState descendant,WorldState ancestor) =>
            descendant.ancestors!=null && descendant.ancestors.Any(a=>a.branchId==ancestor.branchId && a.sequence>=ancestor.checkpointSequence);

        public static string Description(WorldRelation relation) => relation switch {
            WorldRelation.Identical=>"Выбранная копия",
            WorldRelation.Newer=>"Более позднее продолжение",
            WorldRelation.Older=>"Более ранняя копия",
            WorldRelation.Divergent=>"Другая ветвь — прогресс не объединяется",
            WorldRelation.DifferentWorld=>"Другой мир",
            _=>"Родство не подтверждено"
        };
    }
}
