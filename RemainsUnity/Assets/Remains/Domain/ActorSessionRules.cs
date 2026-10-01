namespace Remains.Domain
{
    public static class ActorSessionRules
    {
        public static void Disconnect(ActorState actor)
        {
            actor.connected=false;
            actor.lifecycleSequence++;actor.revision++;
            if(actor.level!="hub") {actor.down=true;ActorPose.Select(actor,"down");}
            // Hub records remain recoverable; ActorPresence excludes them from the live world.
        }
    }
}
