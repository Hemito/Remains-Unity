using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Evaluated on each deciding client against its own current poses.
    public static class AttackContacts
    {
        public static float VisualLimit(IEnumerable<ActorState> actors,AttackEvent attack,bool friendlyFire,float terrainLimit)
        {
            float nearest=terrainLimit;
            foreach(var actor in actors)
                if(TryContact(actor,attack,friendlyFire,out float entry))
                    nearest=UnityEngine.Mathf.Min(nearest,entry);
            return nearest;
        }
        public static List<AttackTargetSnapshot> Capture(IEnumerable<ActorState> actors,AttackEvent attack,bool friendlyFire,bool stopAtFirst,float terrainLimit=1,
            Func<ActorState,float,bool> acceptContact=null)
        {
            var result=new List<AttackTargetSnapshot>();
            ActorState nearest=null;float distance=float.PositiveInfinity;
            foreach(var actor in actors)
            {
                if(!TryContact(actor,attack,friendlyFire,out float entry) || entry>=terrainLimit ||
                    acceptContact!=null && !acceptContact(actor,entry)) continue;
                if(!stopAtFirst) result.Add(new AttackTargetSnapshot(actor,true));
                else if(entry<distance || entry==distance && string.CompareOrdinal(actor.id,nearest?.id)<0)
                {nearest=actor;distance=entry;}
            }
            if(nearest!=null) result.Add(new AttackTargetSnapshot(nearest,true));
            return result;
        }
        private static bool TryContact(ActorState actor,AttackEvent attack,bool friendlyFire,out float entry)
        {
            entry=1;
            return AttackScope.CanAffect(attack,actor,friendlyFire) && Anatomy.Contact(actor,attack,out entry)!=null;
        }
    }
}
