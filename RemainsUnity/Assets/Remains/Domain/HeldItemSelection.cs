namespace Remains.Domain
{
    // The held reference is a selection, never a second copy of an inventory stack.
    public static class HeldItemSelection
    {
        public static bool HasSelection(ActorState actor)=>!string.IsNullOrEmpty(actor?.heldItemId);
        public static ItemStack Selected(ActorState actor)=>HasSelection(actor)?InventoryStacks.Find(actor,actor.heldItemId):null;
        public static bool Select(ActorState actor,string itemId,out string reason)
        {
            reason=null;
            if(actor==null || actor.weaponInventoryVersion!=1) {reason="Инвентарь предметов ещё не подготовлен.";return false;}
            var item=InventoryStacks.Find(actor,itemId);
            if(item==null || WeaponInventory.IsEquippable(item)) {reason="Этот предмет не является обычной удерживаемой вещью.";return false;}
            if(InventoryAvailability.Count(actor,item)<=0) {reason="Предмет зарезервирован для передачи или закончился.";return false;}
            actor.heldItemId=item.id;actor.heldItemPoseValid=false;actor.weaponLift=0;
            actor.weaponSelectionRevision++;actor.revision++;return true;
        }
        public static void Clear(ActorState actor)
        {actor.heldItemId=null;actor.heldItemPoseValid=false;actor.weaponLift=0;}
        public static void CopyPose(ActorState source,ActorState destination)
        {
            if(source.weaponSelectionRevision!=destination.weaponSelectionRevision) return;
            destination.heldItemPoseX=source.heldItemPoseX;destination.heldItemPoseY=source.heldItemPoseY;
            destination.heldItemPoseValid=source.heldItemPoseValid;
        }
    }
}
