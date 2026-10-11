using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    // Diagnostics only: a world bound to the old authored connected factory (modules 46/18/53/40/27), for
    // historical checks and probes that depend on its hard-coded placements and routes. New ordinary worlds
    // use the generated factory (NewGameWorld); this fixture never selects the production default.
    public static class LegacyFactoryFixture
    {
        public const string DefinitionId="base:factory-connected-46-18";
        public static WorldState Create()
        {
            Debug.Log("Историческая проверка: мир со старым собранным заводом ("+DefinitionId+"), не обычный новый мир.");
            var world=new WorldState();
            world.rooms.Add(new WorldRoom {level="factory:0",definition=DefinitionId});
            return world;
        }
    }
}
