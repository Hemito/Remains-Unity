using System;
using Remains.Content;

namespace Remains.Domain
{
    public interface IDoorDamageRule
    {
        float Damage(DoorState door,AttackEvent impact,WeaponDefinition weapon);
    }
    // Temporary Unity balance: existing weapon base damage, without actor injury rules.
    public sealed class CatalogDoorImpactDamage : IDoorDamageRule
    {
        public float Damage(DoorState door,AttackEvent impact,WeaponDefinition weapon)
        {
            if(door==null || impact==null || weapon==null || impact.weapon!=weapon.id)
                throw new ArgumentException("Не совпали дверь, атака и определение оружия для урона объекту.");
            // The prototype weapons and the original's weapons connected from the export (ids "weapon:<id>:grade:<n>").
            if(weapon.id is not ("pistol" or "energy" or "crowbar" or "spear") && !(weapon.id.StartsWith("weapon:",StringComparison.Ordinal) && !weapon.explosive))
                throw new InvalidOperationException($"Нет правила урона закреплённой двери: door={door.id}, weapon={weapon.id}. Мод должен задать IDoorDamageRule.");
            if(!float.IsFinite(weapon.damage) || weapon.damage<=0)
                throw new InvalidOperationException($"Некорректный базовый урон: door={door.id}, weapon={weapon.id}, damage={weapon.damage}.");
            return weapon.damage;
        }
    }
}
