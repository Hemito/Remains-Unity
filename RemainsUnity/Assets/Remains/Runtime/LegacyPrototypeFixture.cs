using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    // Existing regression scenarios target the old room's authored coordinates.
    // They explicitly load a legacy fixture; ordinary new games never use it.
    internal static class LegacyPrototypeFixture
    {
        public static void Prepare(PrototypeRuntime runtime)
        {
            var options=runtime.Options;
            if(options.Smoke==null || options.Smoke.StartsWith("factory-room") || options.Smoke.StartsWith("travel-points") || options.Role!="host") return;
            if(string.IsNullOrEmpty(options.SaveDirectory)) throw new InvalidOperationException("Старый тестовый мир требует отдельного каталога сохранений.");
            if(runtime.Session.Saves.SelectedSnapshot()!=null) return;
            var world=new WorldState();
            if(!(options.Smoke.StartsWith("actor-hold") && options.Smoke.Contains("-player-")))
            {
                var npc=new ActorState {id="npc:training-guard",actorType="raider",anatomy="raider",owner="host",x=8,health=140};
                if(options.Smoke.StartsWith("actor-hold")) npc.inventory.Add(SharedInventory.Create("heal",3));
                world.actors.Add(npc);
            }
            var container=new ContainerState();world.containers.Add(container);
            container.items.Add(SharedInventory.Create("caps",20));
            container.items.Add(SharedInventory.Create("heal",3));
            container.items.Add(SharedInventory.Create("grenade",3));
            world.props.Add(PropCatalog.Create("prop:crate-1","mcrate1","factory:0",new Vector2(-3,.8f)));
            runtime.Session.Saves.SaveWorld(world);
            Debug.Log("Регрессионный сценарий загружает старый тестовый мир: "+options.Smoke);
        }
    }
}
