using Remains.Content;
namespace Remains.Domain
{
    // Current starting equipment, shared by explicit creation and first network admission.
    public static class PlayerStarterInventory
    {
        public static void Give(ActorState actor)
        {
            if(actor.inventory.Count!=0 || actor.weaponInventoryVersion!=0)
                throw new System.InvalidOperationException("Стартовое оснащение можно выдать только новому персонажу: "+actor.character);
            InventoryStacks.Add(actor,SharedInventory.Create("heal",5));
            InventoryStacks.Add(actor,SharedInventory.Create("grenade",5));
            string[] weapons={"spear","crowbar","pistol","energy"};
            for(int i=0;i<weapons.Length;i++)
            {
                var definition=WeaponCatalog.Get(weapons[i]);
                var item=SharedInventory.Create(definition.id,1);
                item.weaponState=new WeaponItemState {rounds=definition.magazineSize};
                actor.inventory.Add(item);actor.weaponShortcuts.Add(new WeaponShortcut {slot=(i+1).ToString(),item=item.id});
                if(actor.weapon==definition.id) actor.equippedWeaponItem=item.id;
                if(!string.IsNullOrEmpty(definition.ammo)) InventoryStacks.Add(actor,SharedInventory.Create(definition.ammo,definition.magazineSize*3));
            }
            actor.loadoutVersion=actor.weaponInventoryVersion=1;actor.weaponSelectionRevision=1;
        }
    }
}
