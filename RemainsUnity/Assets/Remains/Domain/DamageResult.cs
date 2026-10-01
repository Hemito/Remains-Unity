namespace Remains.Domain
{
    // Presentation receipt, emitted only after the owner's damage rule has applied.
    public struct DamageResult
    {
        public string attack,actor,level;
        public int generation;
        public float amount,x,y;
        public string Key=>$"{actor}:{generation}:{attack}";
    }
}
