using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Domain
{
    public sealed class OwnershipService
    {
        public void BeginSession(WorldState world)
        {
            ProjectileLifecycle.BeginSession(world);
            foreach(var actor in world.actors)
            {
                // Transport connection IDs have no identity across session boundaries.
                actor.owner=null;
                if(actor.player) actor.connected=false;
                else
                {
                    actor.target=null;
                    if(actor.attackBusy) NpcAttackRules.Cancel(actor);
                }
                actor.generation++;actor.revision++;
            }
            foreach(var prop in WorldProps.All(world))
            {prop.owner=null;prop.held=false;prop.generation++;}
        }
        public void Reassign(WorldState world, IReadOnlyCollection<string> activeOwners)
        {
            GrenadeRules.Reassign(world,activeOwners);
            ProjectileLifecycle.Reassign(world,activeOwners);
            var active = world.actors.Where(a => a.player && a.connected && !a.down && activeOwners.Contains(a.owner)).ToArray();
            foreach (var actor in world.actors.Where(a => !a.player || !a.connected))
            {
                if (!ActorPresence.IsPresent(actor))
                {
                    if (actor.owner != null || actor.target != null || actor.attackBusy)
                    {
                        actor.owner=null;actor.target=null;actor.attackBusy=false;actor.attackRemaining=0;
                        actor.generation++;actor.revision++;
                    }
                    continue;
                }
                if (actor.attackBusy && activeOwners.Contains(actor.owner)) continue;
                if (actor.attackBusy || !actor.player && actor.owner!=null && actor.owner!="host" && !activeOwners.Contains(actor.owner))
                {
                    // The latest attack may not have arrived before owner loss.
                    // Reset recovery cooldown even when the last snapshot looked idle.
                    NpcAttackRules.Cancel(actor);
                }
                var candidate = active.Where(a => a.level == actor.level)
                    .OrderBy(a => Vector2.Distance(a.Position, actor.Position)).ThenBy(a => a.id).FirstOrDefault();
                string next = candidate?.owner ?? "host";
                actor.target = !actor.player && !Remains.Content.ActorTypes.Get(actor.actorType).combatAi?null:candidate?.id;
                if (actor.owner == next) continue;
                actor.owner = next;
                actor.generation++;
                actor.revision++;
            }
            foreach(var prop in WorldProps.All(world))
            {
                if(active.Any(a=>a.owner==prop.owner && a.level==prop.level)) continue;
                var candidate=active.Where(a=>a.level==prop.level).OrderBy(a=>Vector2.Distance(a.Position,prop.Position)).FirstOrDefault();
                string next=candidate?.owner??"host";
                if(prop.owner==next) continue;
                prop.owner=next;prop.held=false;prop.generation++;
            }
        }
    }
}
