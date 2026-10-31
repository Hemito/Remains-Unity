using UnityEngine;

namespace Remains.Domain
{
    public static class HealingRules
    {
        public const float RevivalSeconds=1.5f,Revived=35;
        public static bool CanApply(ActorState healer,ActorState target,bool selfHeal)=>Rejection(healer,target,selfHeal)==null;
        // A player heals itself and revives other players; a sapient NPC (a raider) revives its own kind — with a medkit
        // either way (user decision 2026-10-31: the actions are shared, NPCs carry medkits). NPCs do not heal themselves yet.
        public static string Rejection(ActorState healer,ActorState target,bool selfHeal)
        {
            bool npc=healer!=null && !healer.player;
            if(healer==null || (npc?!ActorPresence.IsPresent(healer) || !SapientPonies.Is(healer):!healer.connected)) return "Персонаж не подключён.";
            if(healer.down) return "Без сознания нельзя использовать аптечку.";
            if(npc && selfHeal) return "NPC пока не лечат себя.";
            if(target==null || (npc?target.player || !ActorPresence.IsPresent(target) || !SapientPonies.Is(target):!target.player || !target.connected))
                return "Получатель лечения недоступен.";
            if(healer.level!=target.level) return "Получатель лечения в другой области.";
            if(selfHeal)
            {
                if(target.id!=healer.id) return "Для самолечения выберите своего персонажа.";
                if(target.health>=PlayerRadiation.HealthLimit(target))
                    return target.radiation>0?"Здоровье полное с учётом радиации.":"Здоровье уже полное.";
            }
            else if(!target.down || target.id==healer.id) return npc?"Поднять можно только другого лежащего союзника.":"Поднять можно только другого игрока без сознания.";
            if(Vector2.Distance(healer.Position,target.Position)>2) return "Подойдите ближе к союзнику.";
            if(InventoryJournal.Count(healer,"heal")<=0) return "Нет доступной аптечки.";
            return null;
        }
        // Canonical coordinator entry point; legacy local Apply overloads remain available.
        public static bool ApplyOnce(WorldState world,ActorState healer,ActorState target,bool selfHeal,string itemId,string transaction)
        {
            if(world==null || string.IsNullOrWhiteSpace(transaction) || !world.actors.Contains(healer) || !world.actors.Contains(target)) return false;
            if(world.completedTransactions.Contains(transaction)) return true;
            if(!Apply(healer,target,selfHeal,itemId)) return false;
            world.completedTransactions.Add(transaction);world.revision++;return true;
        }
        public static bool Apply(ActorState healer,ActorState target,bool selfHeal)
            =>Apply(healer,target,selfHeal,null);
        public static bool Apply(ActorState healer,ActorState target,bool selfHeal,string itemId)
        {
            if(!CanApply(healer,target,selfHeal) || !SharedInventory.Consume(healer,"heal",itemId)) return false;
            // The health limit is maxhp − rad; a revived player's radiation is cut to RevivedLimit by its owner. An NPC gets up
            // with the same 35, within its factory level's maximum when it has one.
            float limit=!target.player?(target.factoryEnemy?.maxHealth>0?target.factoryEnemy.maxHealth:float.MaxValue):
                selfHeal?PlayerRadiation.HealthLimit(target):PlayerRadiation.RevivedHealthLimit(target);
            target.health=Mathf.Min(limit,target.health+Revived);target.down=false;
            if(!selfHeal) ActorPose.Select(target,"idle");
            target.lifecycleSequence++;
            target.revision++;
            if(target!=healer) healer.revision++;
            return true;
        }
    }
}
