using System;
using Remains.Content;

namespace Remains.Domain
{
    [Serializable] public sealed class NpcWeaponActionState : Remains.Serialization.ExtensibleState
    {
        public string kind,item,weapon;
        public string naturalAttackType;
        public long selectionRevision;
    }
    public interface INpcWeaponTimingRule
    {
        float Preparation(ActorState actor,WeaponDefinition weapon);
        float Recovery(ActorState actor,WeaponDefinition weapon);
    }
    public sealed class GuardWeaponTiming : INpcWeaponTimingRule
    {
        // A creature's gun fires at the weapon's own rate (UnitTurret: attack every frame in aiState 3; UnitSpriteBot:
        // currentWeapon.attack() — both limited by rapid): one frame of preparation, the weapon cooldown as recovery.
        // Guards aim first and pause between shots.
        public float Preparation(ActorState actor,WeaponDefinition weapon)=>CreatureGun(actor)?1f/30:NpcAttackRules.PreparationSeconds;
        public float Recovery(ActorState actor,WeaponDefinition weapon)=>CreatureGun(actor)?Math.Max(1f/30,weapon.cooldown):Math.Max(NpcAttackRules.CooldownSeconds,weapon.cooldown);
        private static bool CreatureGun(ActorState actor)
        {string behavior=ActorTypes.Get(actor.actorType).behavior;return NpcCreatureBehaviours.Fixed(behavior) || NpcCreatureBehaviours.FlyingGun(behavior);}
    }
    public static class NpcWeaponActions
    {
        public const string Shot="shot",Reload="reload",Swing="swing";
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
            // A raider swings a melee weapon (UnitRaider attackerType 0/1); the other armed NPCs only shoot.
            bool swings=WeaponCatalog.IsMelee(weapon.id) && ActorTypes.Get(actor.actorType).behavior==NpcCreatureBehaviours.Raider;
            if(!WeaponRigs.Get(weapon.id).HasMuzzle && !swings)
                throw new InvalidOperationException($"AI NPC не поддерживает выбранное оружие: actor={actor.id}, item={item.id}, weapon={weapon.id}.");
            if(item.weaponState.rounds<0 || item.weaponState.rounds>weapon.magazineSize || item.weaponState.revision<0)
                throw new InvalidOperationException($"Неверное состояние предмета оружия NPC: actor={actor.id}, item={item.id}, weapon={weapon.id}, rounds={item.weaponState.rounds}, revision={item.weaponState.revision}.");
            return item;
        }
        public static NpcWeaponActionState Prepare(ActorState actor)
        {
            var item=ValidateEquipped(actor);if(item==null || !WeaponInventory.Available(actor,item.definition)) return null;
            var weapon=WeaponCatalog.Get(item.definition);
            string kind=WeaponCatalog.IsMelee(weapon.id)?Swing:Shot;
            if(weapon.UsesMagazine && item.weaponState.rounds<weapon.ammoPerShot)
            {
                if(WeaponAmmunition.ReloadAmount(actor,weapon)==0) return null;
                kind=Reload;
            }
            else if(actor.mana<weapon.manaCost) return null;
            return new NpcWeaponActionState {kind=kind,item=item.id,weapon=weapon.id,selectionRevision=actor.weaponSelectionRevision};
        }
        // Nothing to shoot and nothing to reload with: the selected firearm is spent (a melee weapon never is). An unprepared
        // inventory is not "spent": the weapon action itself rejects it loudly (ValidateEquipped) when one is wanted.
        public static bool Spent(ActorState actor)=>!string.IsNullOrEmpty(actor.weapon) && actor.weaponInventoryVersion==1 &&
            !WeaponCatalog.IsMelee(actor.weapon) && Prepare(actor)==null;
        public static bool StillSelected(ActorState actor,NpcWeaponActionState action)=>action!=null &&
            action.selectionRevision==actor.weaponSelectionRevision && WeaponInventory.Selected(actor) is { } item &&
            item.id==action.item && item.definition==action.weapon && actor.weapon==action.weapon && WeaponInventory.Available(actor,action.weapon);
        public static bool SameBinding(NpcWeaponActionState a,NpcWeaponActionState b)=>a==null?b==null:
            b!=null && a.kind==b.kind && a.item==b.item && a.weapon==b.weapon && a.selectionRevision==b.selectionRevision && a.naturalAttackType==b.naturalAttackType;
        public static float ValidSeconds(float value,string context)
        {
            if(!float.IsFinite(value) || value<0) throw new InvalidOperationException("Некорректное время действия NPC: "+context);
            return value;
        }
    }
}
