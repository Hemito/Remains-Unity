namespace Remains.Domain
{
    // Publication order spans pose packets, actor updates and persisted world snapshots.
    // Simulation extrapolation does not increment this counter.
    public static class ActorMotionReplication
    {
        public static bool IsNewer(uint incoming,uint current)=>unchecked((int)(incoming-current))>0;
        public static bool SameLifetime(ActorState current,ActorState incoming)=>current!=null &&
            current.id==incoming.id && current.owner==incoming.owner && current.generation==incoming.generation &&
            current.lifecycleSequence==incoming.lifecycleSequence && current.level==incoming.level;
        public static void PreserveNewerMotion(ActorState current,ActorState incoming)
        {
            if(SameLifetime(current,incoming) && !IsNewer(incoming.motionSequence,current.motionSequence))
                ActorPose.CopyMotion(current,incoming);
        }
    }
}
