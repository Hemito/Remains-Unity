using System;
using Remains.Content;
using Remains.Domain;

namespace Remains.Editor
{
    // Explicit item state for domain fixtures; never called by production spawning.
    internal static class NpcWeaponTestItems
    {
        public static void Equip(ActorState actor,string weapon=null,int? rounds=null,int reserve=0)
        {
            weapon ??=actor.weapon;var definition=WeaponCatalog.Get(weapon);
            var item=SharedInventory.Create(weapon,1);item.weaponState=new WeaponItemState {rounds=rounds ?? definition.magazineSize};
            actor.inventory.Add(item);actor.weaponInventoryVersion=1;
            if(!WeaponInventory.Select(actor,item.id,out string reason)) throw new InvalidOperationException(reason);
            if(reserve>0) actor.inventory.Add(SharedInventory.Create(definition.ammo,reserve));
        }
        public static AttackEvent Finish(ActorState actor,ActorState target,RoomLayout room=null)
        {actor.attackRemaining=0;return NpcAttackRules.Complete(actor,target,room);}
    }
}
