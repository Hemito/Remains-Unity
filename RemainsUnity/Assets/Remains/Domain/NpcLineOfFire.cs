using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Friendly fire is on for everyone (user 2026-11-05, PT-20261103-29), so a sapient NPC holds its attack while another
    // one stands on the line to its target instead of hitting it. Asked only by an NPC that sees a target.
    public static class NpcLineOfFire
    {
        // How close to the line a body counts, and the stretch at the shooter's and the target's ends it may stand in.
        public const float BodyRadius=.45f,EndMargin=.3f;
        private const float BodyHeight=.55f;

        public static bool Blocked(IReadOnlyList<ActorState> actors,ActorState shooter,ActorState target,Vector2 from,Vector2 to)
        {
            var line=to-from;float length=line.magnitude;
            if(length<=EndMargin*2) return false;
            var along=line/length;
            for(int i=0;i<actors.Count;i++)
            {
                var other=actors[i];
                if(other.player || other.down || other.level!=shooter.level || ReferenceEquals(other,shooter) || ReferenceEquals(other,target)) continue;
                // Most of a factory is far from the line: the box before the body's sampled pose and its type.
                var center=new Vector2(other.x,other.y+BodyHeight);
                var offset=center-from;float ahead=Vector2.Dot(offset,along);
                if(ahead<EndMargin || ahead>length-EndMargin || Mathf.Abs(along.x*offset.y-along.y*offset.x)>BodyRadius) continue;
                if(ActorPresence.IsPresent(other) && !ActorPresence.Hidden(other) && SapientPonies.Is(other)) return true;
            }
            return false;
        }
    }
}
