using UnityEngine;

namespace Remains.Domain
{
    public static class HealingRules
    {
        public static bool Apply(ActorState healer,ActorState target,bool selfHeal)
        {
            if(healer.down || target==null || !target.player || !target.connected || healer.level!=target.level) return false;
            if(selfHeal ? target.id!=healer.id || target.health>=100 : !target.down || target.id==healer.id) return false;
            if(Vector2.Distance(healer.Position,target.Position)>2 || !SharedInventory.Consume(healer,"heal")) return false;
            target.health=Mathf.Min(100,target.health+35);target.down=false;ActorPose.Select(target,"idle");
            target.lifecycleSequence++;
            return true;
        }
    }
}
