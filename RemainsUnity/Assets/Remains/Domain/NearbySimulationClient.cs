using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // Candidates are supplied by the caller's connection/eligibility policy.
    public static class NearbySimulationClient
    {
        public static string Select(IEnumerable<ActorState> candidates,string level,Vector2 position)
        {
            ActorState nearest=null;float distance=float.PositiveInfinity;
            foreach(var actor in candidates)
            {
                if(actor.level!=level) continue;
                float next=Vector2.Distance(actor.Position,position);
                if(next<distance || next==distance && string.CompareOrdinal(actor.id,nearest?.id)<0)
                {nearest=actor;distance=next;}
            }
            return nearest?.owner;
        }
    }
}
