namespace Remains.Domain
{
    // The original simulates only the location (one room) the player is in; a level here is the whole factory. A
    // non-player actor far from every connected player of its level sleeps — no senses, motion or attack — until one
    // comes within about a room (a room of the factory grid is 48×25 cells of 0.8 m). An attack already begun ends.
    public static class NpcActivity
    {
        public const float HalfWidth=40,HalfHeight=22;
        public static bool Awake(WorldState world,ActorState npc)=>Awake(world.actors,npc);
        public static bool Awake(System.Collections.Generic.IReadOnlyList<ActorState> actors,ActorState npc)
        {
            if(npc.attackBusy) return true;
            // Indexed: foreach over the interface boxes an enumerator, and this runs for every NPC every frame.
            for(int i=0;i<actors.Count;i++)
            {
                var player=actors[i];
                if(player.player && player.connected && player.level==npc.level &&
                    System.Math.Abs(player.x-npc.x)<=HalfWidth && System.Math.Abs(player.y-npc.y)<=HalfHeight) return true;
            }
            return false;
        }
    }
}
