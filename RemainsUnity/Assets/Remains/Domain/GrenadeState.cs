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
        public bool sourcePlayer;
        public int generation;
        public uint sequence;
        public float x,y,vx,vy,remaining;
        public double simulationRemainder;
        public int rotationVersion;
        public float angle,angularVelocity;
        public bool mirrored;
        public Vector2 Position {get=>new(x,y);set {x=value.x;y=value.y;}}
        public Vector2 Velocity {get=>new(vx,vy);set {vx=value.x;vy=value.y;}}
        public GrenadeState Copy()=>JsonUtility.FromJson<GrenadeState>(JsonUtility.ToJson(this));
        public AttackEvent Attack()=>new() {id=id,attacker=attacker,level=level,sourceOwner=owner,sourcePlayer=sourcePlayer,
            weapon="grenade",originX=x,originY=y,endX=x,endY=y};
    }
    [Serializable]
    public sealed class ExplosionState : Remains.Serialization.ExtensibleState
    {
        public AttackEvent attack;
        public bool friendlyFire;
        public int generation;
        public List<string> players=new();
        public List<string> enemies=new();
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
            var definition=Remains.Content.WeaponCatalog.Get("grenade");
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
            state.Velocity+=acceleration*dt;
            var moved=ActorTerrainMotion.Move(room,Hull,previous-Vector2.up*.1f,delta,false)+Vector2.up*.1f;
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
            if(collided) state.angularVelocity*=definition.grenadeAngularRetention;
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
            var explosion=new ExplosionState {attack=grenade.Attack(),friendlyFire=world.friendlyFire,generation=grenade.generation};
            explosion.players=world.actors.Where(a=>a.player && ActorPresence.IsPresent(a) && a.level==grenade.level).Select(a=>a.id).ToList();
            var hits=new HashSet<string>(enemyHits ?? Array.Empty<string>());
            explosion.enemies=world.actors.Where(a=>!a.player && a.level==grenade.level && hits.Contains(a.id)).Select(a=>a.id).ToList();
            world.grenades.Remove(grenade);world.explosions.Add(explosion);return explosion;
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
