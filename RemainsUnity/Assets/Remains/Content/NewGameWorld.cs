using Remains.Domain;

namespace Remains.Content
{
    public static class NewGameWorld
    {
        public const string FactoryDefinitionId="base:factory-connected-46-18";
        public static WorldState Create()
        {
            var world=new WorldState();
            world.rooms.Add(new WorldRoom {level="factory:0",definition=FactoryDefinitionId});
            return world;
        }
    }
}
