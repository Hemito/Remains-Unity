using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Domain
{
    public sealed class OwnershipService
    {
        public void Reassign(WorldState world, IReadOnlyCollection<string> activeOwners)
        {
            var active = world.actors.Where(a => a.player && a.connected && !a.down && activeOwners.Contains(a.owner)).ToArray();
            foreach (var actor in world.actors.Where(a => !a.player || !a.connected))
            {
                if (actor.attackBusy && activeOwners.Contains(actor.owner)) continue;
                var candidate = active.Where(a => a.level == actor.level)
                    .OrderBy(a => Vector2.Distance(a.Position, actor.Position)).ThenBy(a => a.id).FirstOrDefault();
                string next = candidate?.owner ?? "host";
                actor.target = candidate?.id;
                if (actor.owner == next) continue;
                actor.owner = next;
                actor.generation++;
                actor.revision++;
            }
        }
    }
}
