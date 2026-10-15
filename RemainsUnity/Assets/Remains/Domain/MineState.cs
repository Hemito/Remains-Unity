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
        public Vector2 Position=>new(x,y);
    }

    public static class MineRules
    {
        public const string LevelSide="level",PlayerSide="player";
        // Original Mine.control: hostile units are checked every 4th frame at 30 fps.
        public const float SensePeriod=4f/30;

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
                !float.IsFinite(mine.x) || !float.IsFinite(mine.y) || !float.IsFinite(mine.remaining) || mine.remaining<0)
                throw new InvalidOperationException($"Некорректная мина: id={mine?.id}, level={mine?.level}, definition={mine?.definition}, side={mine?.side}.");
        }

        // Whether this actor sets the mine off (Mine.control): a present, standing actor of the hostile side inside the
        // zone sensitivity wide on each side, up to sensitivity above and 0.4 × sensitivity below the mine. The
        // original lets small creatures (acttrap=1: rats, roaches) pass level mines; until creature kinds carry that
        // flag, level mines react to players only.
        public static bool Senses(MineState mine,ActorState actor,MineProfile profile)
        {
            if(actor==null || actor.level!=mine.level || !ActorPresence.IsPresent(actor) || actor.down || actor.health<=0) return false;
            bool hostile=mine.side==LevelSide?actor.player:!actor.player;
            if(!hostile) return false;
            float dx=actor.x-mine.x,dy=actor.y-mine.y,s=profile.sensitivity;
            return dx>-s && dx<s && dy<s && dy>-.4f*s;
        }

        // Starts the countdown; true when this changed the mine.
        public static bool Trigger(MineState mine,MineProfile profile)
        {
            if(mine.triggered) return false;
            mine.triggered=true;mine.remaining=profile.delaySeconds;return true;
        }

        public static AttackEvent Attack(MineState mine,MineProfile profile)=>new()
        {
            id=mine.id,attacker=mine.author??mine.id,level=mine.level,sourcePlayer=mine.side==PlayerSide,weapon=mine.definition,
            // The blast centre is the middle of the mine's body.
            originX=mine.x,originY=mine.y+profile.height*.5f,endX=mine.x,endY=mine.y+profile.height*.5f
        };

        // Interim rules (decisions.md, 2026-10-15): a mine is seen within its profile's visibility radius (300 px of the
        // original) and disarming always succeeds; the original's skill checks come later.
        public const float DisarmRadius=2f;
        // The pointer target of a tiny mine: its body, enlarged to a usable minimum around the bottom centre.
        public static Rect Bounds(MineState mine,MineProfile profile)
        {
            float width=Mathf.Max(profile.width,.6f),height=Mathf.Max(profile.height,.4f);
            return new Rect(mine.x-width*.5f,mine.y,width,height);
        }
        public static bool Visible(MineState mine,ActorState viewer,MineProfile profile)=>
            mine.triggered || mine.side==PlayerSide || viewer!=null && viewer.level==mine.level &&
            Vector2.Distance(viewer.Position,mine.Position)<=profile.visibility;
        public static string DisarmRejection(MineState mine,ActorState actor,MineProfile profile)
        {
            if(mine==null) return "Мина больше недоступна.";
            if(actor==null || actor.down || !actor.connected || actor.health<=0) return "Обезвреживание недоступно: персонаж не может действовать.";
            if(actor.level!=mine.level || !Visible(mine,actor,profile)) return "Мина не видна.";
            if(mine.triggered) return "Мина уже сработала.";
            if(Vector2.Distance(actor.Position,mine.Position)>DisarmRadius) return "Подойдите ближе к мине.";
            return null;
        }

        // Removes the mine and records its explosion like a grenade's (GrenadeRules.Detonate): every present player of the
        // level decides its own contact; the host lists the non-player actors it hit.
        public static ExplosionState Detonate(WorldState world,MineState mine,IEnumerable<string> enemyHits)
        {
            if(!world.mines.Contains(mine)) return null;
            var explosion=new ExplosionState {attack=Attack(mine,Profile(mine)),friendlyFire=world.friendlyFire,stationary=true};
            explosion.players=world.actors.Where(a=>a.player && ActorPresence.IsPresent(a) && a.level==mine.level).Select(a=>a.id).ToList();
            var hits=new HashSet<string>(enemyHits??Array.Empty<string>(),StringComparer.Ordinal);
            explosion.enemies=world.actors.Where(a=>!a.player && a.level==mine.level && hits.Contains(a.id)).Select(a=>a.id).ToList();
            world.mines.Remove(mine);world.explosions.Add(explosion);
            return explosion;
        }
    }
}
