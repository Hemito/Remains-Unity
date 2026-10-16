using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // A mine in the world (original Mine.as): its weapon definition (hmine, mine...), the side it belongs to and its
    // trigger state. The host simulates mines; clients draw them. A level mine is hostile to players; a player's mine
    // to non-player actors.
    [Serializable]
    public sealed class MineState : Remains.Serialization.ExtensibleState
    {
        public string id,level,definition,side;
        public string author;          // the player actor who placed it (null for a level mine)
        public float x,y;              // bottom centre on its support
        public bool triggered;
        public float remaining;        // seconds to the explosion once triggered
        public float arming;           // a placed mine's seconds until it senses (0 for a level mine)
        public string holder;          // the player actor holding it in telekinesis (null when lying)
        public Vector2 Position=>new(x,y);
    }

    public static class MineRules
    {
        public const string LevelSide="level",PlayerSide="player";
        // Original Mine.control: hostile units are checked every 4th frame at 30 fps.
        public const float SensePeriod=4f/30;
        // Original WThrow.shoot for a placed mine (throwtip 1): it arms for 75 frames, and its countdown is 0.3 of the
        // definition's once triggered.
        public const float PlacedArmingSeconds=75f/30,PlacedDelayFactor=.3f;
        // How far below its release point a placed mine may fall onto a support.
        // The hold point is within the telekinesis reach around the body; PlaceReach bounds it generously on the host.
        public const float PlaceDrop=6f,PlaceReach=3f;
        // Original Mine.control: an armed mine held in telekinesis sets itself off with 10% every 10 frames at 30 fps.
        public const float HeldTriggerPeriod=10f/30,HeldTriggerChance=.1f;
        public static bool CanTriggerHeld(MineState mine)=>mine.holder!=null && !mine.triggered && mine.arming<=0;
        // The common telekinesis rules (reach, mana, mass, sight) see a mine as this light body; it never enters WorldProps.
        public static PropState TelekinesisBody(MineState mine,MineProfile profile)=>new()
        {
            id=mine.id,level=mine.level,x=mine.x,y=mine.y,mass=profile.mass,movable=true,hasPhysicalBody=true,
            width=profile.width,height=profile.height,anchorHeight=profile.height*.5f
        };
        // Where a released mine (its bottom centre) comes to rest: straight down onto the first support within PlaceDrop.
        public static bool TryDrop(RoomLayout room,Vector2 bottom,MineProfile profile,out Vector2 point)
        {
            var hull=new MovementHull {width=profile.width,height=profile.height};
            var start=bottom+Vector2.up*.1f;
            point=ActorTerrainMotion.Move(room,hull,start,Vector2.down*(PlaceDrop+.1f),false,bodyTops:false);
            return start.y-point.y<PlaceDrop+.09f;
        }

        // The mine weapon an inventory item places: the runtime ID (hmine) or the source item of loot and disarming
        // (weapon:hmine:grade:0). Other grades and items place nothing.
        public static string PlacedWeapon(string itemDefinition)
        {
            if(string.IsNullOrEmpty(itemDefinition)) return null;
            string id=itemDefinition;
            if(id.StartsWith("weapon:",StringComparison.Ordinal))
            {
                const string grade=":grade:0";
                if(!id.EndsWith(grade,StringComparison.Ordinal)) return null;
                id=id.Substring(7,id.Length-7-grade.Length);
            }
            return WeaponCatalog.IsMine(id)?id:null;
        }

        public static MineProfile Profile(MineState mine)
        {
            var weapon=WeaponCatalog.Get(mine.definition);
            if(!weapon.explosive || weapon.mine==null || !weapon.mine.Configured)
                throw new InvalidOperationException($"Мина {mine.id}: оружие {mine.definition} не описано как мина.");
            return weapon.mine;
        }

        public static void Validate(MineState mine)
        {
            if(mine==null || string.IsNullOrWhiteSpace(mine.id) || string.IsNullOrWhiteSpace(mine.level) || string.IsNullOrWhiteSpace(mine.definition) ||
                mine.side!=LevelSide && mine.side!=PlayerSide || mine.side==PlayerSide && string.IsNullOrWhiteSpace(mine.author) ||
                !float.IsFinite(mine.x) || !float.IsFinite(mine.y) || !float.IsFinite(mine.remaining) || mine.remaining<0 ||
                !float.IsFinite(mine.arming) || mine.arming<0 || mine.arming>0 && (mine.side!=PlayerSide || mine.triggered))
                throw new InvalidOperationException($"Некорректная мина: id={mine?.id}, level={mine?.level}, definition={mine?.definition}, side={mine?.side}.");
        }

        // Whether this actor sets the mine off (Mine.control): a present, standing actor of the hostile side inside the
        // zone sensitivity wide on each side, up to sensitivity above and 0.4 × sensitivity below the mine. The
        // original lets small creatures (acttrap=1: rats, roaches) pass level mines; until creature kinds carry that
        // flag, level mines react to players only.
        public static bool Senses(MineState mine,ActorState actor,MineProfile profile)
        {
            if(mine.arming>0 || actor==null || actor.level!=mine.level || !ActorPresence.IsPresent(actor) || actor.down || actor.health<=0) return false;
            bool hostile=mine.side==LevelSide?actor.player:!actor.player;
            if(!hostile) return false;
            float dx=actor.x-mine.x,dy=actor.y-mine.y,s=profile.sensitivity;
            return dx>-s && dx<s && dy<s && dy>-.4f*s;
        }

        // Starts the countdown; true when this changed the mine.
        public static bool Trigger(MineState mine,MineProfile profile)
        {
            if(mine.triggered) return false;
            mine.triggered=true;mine.remaining=profile.delaySeconds*(mine.side==PlayerSide?PlacedDelayFactor:1);return true;
        }

        public static AttackEvent Attack(MineState mine,MineProfile profile)=>new()
        {
            id=mine.id,attacker=mine.author??mine.id,level=mine.level,sourcePlayer=mine.side==PlayerSide,weapon=mine.definition,
            // The blast centre is the middle of the mine's body.
            originX=mine.x,originY=mine.y+profile.height*.5f,endX=mine.x,endY=mine.y+profile.height*.5f
        };

        // Interim rules (decisions.md, 2026-10-15): a mine is seen within its profile's visibility radius (300 px of the
        // original) and disarming always succeeds; the original's skill checks come later.
        // Original World.actionDist (40000 px², 200 px): beyond the 2 m sensing zone, so a seen mine can be disarmed
        // without walking into it.
        public const float DisarmRadius=4f;
        // The pointer target of a tiny mine: its body, enlarged to a usable minimum around the bottom centre.
        public static Rect Bounds(MineState mine,MineProfile profile)
        {
            float width=Mathf.Max(profile.width,.6f),height=Mathf.Max(profile.height,.4f);
            return new Rect(mine.x-width*.5f,mine.y,width,height);
        }
        public static bool Visible(MineState mine,ActorState viewer,MineProfile profile)=>
            mine.triggered || mine.side==PlayerSide || mine.holder!=null || Within(mine.level,mine.Position,viewer,profile.visibility);
        // The interim noticing rule of hidden level objects (mines, traps, charges; original visibility 300 px): the viewer
        // is on the level within the radius. The original's perception check (Unit.lookInvis) comes with skills.
        public static bool Within(string level,Vector2 position,ActorState viewer,float radius)=>
            viewer!=null && viewer.level==level && Vector2.Distance(viewer.Position,position)<=radius;
        public static string DisarmRejection(MineState mine,ActorState actor,MineProfile profile)
        {
            if(mine==null) return "Мина больше недоступна.";
            if(actor==null || actor.down || !actor.connected || actor.health<=0) return "Обезвреживание недоступно: персонаж не может действовать.";
            if(actor.level!=mine.level || !Visible(mine,actor,profile)) return "Мина не видна.";
            if(mine.triggered) return "Мина уже сработала.";
            if(mine.holder!=null) return "Мину удерживают телекинезом.";
            if(Vector2.Distance(actor.Position,mine.Position)>DisarmRadius) return "Подойдите ближе к мине.";
            return null;
        }

        // Removes the mine and records its explosion (ExplosionRules.Record).
        public static ExplosionState Detonate(WorldState world,MineState mine,IEnumerable<string> enemyHits)
        {
            if(!world.mines.Contains(mine)) return null;
            world.mines.Remove(mine);
            return ExplosionRules.Record(world,Attack(mine,Profile(mine)),enemyHits,true);
        }
    }
}
