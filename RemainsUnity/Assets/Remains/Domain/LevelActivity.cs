namespace Remains.Domain
{
    public static class LevelActivity
    {
        // Unconscious connected players still keep their level loaded and simulated.
        // A plain loop: called per actor per frame, the former Exists(lambda) allocated a closure each call.
        public static bool HasConnectedPlayer(WorldState world,string level)
        {
            if(world==null) return false;
            var actors=world.actors;
            for(int i=0;i<actors.Count;i++)
            {
                var a=actors[i];
                if(a.player && a.connected && a.level==level) return true;
            }
            return false;
        }
    }
}
