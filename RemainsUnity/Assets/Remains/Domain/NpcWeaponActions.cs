using System;
using Remains.Content;

namespace Remains.Domain
{
    [Serializable] public sealed class NpcWeaponActionState : Remains.Serialization.ExtensibleState
    {
        public string kind,item,weapon;
        public long selectionRevision;
    }
    public interface INpcWeaponTimingRule
    {
        float Preparation(ActorState actor,WeaponDefinition weapon);
        float Recovery(ActorState actor,WeaponDefinition weapon);
    }
    public sealed class GuardWeaponTiming : INpcWeaponTimingRule
    {
        public float Preparation(ActorState actor,WeaponDefinition weapon)=>NpcAttackRules.PreparationSeconds;
        public float Recovery(ActorState actor,WeaponDefinition weapon)=>Math.Max(NpcAttackRules.CooldownSeconds,weapon.cooldown);
    }
    public static class NpcWeaponActions
    {
        public const string Shot="shot",Reload="reload";
        public static void ValidateWorld(WorldState world)
        {
            foreach(var actor in world.actors)
                if(!actor.player && !actor.down && ActorTypes.Get(actor.actorType) is {combatAi:true,hasWeapon:true}) ValidateEquipped(actor);
        }
        public static ItemStack ValidateEquipped(ActorState actor)
        {
            if(string.IsNullOrEmpty(actor.weapon)) return null;
            if(actor.weaponInventoryVersion!=1)
                throw new InvalidOperationException($"Оружие NPC не подготовлено: actor={actor.id}, type={actor.actorType}, inventoryVersion={actor.weaponInventoryVersion}.");
            var item=WeaponInventory.Selected(actor);
            if(item?.weaponState==null || item.definition!=actor.weapon)
                throw new InvalidOperationException($"Нет выбранного предмета оружия NPC: actor={actor.id}, item={actor.equippedWeaponItem}, weapon={actor.weapon}.");
            var weapon=WeaponCatalog.Get(item.definition);
            if(!WeaponRigs.Get(weapon.id).HasMuzzle)
                throw new InvalidOperationException($"Стрелковый AI NPC не поддерживает выбранное оружие: actor={actor.id}, item={item.id}, weapon={weapon.id}.");
            if(item.weaponState.rounds<0 || item.weaponState.rounds>weapon.magazineSize || item.weaponState.revision<0)
                throw new InvalidOperationException($"Неверное состояние предмета оружия NPC: actor={actor.id}, item={item.id}, weapon={weapon.id}, rounds={item.weaponState.rounds}, revision={item.weaponState.revision}.");
            return item;
        }
        public static NpcWeaponActionState Prepare(ActorState actor)
        {
            var item=ValidateEquipped(actor);if(item==null || !WeaponInventory.Available(actor,item.definition)) return null;
            var weapon=WeaponCatalog.Get(item.definition);
            string kind=Shot;
            if(!string.IsNullOrEmpty(weapon.ammo) && item.weaponState.rounds<weapon.ammoPerShot)
            {
                if(WeaponAmmunition.ReloadAmount(actor,weapon)==0) return null;
                kind=Reload;
            }
            else if(actor.mana<weapon.manaCost) return null;
            return new NpcWeaponActionState {kind=kind,item=item.id,weapon=weapon.id,selectionRevision=actor.weaponSelectionRevision};
        }
        public static bool StillSelected(ActorState actor,NpcWeaponActionState action)=>action!=null &&
            action.selectionRevision==actor.weaponSelectionRevision && WeaponInventory.Selected(actor) is { } item &&
            item.id==action.item && item.definition==action.weapon && actor.weapon==action.weapon && WeaponInventory.Available(actor,action.weapon);
        public static bool SameBinding(NpcWeaponActionState a,NpcWeaponActionState b)=>a==null?b==null:
            b!=null && a.kind==b.kind && a.item==b.item && a.weapon==b.weapon && a.selectionRevision==b.selectionRevision;
        public static float ValidSeconds(float value,string context)
        {
            if(!float.IsFinite(value) || value<0) throw new InvalidOperationException("Некорректное время действия NPC: "+context);
            return value;
        }
    }
}
