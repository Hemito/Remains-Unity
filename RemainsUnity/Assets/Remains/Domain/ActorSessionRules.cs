namespace Remains.Domain
{
    public static class ActorSessionRules
    {
        public const int ControlStateVersion=1;
        public static bool IsDisconnectedBody(ActorState actor)=>
            actor!=null && actor.player && !actor.connected && actor.level!="hub";
        public static bool IsPassive(ActorState actor)=>actor!=null && (actor.down || IsDisconnectedBody(actor));

        public static void Disconnect(ActorState actor)
        {
            actor.connected=false;
            actor.controlStateVersion=ControlStateVersion;
            actor.lifecycleSequence++;actor.revision++;
            if(IsDisconnectedBody(actor)) {actor.climbing=false;ActorPose.Select(actor,"down");}
            // Hub records remain recoverable; ActorPresence excludes them from the live world.
        }
        public static void Connect(ActorState actor,string owner)
        {
            actor.owner=owner;
            ActorHoldRules.Release(actor);
            actor.connected=true;
            actor.controlStateVersion=ControlStateVersion;
            if(!actor.down && actor.pose=="down") ActorPose.Select(actor,"idle");
            actor.generation++;actor.lifecycleSequence++;actor.revision++;
        }
    }
}
