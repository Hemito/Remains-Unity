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
        public string id,attacker,level,owner;
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
        // A bottle (FireSplash.shatter): how many times it hit something, and whether and with what velocity it broke.
        public int impacts;public bool shattered;public float impactVx,impactVy;
        public Vector2 Position {get=>new(x,y);set {x=value.x;y=value.y;}}
        public Vector2 Velocity {get=>new(vx,vy);set {vx=value.x;vy=value.y;}}
        public GrenadeState Copy()=>JsonUtility.FromJson<GrenadeState>(JsonUtility.ToJson(this));
        public AttackEvent Attack()=>new() {id=id,attacker=attacker,level=level,sourceOwner=owner,sourcePlayer=sourcePlayer,
            weapon=weapon,originX=x,originY=y,endX=x,endY=y};
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
        private static readonly MovementHull Hull=new() {width=.2f,height=.2f};
        public static void BeginSession(WorldState world)
        {
            foreach(var grenade in world.grenades) {GrenadeRotation.Prepare(grenade);grenade.owner=null;grenade.generation++;grenade.sequence=0;}
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
                impact=Mathf.Max(impact,Advance(state,room,(float)step,definition));
                state.simulationRemainder=Math.Max(0,state.simulationRemainder-step);
                state.remaining=Mathf.Max(0,state.remaining-(float)step);
            }
            return impact;
        }
        private static float Advance(GrenadeState state,RoomLayout room,float dt,Remains.Content.WeaponDefinition definition)
        {
            float impact=0;
            var previous=state.Position;
            var acceleration=Vector2.down*definition.grenadeGravity;
            var delta=state.Velocity*dt+acceleration*(.5f*dt*dt);
            state.Velocity+=acceleration*dt;var incoming=state.Velocity;
            // Body tops are actor supports only (BodyTopSurfaces); a grenade landing on them hovered and crept (TD-16).
            var moved=ActorTerrainMotion.Move(room,Hull,previous-Vector2.up*.1f,delta,false,bodyTops:false)+Vector2.up*.1f;
            state.Position=moved;
            GrenadeRotation.Advance(state,dt);
            bool collided=false;
            if(Mathf.Abs(moved.x-previous.x-delta.x)>.00001f)
            {impact=Mathf.Abs(state.vx);state.vx=-state.vx*definition.grenadeRestitution;collided=true;}
            if(Mathf.Abs(moved.y-previous.y-delta.y)>.00001f)
            {
                impact=Mathf.Max(impact,Mathf.Abs(state.vy));
                if(state.vy<0) state.vx*=definition.grenadeGroundRetention;
                state.vy=-state.vy*definition.grenadeRestitution;
                collided=true;
            }
            if(collided)
            {
                state.angularVelocity*=definition.grenadeAngularRetention;
                // A bottle may break by its speed into the surface; it then ends at once with the velocity it hit with.
                if(definition.fire!=null && definition.fire.shatter && FireRules.Shatters(definition.fire,impact,state.id,state.impacts++))
                {
                    state.shattered=true;state.impactVx=incoming.x;state.impactVy=incoming.y;state.remaining=0;
                }
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
                if(players.Any(a=>a.owner==grenade.owner && a.level==grenade.level)) continue;
                string owner=NearbySimulationClient.Select(players,grenade.level,grenade.Position);
                if(grenade.owner==owner) continue;
                grenade.owner=owner;grenade.generation++;grenade.sequence=0;
                changed=true;
            }
            return changed;
        }
        public static ExplosionState Detonate(WorldState world,GrenadeState grenade,IEnumerable<string> enemyHits)
        {
            if(!world.grenades.Contains(grenade) || grenade.remaining>0) return null;
            world.grenades.Remove(grenade);
            return ExplosionRules.Record(world,grenade.Attack(),enemyHits,false,grenade.generation);
        }
        public static bool Resolve(ActorState actor,ExplosionState explosion,DamageReceiver damage,Vector2 localPosition)
        {
            if(!ActorPresence.IsPresent(actor) || actor.resolvedExplosions.Contains(explosion.attack.id) ||
                !(actor.player?explosion.players:explosion.enemies).Contains(actor.id)) return false;
            var attack=JsonUtility.FromJson<AttackEvent>(JsonUtility.ToJson(explosion.attack));
            attack.endX=localPosition.x;attack.endY=localPosition.y;
            bool contact=actor.player?HitGeometry.Contacts(actor,attack):true;
            if(AttackScope.CanAffect(attack,actor,explosion.friendlyFire)) damage.Apply(actor,attack,contact);
            // A miss is also a final victim decision. Returning later cannot turn it into a hit.
            actor.resolvedExplosions.Add(attack.id);actor.revision++;return true;
        }
        public static void ForgetHubActor(WorldState world,string id)
        {foreach(var explosion in world.explosions) explosion.players.Remove(id);}
    }
}
