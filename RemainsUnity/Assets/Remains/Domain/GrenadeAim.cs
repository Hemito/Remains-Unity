using System;
using UnityEngine;

namespace Remains.Domain
{
    // A thrower's chosen throw: where the grenade leaves it, the launch velocity and where it is expected to burst; not
    // valid when no throw bursts both near the target and out of the thrower's own blast reach.
    public readonly struct GrenadeThrow
    {
        public readonly bool valid;public readonly string weapon;public readonly Vector2 origin,velocity,burst;
        public GrenadeThrow(string weapon,Vector2 origin,Vector2 velocity,Vector2 burst) {valid=true;this.weapon=weapon;this.origin=origin;this.velocity=velocity;this.burst=burst;}
        public static readonly GrenadeThrow None=default;
    }

    // How a non-player thrower aims a grenade. The original's raiders threw from a speed-by-distance guess and often
    // dropped it at their own feet; ours aim (user 2026-11-01). Candidate launch angles toward the target, each with the
    // speed of a drag-free arc through an aim point at or short of the target (the grenade rolls on), capped at the
    // players' maximum launch speed; every candidate is flown through GrenadeRules on a copy for its whole fuse, so walls,
    // ceilings, bounces and the roll count. The one bursting nearest the target wins, unless it bursts within the blast
    // reach of the thrower; none bursting within the blast reach of the target means no throw. Then the thrower's error is added and the throw flown again; an error that would burst on the
    // thrower falls back to the clean throw. Run once per throw by the thrower's owner, never per frame.
    public static class GrenadeAim
    {
        // Launch angles above the horizontal, degrees, and how far short of the target the arc is aimed, metres.
        private static readonly float[] Angles={-10,0,10,20,30,40,50,60,70};
        private static readonly float[] Short={0,1,2};
        // Beyond the blast reach the thrower keeps this much more between itself and the burst (the blast's damage is
        // already nothing at its radius: ExplosionRules.Damage).
        public const float SafetyMargin=.5f;
        // Nearer than this a target is inside the thrower's own blast: the grenade's reach and the margin.
        public static float Reach(string weapon)=>Remains.Content.WeaponCatalog.Get(weapon).range+SafetyMargin;
        // A burst this near the target ends the search early, metres; a grenade at rest is flown no further.
        private const float Close=1,RestChunk=.1f,RestMove=.01f,RestSpeed=.05f;

        // origin: where the grenade leaves the thrower; thrower and target: their body centres. errorDegrees: the angle
        // error's half-width; the speed error is the same share in percent (6° → ±6%).
        public static GrenadeThrow Plan(RoomLayout room,Vector2 origin,Vector2 thrower,Vector2 target,float errorDegrees,System.Random random,string weapon="grenade")
        {
            if(room==null || random==null || !float.IsFinite(errorDegrees) || errorDegrees<0 || !Finite(origin) || !Finite(thrower) || !Finite(target))
                throw new ArgumentException($"Прицел гранаты: некорректные данные (комната {room?.id}, ошибка {errorDegrees}, бросок {origin}→{target}).");
            var definition=Remains.Content.WeaponCatalog.Get(weapon);
            if(!definition.IsGrenade) throw new ArgumentException("Прицел гранаты: "+weapon+" не ручная граната.");
            float reach=Reach(weapon);
            if((target-thrower).sqrMagnitude<=reach*reach) return GrenadeThrow.None;   // the target is inside its own blast
            float side=target.x>=origin.x?1:-1;
            GrenadeThrow best=GrenadeThrow.None;float nearest=float.MaxValue;
            foreach(float shortBy in Short)
            {
                var aim=new Vector2(target.x-side*shortBy,target.y);
                foreach(float angle in Angles)
                {
                    if(!Launch(origin,aim,angle,side,definition.grenadeGravity,definition.grenadeMaxLaunchSpeed,out var velocity)) continue;
                    var burst=Burst(room,origin,velocity,weapon);
                    if((burst-thrower).sqrMagnitude<=reach*reach) continue;
                    float miss=(burst-target).sqrMagnitude;
                    if(miss<nearest) {nearest=miss;best=new GrenadeThrow(weapon,origin,velocity,burst);}
                }
                if(nearest<Close*Close) break;   // close enough: aiming shorter is not tried
            }
            // Nothing would burst within the blast reach of the target (a wall between, too far): no throw.
            if(!best.valid || nearest>definition.range*definition.range) return GrenadeThrow.None;
            if(errorDegrees==0) return best;
            float turn=((float)random.NextDouble()*2-1)*errorDegrees*Mathf.Deg2Rad;
            float speed=1+((float)random.NextDouble()*2-1)*errorDegrees/100;
            var erred=Rotate(best.velocity,turn)*speed;
            if(erred.magnitude>definition.grenadeMaxLaunchSpeed) erred=erred.normalized*definition.grenadeMaxLaunchSpeed;
            var landed=Burst(room,origin,erred,weapon);
            return (landed-thrower).sqrMagnitude<=reach*reach?best:new GrenadeThrow(weapon,origin,erred,landed);
        }

        // The speed of an arc at this angle through the aim point; capped at the maximum (it then falls short).
        private static bool Launch(Vector2 origin,Vector2 aim,float degrees,float side,float gravity,float maximum,out Vector2 velocity)
        {
            velocity=default;
            float dx=Mathf.Abs(aim.x-origin.x),dy=aim.y-origin.y,radians=degrees*Mathf.Deg2Rad;
            float cos=Mathf.Cos(radians),rise=dx*Mathf.Tan(radians)-dy;
            if(dx<.1f || rise<=0) return false;
            float speed=Mathf.Min(maximum,Mathf.Sqrt(gravity*dx*dx/(2*cos*cos*rise)));
            velocity=new Vector2(side*speed*cos,speed*Mathf.Sin(radians));
            return float.IsFinite(velocity.x) && float.IsFinite(velocity.y);
        }

        // Where a grenade thrown so bursts: the game's own flight (GrenadeRules) on a throwaway state, cut short once it
        // lies at rest (it then only settles by millimetres till the fuse ends).
        public static Vector2 Burst(RoomLayout room,Vector2 origin,Vector2 velocity,string weapon="grenade")
        {
            float fuse=Remains.Content.WeaponCatalog.Get(weapon).grenadeFuseSeconds;
            var flight=new GrenadeState {id="aim",weapon=weapon,x=origin.x,y=origin.y,vx=velocity.x,vy=velocity.y,remaining=fuse,rotationVersion=1};
            while(flight.remaining>0)
            {
                var before=flight.Position;
                GrenadeRules.Step(flight,room,Mathf.Min(RestChunk,flight.remaining));
                if((flight.Position-before).sqrMagnitude<RestMove*RestMove && Mathf.Abs(flight.vx)<RestSpeed) break;
            }
            return flight.Position;
        }

        private static Vector2 Rotate(Vector2 v,float radians)
        {
            float c=Mathf.Cos(radians),s=Mathf.Sin(radians);
            return new Vector2(v.x*c-v.y*s,v.x*s+v.y*c);
        }
        private static bool Finite(Vector2 v)=>float.IsFinite(v.x) && float.IsFinite(v.y);
    }
}
