namespace Remains.Domain
{
    // Presentation receipt, emitted only after the owner's damage rule has applied.
    public struct DamageResult
    {
        public string attack,actor,level;
        // Explicit owner-confirmed outcome; presentation must not infer a new
        // death by comparing snapshots or by observing a loaded corpse.
        public string world,actorType;
        public int generation,lifecycleSequence,damageSequence;
        public bool confirmedNpcDeath;
        public float amount,x,y;
        public string Key=>$"{actor}:{attack}";
    }
}
