using UnityEngine;

namespace Remains.Domain
{
    public static class HealingRules
    {
        public const float RevivalSeconds=1.5f;
        public static bool CanApply(ActorState healer,ActorState target,bool selfHeal)
        {
            if(healer==null || !healer.connected || healer.down || target==null || !target.player || !target.connected || healer.level!=target.level) return false;
            if(selfHeal ? target.id!=healer.id || target.health>=100 : !target.down || target.id==healer.id) return false;
            return Vector2.Distance(healer.Position,target.Position)<=2 && InventoryJournal.Count(healer,"heal")>0;
        }
        public static bool Apply(ActorState healer,ActorState target,bool selfHeal)
        {
            if(!CanApply(healer,target,selfHeal) || !SharedInventory.Consume(healer,"heal")) return false;
            target.health=Mathf.Min(100,target.health+35);target.down=false;ActorPose.Select(target,"idle");
            target.lifecycleSequence++;
            target.revision++;
            if(target!=healer) healer.revision++;
            return true;
        }
    }
}
