using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;

namespace Remains.Domain
{
    // A splash of burning droplets in the world (FireRules): where and how it started; its patches follow from it the
    // same on every peer (the room geometry and the seed are shared), so only this travels and is saved.
    [Serializable]
    public sealed class FireState : Remains.Serialization.ExtensibleState
    {
        public string id,weapon,level,attacker;
        public bool sourcePlayer;
        public float x,y,vx,vy;   // where it broke and the bottle's velocity then; a grenade's burst has none; a torch's last motion
        public int seed;
        public bool torch;        // a bottle at rest unbroken: one patch at its neck, burning its fuel out
        public float fuel;        // droplet-seconds of fuel left (FireRules.Fuel): a torch's time, a splash's droplets
        public double start;      // the level's time (LevelTime) when it began
        // The host records motion changes and impacts, so an arriving peer uses
        // the same flight even when a door has since moved. Null is a legacy fire.
        public FireDropletPath[] dropletPaths;
        public double plannedDuration;
        public Vector2 Position=>new(x,y);
    }

    [Serializable]
    public struct FireFlightSegment
    {
        public int step;
        public float x,y,vx,vy;
        internal Vector2 Start=>new(x,y);
        internal FireFlightSegment(int step,Vector2 position,Vector2 velocity)
        {this.step=step;x=position.x;y=position.y;vx=velocity.x;vy=velocity.y;}
        internal Vector2 PositionAt(int frame)
        {
            float seconds=(frame-step)*FireRules.DropletStep;
            return Start+new Vector2(vx,vy)*seconds+Vector2.down*(.5f*FireRules.DropletGravity*seconds*seconds);
        }
    }

    [Serializable]
    public sealed class FireDropletPath
    {
        public FireFlightSegment[] segments;
        public int steps;
        public float endX,endY;
        public bool landed;
        public Vector2 End=>new(endX,endY);
        public double Arrival=>steps*(double)FireRules.DropletStep;

        public bool TryPosition(double age,out Vector2 position)
        {
            position=default;
            if(age<0 || age>=Arrival) return false;
            double frame=age/FireRules.DropletStep;
            int step=(int)frame,segment=0;
            while(segment+1<segments.Length && segments[segment+1].step<=step) segment++;
            var from=segments[segment].PositionAt(step);
            var to=step+1==steps?End:
                segment+1<segments.Length && segments[segment+1].step==step+1?segments[segment+1].Start:
                segments[segment].PositionAt(step+1);
            position=Vector2.LerpUnclamped(from,to,(float)(frame-step));
            return true;
        }
    }

    // A burning patch on a surface: its centre on the surface top and its radius.
    public readonly struct FirePatch
    {
        public readonly Vector2 position;public readonly float radius;
        public readonly double landedAt;
        public FirePatch(Vector2 position,float radius):this(position,radius,0) {}
        public FirePatch(Vector2 position,float radius,double landedAt)
        {this.position=position;this.radius=radius;this.landedAt=landedAt;}
        public bool IsBurning(double age,double seconds)=>age>=landedAt && age<landedAt+seconds;
    }

    // Fire (user 2026-11-02): instead of the original's pulses over a radius drawn one flame per tile top
    // (Bullet.explRun ×explkol, explLiquid), the splash flies as droplets with inertia — a bottle's keep its velocity,
    // a grenade's fly out evenly — and each droplet that lands burns a patch; only the patches hurt (a pulse every
    // pulseSeconds, once per pulse per fire for an actor touching any patch) and set burning. A bottle breaks on impact
    // by its speed into the surface (FireSplash.shatter). A bottle is lit when thrown (user 2026-11-02): it is a torch from
    // then, its fuel (droplets × a patch's time) burning from the neck; broken, what fuel is left splashes as that many
    // droplets (one per patch time, fewer the longer it burnt); at rest unbroken, it burns the rest as a torch with one
    // droplet's damage. It has no fuse.
    public static class FireRules
    {
        internal const float DropletStep=1f/60,DropletGravity=18;
        private const float MaxFlight=1.5f;
        private static readonly MovementHull Droplet=new() {width=.08f,height=.08f};
        // Above a patch an actor's feet touch it within this height.
        public const float Reach=.6f;

        // Host: a fire grenade or bottle at the end of its flight becomes a fire — droplets from where it broke (with the
        // velocity it hit with), from where it burst (radial), or a torch where an unbroken bottle lies.
        public static FireState Ignite(WorldState world,GrenadeState grenade,double now)
            =>Ignite(world,grenade,now,null);

        public static FireState Ignite(WorldState world,GrenadeState grenade,double now,RoomLayout room)
        {
            if(!world.grenades.Contains(grenade) || grenade.remaining>0) return null;
            var profile=WeaponCatalog.Get(grenade.weapon).fire;
            if(profile==null || !profile.Configured) throw new InvalidOperationException($"Граната {grenade.id}: {grenade.weapon} без огненных брызг.");
            // A bottle carries what fuel it has left (burnt from the neck since the throw); a grenade all of it.
            bool torch=profile.shatter && !grenade.shattered;float fuel=profile.shatter?grenade.fuel:Fuel(profile);
            if(fuel<=0) {world.grenades.Remove(grenade);world.revision++;return null;}
            var velocity=grenade.shattered?new Vector2(grenade.impactVx,grenade.impactVy):Vector2.zero;
            var fire=new FireState {id=grenade.id,weapon=grenade.weapon,level=grenade.level,attacker=grenade.attacker,sourcePlayer=grenade.sourcePlayer,
                x=grenade.x,y=grenade.y,vx=velocity.x,vy=velocity.y,seed=Seed(grenade.id),torch=torch,fuel=fuel,start=now};
            if(room!=null && !torch)
            {
                fire.dropletPaths=Plan(fire,room,profile);
                foreach(var path in fire.dropletPaths)
                    fire.plannedDuration=Math.Max(fire.plannedDuration,path.Arrival+(path.landed?profile.PatchSeconds:0));
            }
            world.grenades.Remove(grenade);world.revision++;
            world.fires.Add(fire);world.revision++;
            return fire;
        }
        // Host: fires burnt out by now.
        public static bool Expire(WorldState world,Func<string,double> now)
        {
            int removed=world.fires.RemoveAll(fire=>now(fire.level)>=fire.start+Duration(fire));
            if(removed>0) world.revision++;
            return removed>0;
        }
        private static int Seed(string id) {unchecked {int hash=17;foreach(char c in id) hash=hash*31+c;return hash;}}

        public static FireSplash Profile(FireState fire)=>WeaponCatalog.Get(fire.weapon).fire??
            throw new InvalidOperationException($"Огонь {fire.id}: у {fire.weapon} нет огненных брызг.");
        public static double Duration(FireState fire)=>fire.torch?fire.fuel:
            fire.dropletPaths!=null?fire.plannedDuration:Profile(fire).PatchSeconds;
        // All a bottle or grenade holds: every droplet burning a patch's time.
        public static float Fuel(FireSplash profile)=>profile.droplets*profile.PatchSeconds;
        // The droplets a splash of this much fuel throws: one per patch time left, at most the profile's.
        public static int Droplets(FireSplash profile,float fuel)=>Mathf.Clamp(Mathf.CeilToInt(fuel/profile.PatchSeconds-.001f),0,profile.droplets);
        public static bool Burning(FireState fire,double now)=>now>=fire.start && now<fire.start+Duration(fire);
        // The pulse running at this time (0…), or −1 outside the fire.
        public static int Pulse(FireState fire,double now)=>Burning(fire,now)?(int)Math.Floor((now-fire.start)/Profile(fire).pulseSeconds):-1;

        // Will a bottle hitting a surface at this speed (its speed into the surface) break? A stable roll of the bottle
        // and its impact count, the same on every peer.
        public static bool Shatters(FireSplash profile,float impact,string bottle,int impactIndex)
        {
            if(!profile.shatter || impact<profile.shatterSpeed) return false;
            if(impact>=profile.sureShatterSpeed) return true;
            float chance=(impact-profile.shatterSpeed)/Mathf.Max(.0001f,profile.sureShatterSpeed-profile.shatterSpeed);
            return Roll(bottle+"|"+impactIndex)<chance;
        }

        // The patches of a fire: the torch's one, or where its droplets land.
        public static List<FirePatch> Patches(FireState fire,RoomLayout room)
        {
            var profile=Profile(fire);var result=new List<FirePatch>();
            if(fire.torch) {result.Add(new FirePatch(fire.Position,profile.patchRadius));return result;}   // it lies at rest
            foreach(var path in fire.dropletPaths??Plan(fire,room,profile))
                if(path.landed) result.Add(new FirePatch(path.End,profile.patchRadius,fire.dropletPaths==null?0:path.Arrival));
            return result;
        }

        private static FireDropletPath[] Plan(FireState fire,RoomLayout room,FireSplash profile)
        {
            var random=new System.Random(fire.seed);
            var inherited=new Vector2(fire.vx,fire.vy);
            var result=new FireDropletPath[Droplets(profile,fire.fuel)];
            for(int i=0;i<result.Length;i++)
            {
                float angle=(float)(random.NextDouble()*Math.PI*2),speed=profile.spread*(.4f+.6f*(float)random.NextDouble());
                var velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*speed;
                // A bottle's droplets keep its motion (its forward speed, a rebound off what it hit, upward at most a little).
                if(!profile.radial) velocity+=new Vector2(inherited.x*.6f,Mathf.Abs(inherited.y)*.15f);
                result[i]=Fly(room,fire.Position+Vector2.up*.15f,velocity);
            }
            return result;
        }

        // A droplet's flight: gravity, sliding down walls and off ceilings, until it lands on a surface top.
        private static FireDropletPath Fly(RoomLayout room,Vector2 position,Vector2 velocity)
        {
            var segments=new List<FireFlightSegment>(3) {new(0,position,velocity)};
            int steps=0;bool landed=false;
            for(float t=0;t<MaxFlight;t+=DropletStep)
            {
                var delta=velocity*DropletStep+Vector2.down*(.5f*DropletGravity*DropletStep*DropletStep);
                velocity+=Vector2.down*DropletGravity*DropletStep;
                var expected=position+delta;
                var moved=ActorTerrainMotion.Move(room,Droplet,position,delta,false,bodyTops:false);
                // Compared with the expected point, not by subtracting: at factory coordinates rounding would read as a hit.
                bool blockedX=Mathf.Abs(moved.x-expected.x)>.00001f,blockedY=Mathf.Abs(moved.y-expected.y)>.00001f;
                position=moved;steps++;
                if(blockedY && velocity.y<0) {landed=true;break;}
                if(blockedX) velocity.x=0;
                if(blockedY) velocity.y=0;
                if(blockedX || blockedY) segments.Add(new FireFlightSegment(steps,position,velocity));
                if(position.y<room.bottom-1) break;
            }
            return new FireDropletPath {segments=segments.ToArray(),steps=steps,endX=position.x,endY=position.y,landed=landed};
        }

        // Does an actor's body touch a patch: its feet above the patch within Reach, horizontally within the radius
        // and half its width.
        public static bool Touches(FirePatch patch,ActorState actor)
        {
            var hull=Anatomy.Movement(actor);var feet=actor.Position;
            return Mathf.Abs(feet.x-patch.position.x)<=patch.radius+hull.width*.5f && feet.y>=patch.position.y-.2f && feet.y<=patch.position.y+Reach;
        }

        private static float Roll(string key)
        {
            unchecked
            {
                uint hash=2166136261;
                foreach(char c in key) {hash^=c;hash*=16777619;}
                hash^=hash>>15;hash*=2246822519;hash^=hash>>13;
                return (hash&0xFFFFFF)/(float)0x1000000;
            }
        }
    }
}
