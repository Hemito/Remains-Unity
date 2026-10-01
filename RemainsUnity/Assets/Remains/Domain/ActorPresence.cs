namespace Remains.Domain
{
    public static class ActorPresence
    {
        // A saved hub character waits for its player; it is not an unconscious world body.
        public static bool IsPresent(ActorState actor) =>
            actor != null && (!actor.player || actor.connected || actor.level != "hub");
    }
}
