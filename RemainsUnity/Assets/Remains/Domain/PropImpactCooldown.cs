using System;

namespace Remains.Domain
{
    // Deadlines belong to one durable level clock, never wall time or another level's elapsed time.
    public static class PropImpactCooldown
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ActorState,object> warnedLegacy=new();
        public static bool Active(ActorState actor,double now)
        {
            if(actor!=null && actor.boxImpactUntil>0 && string.IsNullOrEmpty(actor.boxImpactLevel) && !warnedLegacy.TryGetValue(actor,out _))
            {
                warnedLegacy.Add(actor,new object());
                UnityEngine.Debug.LogWarning($"Не применён старый срок защиты от Box без уровня часов: actor={actor.id}, level={actor.level}, until={actor.boxImpactUntil}; часы исходной локации неизвестны.");
            }
            return actor!=null && actor.boxImpactLevel==actor.level && actor.boxImpactUntil>now;
        }
        public static void Record(ActorState actor,string level,double until)
        {
            if(actor==null || actor.level!=level || !double.IsFinite(until) || until<0)
                throw new InvalidOperationException("Некорректные часы защиты от Box: actor="+actor?.id+", level="+level+", until="+until);
            actor.boxImpactUntil=actor.boxImpactLevel==level?Math.Max(actor.boxImpactUntil,until):until;actor.boxImpactLevel=level;
        }
        public static void Clear(ActorState actor) {actor.boxImpactLevel=null;actor.boxImpactUntil=0;}
        public static void Preserve(ActorState current,ActorState incoming)
        {
            if(current==null || incoming==null || current.id!=incoming.id || current.level!=incoming.level || current.lifecycleSequence!=incoming.lifecycleSequence)return;
            if(current.boxImpactLevel==incoming.level)Record(incoming,incoming.level,current.boxImpactUntil);
        }
    }
}
