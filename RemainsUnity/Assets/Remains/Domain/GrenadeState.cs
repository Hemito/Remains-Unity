using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using JsonUtility=Remains.Serialization.StateJson;

namespace Remains.Domain
{
    [Serializable]
    public sealed class GrenadeState : Remains.Serialization.ExtensibleState
    {
        public string id,attacker,level,owner,holder;
        // Which grenade (WeaponCatalog.IsGrenade): its flight, fuse and blast. Older saves hold the hand-made grenade.
        public string weapon="grenade";
        public bool sourcePlayer;
        public int generation;
        public uint sequence;
        public float x,y,vx,vy,remaining;
        public double simulationRemainder;
        public int rotationVersion;
        public float angle,angularVelocity;
        public bool mirrored;
        // A bottle (FireSplash.shatter), lit when thrown: `remaining` is its fuel burning from the neck; how many times it
        // hit something, whether and with what velocity it broke, and the fuel it ended its flight with (broken or at rest).
        public int impacts;public bool shattered;public float impactVx,impactVy,fuel;
        public Vector2 Position {get=>new(x,y);set {x=value.x;y=value.y;}}
        public Vector2 Velocity {get=>new(vx,vy);set {vx=value.x;vy=value.y;}}
        public GrenadeState Copy()=>JsonUtility.FromJson<GrenadeState>(JsonUtility.ToJson(this));
        public AttackEvent Attack()=>new() {id=id,attacker=attacker,level=level,sourceOwner=owner,sourcePlayer=sourcePlayer,
            weapon=weapon,originX=x,originY=y,endX=x,endY=y};
    }
    [Serializable]
    public struct GrenadeCoverVolume
    {
        public float x,y,width,height;
        internal GrenadeCoverVolume(Rect rect) {x=rect.x;y=rect.y;width=rect.width;height=rect.height;}
        internal Rect Bounds=>new(x,y,width,height);
    }

    [Serializable]
    public sealed class ExplosionState : Remains.Serialization.ExtensibleState
    {
        public AttackEvent attack;
        public bool friendlyFire;
        // A mine's blast: its position is the recorded attack end, not a locally simulated flight.
        public bool stationary;
        public int generation;
        public List<string> players=new();
        public List<string> enemies=new();
        // Frozen by the host before publishing the grenade detonation. Null is a legacy or non-grenade blast.
        public GrenadeCoverVolume[] grenadeCover;
    }
    // One way an explosion becomes world state (grenades, mines, charges): every present player of the level decides
    // its own contact; the host lists the non-player actors it hit.
    public static class ExplosionRules
    {
        // Bullet.explGas: the blast's damage × 0.7–1.3, whole within half the radius, falling linearly to nothing at the
        // radius (user 2026-11-01: as the original's). The spread is a stable roll of the blast and the victim, so every
        // peer that applies it gets the same number. Not ported, by the same decision: the quarter damage to the
        // thrower's own faction and the player's own-blast perk (autoExpl).
        public static float Damage(float damage,float radius,float distance,string blast,string victim)
        {
            if(!(distance<radius) || radius<=0) return 0;
            float falloff=distance>radius*.5f?2-distance*2/radius:1;
            return damage*(.7f+.6f*Roll(blast,victim))*falloff;
        }
        private static float Roll(string blast,string victim)
        {
            unchecked
            {
                uint hash=2166136261;
                foreach(char c in blast+"|"+victim) {hash^=c;hash*=16777619;}
                hash^=hash>>15;hash*=2246822519;hash^=hash>>13;
                return (hash&0xFFFFFF)/(float)0x1000000;
            }
        }
        public static ExplosionState Record(WorldState world,AttackEvent attack,IEnumerable<string> enemyHits,bool stationary,int generation=0)
        {
            var explosion=new ExplosionState {attack=attack,friendlyFire=world.friendlyFire,stationary=stationary,generation=generation};
            explosion.players=world.actors.Where(a=>a.player && ActorPresence.IsPresent(a) && a.level==attack.level).Select(a=>a.id).ToList();
            var hits=new HashSet<string>(enemyHits??Array.Empty<string>(),StringComparer.Ordinal);
            explosion.enemies=world.actors.Where(a=>!a.player && a.level==attack.level && hits.Contains(a.id)).Select(a=>a.id).ToList();
            world.explosions.Add(explosion);
            return explosion;
        }
    }
    public static class GrenadeRules
    {
        public const double SimulationStep=1d/120;
        private const float BottleRest=.25f;   // m/s: a bottle this slow on the ground lies still
        private static readonly MovementHull Hull=new() {width=.2f,height=.2f};
        // A rule view only: it is never inserted into the world's props or persisted separately.
        public static PropState TelekinesisBody(GrenadeState grenade,PropState view=null)
        {
            view??=new PropState {mass=.5f,width=.2f,height=.2f,anchorHeight=.1f,movable=true,hasPhysicalBody=true};
            view.id=grenade.id;view.definition=grenade.weapon;view.level=grenade.level;view.Position=grenade.Position;
            view.owner=grenade.owner;view.held=grenade.holder!=null;view.generation=grenade.generation;
            return view;
        }
        public static void Hold(GrenadeState grenade,ActorState actor)
        {
            grenade.holder=actor.id;grenade.owner=actor.owner;grenade.Velocity=Vector2.zero;grenade.angularVelocity=0;
            grenade.generation++;grenade.sequence=0;
        }
        public static void ReleaseHold(GrenadeState grenade)
        {grenade.holder=null;grenade.generation++;grenade.sequence=0;}
        public static void MoveHeld(GrenadeState grenade,RoomLayout room,Vector2 target,float dt)
        {
            if(grenade.holder==null || dt<=0 || grenade.remaining<=0) return;
            var start=grenade.Position;
            var delta=Vector2.ClampMagnitude(TelekinesisFollow.Displacement(start,target,dt),10*dt);
            var feet=start-Vector2.up*.1f;
            grenade.Position=ActorTerrainMotion.Move(room,Hull,feet,delta,ignoreOneWay:delta.y<0,bodyTops:false)+Vector2.up*.1f;
            grenade.Velocity=(grenade.Position-start)/dt;
        }
        public static void BeginSession(WorldState world)
        {
            foreach(var grenade in world.grenades) {GrenadeRotation.Prepare(grenade);grenade.owner=null;ReleaseHold(grenade);}
        }
        public static float Step(GrenadeState state,RoomLayout room,float dt)
        {
            if(dt<=0 || state.remaining<=0) return 0;
            GrenadeRotation.Prepare(state);
            if(!float.IsFinite(dt) || !double.IsFinite(state.simulationRemainder) || state.simulationRemainder<0 || state.simulationRemainder>=SimulationStep)
                throw new InvalidOperationException($"Некорректные часы гранаты {state.id}: dt={dt}, остаток={state.simulationRemainder}.");
            state.simulationRemainder=Math.Min(state.remaining,state.simulationRemainder+dt);
            float impact=0;
            var definition=Remains.Content.WeaponCatalog.Get(state.weapon);
            if(!definition.IsGrenade) throw new InvalidOperationException($"Граната {state.id}: {state.weapon} не ручная граната.");
            while(state.remaining>0)
            {
                double step=Math.Min(SimulationStep,state.remaining);
                if(state.simulationRemainder+1e-7<step) break;
                if(state.holder==null) impact=Mathf.Max(impact,Advance(state,room,(float)step,definition));
                state.simulationRemainder=Math.Max(0,state.simulationRemainder-step);
                state.remaining=Mathf.Max(0,state.remaining-(float)step);
            }
            // A bottle ends mid-frame by its impact: the unspent time is not a clock any more, and the host rejects a final
            // message that carries a remainder of a step or more (and with it the fire).
            if(state.remaining<=0) state.simulationRemainder=0;
            return impact;
        }
        private static float Advance(GrenadeState state,RoomLayout room,float dt,Remains.Content.WeaponDefinition definition)
        {
            float impact=0;
            var previous=state.Position;
            var acceleration=Vector2.down*definition.grenadeGravity;
            var delta=state.Velocity*dt+acceleration*(.5f*dt*dt);
            state.Velocity+=acceleration*dt;var incoming=state.Velocity;
            var startFeet=previous-Vector2.up*.1f;
            var expectedFeet=startFeet+delta;
            // Body tops are actor supports only (BodyTopSurfaces); a grenade landing on them hovered and crept (TD-16).
            var movedFeet=ActorTerrainMotion.Move(room,Hull,startFeet,delta,false,bodyTops:false);
            state.Position=movedFeet+Vector2.up*.1f;
            GrenadeRotation.Advance(state,dt);
            bool collided=false,ground=false;
            // Compare in Move's own coordinate system. Subtracting the previous world position after the move
            // amplified float rounding at factory coordinates into phantom impacts (PT-20261103-08).
            if(Mathf.Abs(movedFeet.x-expectedFeet.x)>.00001f)
            {impact=Mathf.Abs(state.vx);state.vx=-state.vx*definition.grenadeRestitution;collided=true;}
            if(Mathf.Abs(movedFeet.y-expectedFeet.y)>.00001f)
            {
                impact=Mathf.Max(impact,Mathf.Abs(state.vy));
                if(state.vy<0) {state.vx*=definition.grenadeGroundRetention;ground=true;}
                state.vy=-state.vy*definition.grenadeRestitution;
                collided=true;
            }
            if(collided)
            {
                state.angularVelocity*=definition.grenadeAngularRetention;
                // A bottle may break by its speed into the surface; it then ends at once with the velocity it hit with.
                if(definition.fire!=null && definition.fire.shatter && FireRules.Shatters(definition.fire,impact,state.id,state.impacts++))
                {
                    state.shattered=true;state.impactVx=incoming.x;state.impactVy=incoming.y;state.fuel=state.remaining;state.remaining=0;
                }
                // Unbroken and come to rest on the ground: it lies and burns its fuel out as a torch.
                else if(ground && definition.fire!=null && definition.fire.shatter && Mathf.Abs(state.vx)<BottleRest && Mathf.Abs(state.vy)<BottleRest)
                {state.fuel=state.remaining;state.remaining=0;state.vx=state.vy=0;}
            }
            return impact;
        }
        public static void Reassign(WorldState world,IReadOnlyCollection<string> owners)
            =>ReassignAndReportChanges(world,owners);
        public static bool ReassignAndReportChanges(WorldState world,IReadOnlyCollection<string> owners)
        {
            bool changed=false;
            var players=world.actors.Where(a=>a.player && a.connected && owners.Contains(a.owner)).ToArray();
            foreach(var grenade in world.grenades)
            {
                bool ownerHere=players.Any(a=>a.owner==grenade.owner && a.level==grenade.level);
                bool lostHolder=grenade.holder!=null && !players.Any(a=>a.id==grenade.holder && a.owner==grenade.owner &&
                    a.level==grenade.level && !a.down && a.mana>0);
                if(ownerHere && !lostHolder) continue;
                string owner=ownerHere?grenade.owner:NearbySimulationClient.Select(players,grenade.level,grenade.Position);
                if(grenade.owner==owner && !lostHolder) continue;
                grenade.owner=owner;ReleaseHold(grenade);
                changed=true;
            }
            return changed;
        }
        public static ExplosionState Detonate(WorldState world,GrenadeState grenade,IEnumerable<string> enemyHits)
            =>Detonate(world,grenade,enemyHits,null);

        public static ExplosionState Detonate(WorldState world,GrenadeState grenade,IEnumerable<string> enemyHits,RoomLayout room)
        {
            if(!world.grenades.Contains(grenade) || grenade.remaining>0) return null;
            var attack=grenade.Attack();
            var cover=room==null?null:CaptureCover(room,world,attack);
            world.grenades.Remove(grenade);
            var explosion=ExplosionRules.Record(world,attack,enemyHits,false,grenade.generation);
            explosion.grenadeCover=cover;
            return explosion;
        }

        private static GrenadeCoverVolume[] CaptureCover(RoomLayout room,WorldState world,AttackEvent attack)
        {
            const float inset=.0001f;
            float radius=Remains.Content.WeaponCatalog.Get(attack.weapon).range;
            var area=new Rect(attack.End-Vector2.one*radius,Vector2.one*(radius*2));
            var cover=new List<GrenadeCoverVolume>();
            void Include(Rect bounds)
            {
                // A grenade resting on a floor must be able to blast out of the contact plane.
                var interior=Rect.MinMaxRect(bounds.xMin+inset,bounds.yMin+inset,bounds.xMax-inset,bounds.yMax-inset);
                if(interior.width>0 && interior.height>0 && area.Overlaps(interior)) cover.Add(new GrenadeCoverVolume(interior));
            }
            foreach(var surface in RoomSurfaces.Near(room,area))
                if(!surface.oneWay && string.IsNullOrEmpty(surface.sourceDoorId)) Include(new Rect(surface.x,surface.y,surface.width,surface.height));
            // The host's current doors can be ahead of its own client-side room geometry.
            foreach(var door in world.doors)
                if(door.level==attack.level && door.Blocks) Include(door.Bounds);
            return cover.ToArray();
        }

        internal static bool BlastClear(GrenadeCoverVolume[] cover,Vector2 from,Vector2 to)
        {
            if(cover==null) return true;
            foreach(var obstacle in cover)
                if(SurfaceContact.Entry(obstacle.Bounds,from,to).HasValue) return false;
            return true;
        }

        public static bool Resolve(ActorState actor,ExplosionState explosion,DamageReceiver damage,Vector2 localPosition)
        {
            if(!ActorPresence.IsPresent(actor) || actor.resolvedExplosions.Contains(explosion.attack.id) ||
                !(actor.player?explosion.players:explosion.enemies).Contains(actor.id)) return false;
            var attack=JsonUtility.FromJson<AttackEvent>(JsonUtility.ToJson(explosion.attack));
            attack.endX=localPosition.x;attack.endY=localPosition.y;
            float? blastDistance=explosion.grenadeCover==null?null:
                Anatomy.BlastDistance(actor,localPosition,point=>BlastClear(explosion.grenadeCover,explosion.attack.End,point));
            bool contact=blastDistance.HasValue
                ? blastDistance.Value<Remains.Content.WeaponCatalog.Get(attack.weapon).range
                : !actor.player || HitGeometry.Contacts(actor,attack);
            if(AttackScope.CanAffect(attack,actor,explosion.friendlyFire)) damage.Apply(actor,attack,contact,blastDistance);
            // A miss is also a final victim decision. Returning later cannot turn it into a hit.
            actor.resolvedExplosions.Add(attack.id);actor.revision++;return true;
        }
        public static void ForgetHubActor(WorldState world,string id)
        {foreach(var explosion in world.explosions) explosion.players.Remove(id);}
    }
}
