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
        private static readonly MovementHull Hull=new() {width=.2f,height=.2f};
        public static void BeginSession(WorldState world)
        {
            foreach(var grenade in world.grenades) {grenade.owner=null;grenade.generation++;grenade.sequence=0;}
        }
        public static void Step(GrenadeState state,RoomLayout room,float dt)
        {
            if(dt<=0) return;
            var previous=state.Position;
            state.Velocity+=Vector2.down*WorldPhysics.Gravity*dt;
            var delta=state.Velocity*dt;
            var moved=ActorTerrainMotion.Move(room,Hull,previous-Vector2.up*.1f,delta,false)+Vector2.up*.1f;
            state.Position=moved;
            if(Mathf.Abs(moved.x-previous.x-delta.x)>.00001f) state.vx=-state.vx*.45f;
            if(Mathf.Abs(moved.y-previous.y-delta.y)>.00001f)
            {
                if(state.vy<0) state.vx*=.75f;
                state.vy=-state.vy*.45f;
            }
            state.remaining=Mathf.Max(0,state.remaining-dt);
        }
        public static void Reassign(WorldState world,IReadOnlyCollection<string> owners)
        {
            var players=world.actors.Where(a=>a.player && a.connected && owners.Contains(a.owner)).ToArray();
            foreach(var grenade in world.grenades)
            {
                if(players.Any(a=>a.owner==grenade.owner && a.level==grenade.level)) continue;
                string owner=NearbySimulationClient.Select(players,grenade.level,grenade.Position);
                if(grenade.owner==owner) continue;
                grenade.owner=owner;grenade.generation++;grenade.sequence=0;
            }
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
