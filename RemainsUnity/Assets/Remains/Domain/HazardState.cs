using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // A trap or an explosive charge of the original (UnitTrap; UnitDamager with un tip 3), the editor's "mine" group
    // beside the mines: a fixed object the host simulates. Its definition is the original unit (HazardCatalog).
    //   trap   (mtrap):    an open trap snaps on a standing actor of the other side that springs traps (Unit.activateTrap
    //                      2) and bites it; the snap breaks the trap (UnitTrap.control: 35 inner damage to itself against
    //                      its 10 health, no corpse). Disarming an open trap snaps it harmlessly; a closed trap can be
    //                      rearmed, then it is the player's and catches other actors.
    //   charge (damexpl1): explodes when it is destroyed (UnitDamager.dropLoot → iExpl); disarming removes it and leaves
    //                      three sticks of dynamite (LootGen.lootCont "bomb").
    [Serializable]
    public sealed class HazardState : Remains.Serialization.ExtensibleState
    {
        public FactoryEnemyScaleState factoryEnemy;
        public string id,level,definition,side;
        public string author;      // the player who rearmed a trap (null for a level hazard)
        public float x,y;          // bottom centre on its support
        public float health;
        public bool closed;        // a trap that snapped shut by disarming and waits to be rearmed
        public Vector2 Position=>new(x,y);
    }

    public static class HazardRules
    {
        public const string Trap="trap",Charge="charge";
        // Original UnitTrap.control: the actors are checked every 5th frame at 30 fps.
        public const float TrapSensePeriod=5f/30;
        // Original Unit.activateTrap: 2 springs traps (players, most units); 1 (small creatures) and 0 do not.
        public const int SpringsTraps=2;

        public static HazardDefinition Profile(HazardState hazard)=>HazardCatalog.Get(hazard.definition);
        public static bool IsTrap(HazardState hazard)=>Profile(hazard).kind==Trap;

        public static void Validate(HazardState hazard)
        {
            if(hazard==null || string.IsNullOrWhiteSpace(hazard.id) || string.IsNullOrWhiteSpace(hazard.level) || string.IsNullOrWhiteSpace(hazard.definition) ||
                hazard.side!=MineRules.LevelSide && hazard.side!=MineRules.PlayerSide || hazard.side==MineRules.PlayerSide && string.IsNullOrWhiteSpace(hazard.author) ||
                !float.IsFinite(hazard.x) || !float.IsFinite(hazard.y) || !float.IsFinite(hazard.health) || hazard.health<=0)
                throw new InvalidOperationException($"Некорректная ловушка: id={hazard?.id}, level={hazard?.level}, definition={hazard?.definition}, side={hazard?.side}, health={hazard?.health}.");
        }

        // A level hazard as placed by the level: full health, open.
        public static HazardState Create(string id,string level,HazardDefinition definition,Vector2 position)
        {
            var hazard=new HazardState {id=id,level=level,definition=definition.id,side=MineRules.LevelSide,x=position.x,y=position.y,health=definition.health};
            Validate(hazard);
            return hazard;
        }

        // The body (original sX × sY standing on the bottom centre): what a trap touches, what attacks hit.
        public static Rect Body(HazardState hazard,HazardDefinition profile)=>new(hazard.x-profile.width*.5f,hazard.y,profile.width,profile.height);
        public static Vector2 Center(HazardState hazard,HazardDefinition profile)=>hazard.Position+Vector2.up*(profile.height*.5f);
        // The pointer target: the body enlarged to a usable minimum (as a mine's).
        public static Rect Bounds(HazardState hazard,HazardDefinition profile)
        {
            float width=Mathf.Max(profile.width,.6f),height=Mathf.Max(profile.height,.4f);
            return new Rect(hazard.x-width*.5f,hazard.y,width,height);
        }

        // Hidden until noticed (UnitTrap/UnitDamager setVis(false), visibility 300 px) by the mines' interim rule; a
        // player's trap is always seen.
        public static bool Visible(HazardState hazard,ActorState viewer,HazardDefinition profile)=>
            hazard.side==MineRules.PlayerSide || MineRules.Within(hazard.level,hazard.Position,viewer,profile.visibility);

        // The player's action on it: an open trap is disarmed (snaps), a closed one rearmed, a charge disarmed.
        public static string Action(HazardState hazard,HazardDefinition profile)=>profile.kind==Trap && hazard.closed?"Взвести":"Обезвредить";

        // Interim rule (decisions.md, 2026-10-15, as mines): the action always succeeds; the original's skill checks
        // (repair 1 to disarm, 3 to rearm a trap; explosives for a charge) come with skills.
        public static string DisarmRejection(HazardState hazard,ActorState actor,HazardDefinition profile)
        {
            if(hazard==null) return "Ловушка больше недоступна.";
            if(actor==null || actor.down || !actor.connected || actor.health<=0) return "Действие недоступно: персонаж не может действовать.";
            if(actor.level!=hazard.level || !Visible(hazard,actor,profile)) return profile.title+": не видно.";
            if(Vector2.Distance(actor.Position,hazard.Position)>MineRules.DisarmRadius) return "Подойдите ближе: "+profile.title.ToLowerInvariant()+".";
            return null;
        }

        // Whether this actor springs the trap (UnitTrap.control → attKorp): a present, standing actor of the other side
        // that springs traps, whose body overlaps the trap's. A level trap catches players; the original also lets it
        // catch other creatures that spring traps, which needs NPC-to-NPC damage first (the factory's creatures are all
        // small: acttrap 1). A player's trap catches non-player actors that spring traps.
        public static bool Springs(HazardState trap,ActorState actor,HazardDefinition profile)
        {
            if(trap.closed || actor==null || actor.level!=trap.level || !ActorPresence.IsPresent(actor) || actor.down || actor.health<=0) return false;
            if(trap.side==MineRules.LevelSide?!actor.player:actor.player) return false;
            if(ActorTypes.Get(actor.actorType).activateTrap<SpringsTraps) return false;
            var hull=Anatomy.Movement(actor);
            return Body(trap,profile).Overlaps(new Rect(actor.x-hull.width*.5f,actor.y,hull.width,hull.height));
        }

        // The bite (Unit.udarUnit by the trap): a contact from the trap to the middle of its victim's body, so the
        // victim's own hit regions decide it as any attack; the damage is the bite weapon's (AllData comb damage).
        public static AttackEvent Bite(HazardState trap,HazardDefinition profile,ActorState victim,string owner)
        {
            var start=Center(trap,profile);var end=victim.Position+Vector2.up*(Anatomy.Movement(victim).height*.5f);
            return new AttackEvent {id=Guid.NewGuid().ToString("N"),attacker=trap.id,level=trap.level,sourceOwner=owner,sourcePlayer=trap.side==MineRules.PlayerSide,
                weapon=profile.biteWeapon,factoryEnemy=FactoryEnemyScaling.Capture(trap.factoryEnemy),originX=start.x,originY=start.y,endX=end.x,endY=end.y,radius=.1f};
        }

        // Damage taken (Unit.damage): the skin is subtracted from every hit. True when this destroyed the hazard.
        public static bool Damage(HazardState hazard,HazardDefinition profile,float amount)
        {
            if(!float.IsFinite(amount) || amount<0) throw new InvalidOperationException($"Некорректный урон ловушке {hazard.id}: {amount}.");
            hazard.health=Mathf.Max(0,hazard.health-Mathf.Max(0,amount-profile.skin*FactoryEnemyScaling.Protection(hazard.factoryEnemy)));
            return hazard.health<=0;
        }

        // A destroyed charge's explosion (UnitDamager.iExpl: from the middle of its body, as its blast weapon).
        public static ExplosionState Detonate(WorldState world,HazardState charge,IEnumerable<string> enemyHits)
        {
            if(!world.hazards.Contains(charge)) return null;
            world.hazards.Remove(charge);
            return ExplosionRules.Record(world,Blast(charge,Profile(charge)),enemyHits,true);
        }
        public static AttackEvent Blast(HazardState charge,HazardDefinition profile)
        {
            var centre=Center(charge,profile);
            return new AttackEvent {id=charge.id,attacker=charge.author??charge.id,level=charge.level,sourcePlayer=charge.side==MineRules.PlayerSide,
                weapon=profile.blastWeapon,factoryEnemy=FactoryEnemyScaling.Capture(charge.factoryEnemy),originX=centre.x,originY=centre.y,endX=centre.x,endY=centre.y};
        }
        // Whether an explosion reaches the hazard: its body within the blast weapon's range of the blast centre.
        public static bool InBlast(HazardState hazard,HazardDefinition profile,AttackEvent blast)
        {
            if(hazard.level!=blast.level) return false;
            var body=Body(hazard,profile);var centre=blast.End;
            var nearest=new Vector2(Mathf.Clamp(centre.x,body.xMin,body.xMax),Mathf.Clamp(centre.y,body.yMin,body.yMax));
            return Vector2.Distance(nearest,centre)<=WeaponCatalog.Get(blast.weapon).range;
        }
    }
}
