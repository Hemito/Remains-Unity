namespace Remains.Domain
{
    public static class LevelActivity
    {
        // Unconscious connected players still keep their level loaded and simulated.
        public static bool HasConnectedPlayer(WorldState world,string level)=>
            world!=null && world.actors.Exists(a=>a.player && a.connected && a.level==level);
    }
}
