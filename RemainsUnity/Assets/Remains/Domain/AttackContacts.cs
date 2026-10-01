using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Evaluated on each deciding client against its own current poses.
    public static class AttackContacts
    {
        public static List<AttackTargetSnapshot> Capture(IEnumerable<ActorState> actors,AttackEvent attack,bool friendlyFire,bool stopAtFirst)
        {
            var result=new List<AttackTargetSnapshot>();
            ActorState nearest=null;float distance=float.PositiveInfinity;
            foreach(var actor in actors)
            {
                if(!AttackScope.CanAffect(attack,actor,friendlyFire) || Anatomy.Contact(actor,attack,out float entry)==null) continue;
                if(!stopAtFirst) result.Add(new AttackTargetSnapshot(actor,true));
                else if(entry<distance || entry==distance && string.CompareOrdinal(actor.id,nearest?.id)<0)
                {nearest=actor;distance=entry;}
            }
            if(nearest!=null) result.Add(new AttackTargetSnapshot(nearest,true));
            return result;
        }
    }
}
