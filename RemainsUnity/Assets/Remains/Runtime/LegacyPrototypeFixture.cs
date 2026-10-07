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
            if(options.Smoke is "performance-audit" or "runtime-catalog-release" || options.Smoke?.StartsWith("office-opening")==true ||
                options.Smoke?.StartsWith("training")==true || options.Smoke?.StartsWith("shot-spread")==true ||
                options.Smoke?.StartsWith("held-prop-color")==true || options.Smoke?.StartsWith("weapon-reload")==true || options.Smoke?.StartsWith("direct-items")==true || options.Smoke?.StartsWith("slope-poses")==true) return;
            if(options.Smoke==null || options.Smoke.StartsWith("warehouse-reload") || options.Smoke.StartsWith("warehouse-seam") || options.Smoke.StartsWith("office-furniture") || options.Smoke.StartsWith("office-recovery") || options.Smoke.StartsWith("ladder-contact") || options.Smoke.StartsWith("mechanism") || options.Smoke.StartsWith("prop-body") || options.Smoke.StartsWith("factory-room") || options.Smoke.StartsWith("travel-points") || options.Smoke.StartsWith("contact-audio") || options.Smoke.StartsWith("crawler") || options.Role!="host") return;
            if(string.IsNullOrEmpty(options.SaveDirectory)) throw new InvalidOperationException("Старый тестовый мир требует отдельного каталога сохранений.");
            if(runtime.Session.Saves.SelectedSnapshot()!=null) return;
            var world=new WorldState();
            if(!options.Smoke.StartsWith("shortcuts") && !(options.Smoke.StartsWith("actor-hold") && options.Smoke.Contains("-player-")))
            {
                var npc=new ActorState {id="npc:training-guard",actorType="raider",anatomy="raider",owner="host",x=8,health=140};
                if(options.Smoke.StartsWith("npc") && options.Smoke.Contains("-ammo-"))
                {
                    var item=SharedInventory.Create("pistol",1);item.weaponState=new WeaponItemState {rounds=1};npc.inventory.Add(item);
                    npc.inventory.Add(SharedInventory.Create("p10",2));npc.weaponInventoryVersion=1;
                    if(!WeaponInventory.Select(npc,item.id,out string reason)) throw new InvalidOperationException(reason);
                }
                if(options.Smoke.StartsWith("actor-hold")) npc.inventory.Add(SharedInventory.Create("heal",3));
                world.actors.Add(npc);
            }
            var container=new ContainerState();world.containers.Add(container);
            if(options.Smoke.StartsWith("shortcuts")) container.x=-5;
            // The old container position overlaps the subsequently authored exit.
            // UI transaction probes need an independently reachable E target.
            if(options.Smoke.StartsWith("container")) container.x=1;
            container.items.Add(SharedInventory.Create("caps",20));
            container.items.Add(SharedInventory.Create("heal",3));
            container.items.Add(SharedInventory.Create("grenade",3));
            world.props.Add(PropCatalog.Create("prop:crate-1","mcrate1","factory:0",new Vector2(-3,.8f)));
            runtime.Session.Saves.SaveWorld(world);
            Debug.Log("Регрессионный сценарий загружает старый тестовый мир: "+options.Smoke);
        }
    }
}
