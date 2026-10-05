namespace Remains.Domain
{
    // Simulation authority is independent of the owner's current input location.
    // Held objects still require a conscious holder beside them.
    public static class PropPoseScope
    {
        public static bool CanPublish(WorldState world,ActorState sender,PropState prop,bool levelPaused)=>
            sender!=null && sender.player && sender.connected && !string.IsNullOrEmpty(sender.owner) && prop!=null &&
            prop.movable && prop.hasPhysicalBody && prop.owner==sender.owner && !levelPaused && LevelActivity.HasConnectedPlayer(world,prop.level) &&
            (!prop.held || !sender.down && sender.level==prop.level);
    }
}
