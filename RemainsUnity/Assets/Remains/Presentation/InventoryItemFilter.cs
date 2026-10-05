using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;

namespace Remains.Presentation
{
    // Local presentation state only; never serialized into a character or network message.
    public enum InventoryCategory { All, Weapons, Ammunition, Items, Armor }
    public sealed class InventoryItemFilter
    {
        private readonly HashSet<string> ammunition=new(StringComparer.Ordinal);
        public InventoryItemFilter(IEnumerable<WeaponDefinition> weapons)
        {
            foreach(var weapon in weapons)
                if(!string.IsNullOrEmpty(weapon.ammo)) ammunition.Add(weapon.ammo);
        }
        public bool Includes(ItemStack item,InventoryCategory category)=>category switch
        {
            InventoryCategory.All=>true,
            InventoryCategory.Weapons=>WeaponInventory.IsEquippable(item),
            InventoryCategory.Ammunition=>!WeaponInventory.IsEquippable(item) && ammunition.Contains(item.definition),
            InventoryCategory.Items=>!WeaponInventory.IsEquippable(item) && !ammunition.Contains(item.definition),
            InventoryCategory.Armor=>false, // Reserved presentation tab; armor rules are not implemented.
            _=>throw new ArgumentOutOfRangeException(nameof(category),category,"Неизвестная категория инвентаря.")
        };
    }
}
