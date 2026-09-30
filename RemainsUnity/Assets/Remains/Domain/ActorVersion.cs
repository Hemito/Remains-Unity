namespace Remains.Domain
{
    public static class ActorVersion
    {
        public static bool IsOlder(ActorState incoming,ActorState current)
        {
            if(incoming.generation!=current.generation) return incoming.generation<current.generation;
            if(incoming.lifecycleSequence!=current.lifecycleSequence) return incoming.lifecycleSequence<current.lifecycleSequence;
            return incoming.revision<current.revision;
        }
        public static bool CanPublishOwnerState(ActorState incoming,ActorState current)=>
            incoming.generation==current.generation && incoming.lifecycleSequence==current.lifecycleSequence && incoming.revision>current.revision;
    }
}
