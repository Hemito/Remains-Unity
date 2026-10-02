using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public static class FactoryRaiderContent
    {
        public const string Patch="base:factory-raider-2";
        public static bool NeedsInstall(WorldState world)=>!world.contentPatches.Contains(Patch);
        public static void Install(WorldState world)
        {
            if(!NeedsInstall(world)) return;
            ActorTypes.Get("raider");
            var actor=world.Find("npc:training-guard");
            if(actor!=null && !actor.player && actor.actorType=="pony") actor.actorType="raider";
            if(actor!=null && !actor.player && actor.actorType=="raider" && actor.anatomy=="pony") actor.anatomy="raider";
            // Existing position, death, ownership, inventory and attack state remain saved gameplay state.
            world.contentPatches.Add(Patch);world.revision++;
            Debug.Log($"Обновлён внешний вид охранника: world={world.worldId}, actor={actor?.id}, type={actor?.actorType}, patch={Patch}.");
        }
    }
}
