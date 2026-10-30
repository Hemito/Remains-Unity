namespace Remains.Domain
{
    public static class ActorPresence
    {
        // A saved hub character waits for its player; it is not an unconscious world body.
        public static bool IsPresent(ActorState actor) =>
            actor != null && (!actor.player || actor.connected || actor.level != "hub");
        // A buried zombie (UnitZombie.zakop: invis, overLook, no body height) is present and owned but takes no hits,
        // contacts or telekinesis and is not drawn until it digs out.
        public static bool Hidden(ActorState actor) => actor?.creature?.mode == ZombieBrain.BuriedMode;
    }
}
