namespace Remains.Domain
{
    // Contact belongs to the target incarnation observed when the event arrived.
    public readonly struct AttackTargetSnapshot
    {
        public readonly string id,level;
        public readonly int generation,lifecycle;
        public readonly bool contact;
        public AttackTargetSnapshot(ActorState actor,bool contact)
        {id=actor.id;level=actor.level;generation=actor.generation;lifecycle=actor.lifecycleSequence;this.contact=contact;}
        public bool Matches(ActorState actor)=>actor!=null && actor.id==id && actor.level==level &&
            actor.generation==generation && actor.lifecycleSequence==lifecycle;
    }
}
