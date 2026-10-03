namespace Remains.Domain
{
    // Contact belongs to the target incarnation observed when the event arrived.
    public readonly struct AttackTargetSnapshot
    {
        public readonly string id,level;
        public readonly int generation,lifecycle;
        public readonly bool contact,player;
        public AttackTargetSnapshot(ActorState actor,bool contact)
        {id=actor.id;level=actor.level;generation=actor.generation;lifecycle=actor.lifecycleSequence;player=actor.player;this.contact=contact;}
        public bool Matches(ActorState actor)=>actor!=null && actor.id==id && actor.level==level &&
            actor.generation==generation && actor.lifecycleSequence==lifecycle;
        // NPC contact is the attacker's decision; delivery selects the current simulation owner.
        // A new incarnation or level is still a different target. Player decisions remain owner-bound.
        public bool CanApply(ActorState actor)=>actor!=null && actor.id==id && actor.level==level &&
            actor.lifecycleSequence==lifecycle && actor.player==player && (!player || actor.generation==generation);
    }
}
