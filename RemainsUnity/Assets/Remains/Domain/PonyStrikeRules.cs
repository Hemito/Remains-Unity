using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // The body strike of a sapient pony (original WKick, UnitPlayer.keyPunch): a punch ahead, or with the run key a kick
    // behind — the same action for a player and a raider (user decision 2026-10-31: the actions are shared by players and
    // sapient ponies, not by zombies, robots or animals). Its numbers and shape are the weapon definitions Punch and Kick.
    public static class PonyStrikeRules
    {
        public const string Punch="punch",Kick="kick";
        public static string Weapon(bool kick)=>kick?Kick:Punch;

        // A player, or a raider of the raiders' mind (not the robobrain or a zombie on the same brain).
        public static bool Allowed(ActorState actor)
        {
            if(actor.player) return true;
            var raider=ActorTypes.Get(actor.actorType).raider;
            return raider!=null && !raider.robot && !raider.zombie;
        }

        // UnitPlayer: standing on its support (stay), not crouched, climbing or down.
        public static bool CanBegin(ActorState actor,RoomLayout room)=>actor!=null && room!=null && Allowed(actor) && ActorPresence.IsPresent(actor) &&
            !actor.down && !actor.climbing && !actor.crouched && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);

        // The contact segments, outward from the body so that a wall stops them (WKick binds them from the far end).
        public static void Segments(ActorState actor,WeaponDefinition weapon,List<(Vector2 start,Vector2 end)> result)
        {
            var strike=weapon.strike;
            if(strike==null || !strike.Configured) throw new InvalidOperationException($"Оружие {weapon.id} не удар телом: actor={actor.id}.");
            result.Clear();
            float side=(actor.facing<0?-1:1)*(strike.behind?-1:1);var body=actor.BodyPosition;
            foreach(float height in strike.heights)
                result.Add((body+new Vector2(side*strike.near,height),body+new Vector2(side*strike.far,height)));
        }

        // Whether a target stands in the reach of the punch (ahead) or the kick (behind).
        public static bool InReach(ActorState actor,ActorState target,bool kick)
        {
            if(target==null || target.level!=actor.level || !ActorPresence.IsPresent(target)) return false;
            var strike=WeaponCatalog.Get(Weapon(kick)).strike;
            float dx=(target.x-actor.x)*(actor.facing<0?-1:1)*(kick?-1:1);
            float top=0;foreach(float height in strike.heights) top=Mathf.Max(top,height);
            return dx>=0 && dx<=strike.far && Mathf.Abs(target.y-actor.y)<=top;
        }
    }
}
