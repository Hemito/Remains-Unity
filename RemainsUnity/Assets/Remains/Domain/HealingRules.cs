using UnityEngine;

namespace Remains.Domain
{
    public static class HealingRules
    {
        public const float RevivalSeconds=1.5f;
        public static bool CanApply(ActorState healer,ActorState target,bool selfHeal)=>Rejection(healer,target,selfHeal)==null;
        public static string Rejection(ActorState healer,ActorState target,bool selfHeal)
        {
            if(healer==null || !healer.connected) return "Персонаж не подключён.";
            if(healer.down) return "Без сознания нельзя использовать аптечку.";
            if(target==null || !target.player || !target.connected) return "Получатель лечения недоступен.";
            if(healer.level!=target.level) return "Получатель лечения в другой области.";
            if(selfHeal)
            {
                if(target.id!=healer.id) return "Для самолечения выберите своего персонажа.";
                if(target.health>=100) return "Здоровье уже полное.";
            }
            else if(!target.down || target.id==healer.id) return "Поднять можно только другого игрока без сознания.";
            if(Vector2.Distance(healer.Position,target.Position)>2) return "Подойдите ближе к союзнику.";
            if(InventoryJournal.Count(healer,"heal")<=0) return "Нет доступной аптечки.";
            return null;
        }
        public static bool Apply(ActorState healer,ActorState target,bool selfHeal)
            =>Apply(healer,target,selfHeal,null);
        public static bool Apply(ActorState healer,ActorState target,bool selfHeal,string itemId)
        {
            if(!CanApply(healer,target,selfHeal) || !SharedInventory.Consume(healer,"heal",itemId)) return false;
            target.health=Mathf.Min(100,target.health+35);target.down=false;ActorPose.Select(target,"idle");
            target.lifecycleSequence++;
            target.revision++;
            if(target!=healer) healer.revision++;
            return true;
        }
    }
}
