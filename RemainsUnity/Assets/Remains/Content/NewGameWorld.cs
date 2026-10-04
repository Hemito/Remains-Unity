using Remains.Domain;

namespace Remains.Content
{
    public static class NewGameWorld
    {
        public static WorldState Create()
        {
            var world=new WorldState();
            world.rooms.Add(new WorldRoom {level="factory:0",definition="base:factory-entry-46"});
            return world;
        }
    }
}
