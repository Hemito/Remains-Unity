using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Body strikes of the actors this peer owns (its player, its raiders): the one executor of WKick for both. A strike
    // keeps the actor busy for the weapon's cooldown; at its contact moment the owner sends all its segments as one
    // attack (one ID, so a victim takes it once). A strike whose actor is handed over, removed, knocked down or moved to
    // another level is dropped; a paused level holds it.
    public sealed class PonyStrikes
    {
        private sealed class Strike {public string weapon,id,level;public int generation,lifecycle;public float elapsed;public bool hit;}
        private readonly Dictionary<string,Strike> active=new(StringComparer.Ordinal);
        private readonly List<(Vector2 start,Vector2 end)> segments=new();
        private readonly List<string> finished=new();
        public bool Busy(ActorState actor)=>actor!=null && active.ContainsKey(actor.id);

        public bool Begin(ActorState actor,bool kick,RoomLayout room)
        {
            if(Busy(actor) || !PonyStrikeRules.CanBegin(actor,room)) return false;
            active[actor.id]=new Strike {weapon=PonyStrikeRules.Weapon(kick),id=Guid.NewGuid().ToString("N"),level=actor.level,
                generation=actor.generation,lifecycle=actor.lifecycleSequence};
            // Raiders use a separate sprite atlas. Only the player's assembled pony has
            // the full body timelines; damage/contact remain driven by the strike below.
            if(actor.player) ActorPose.Restart(actor,PonyStrikeRules.Weapon(kick));
            return true;
        }

        public void Step(WorldState world,string owner,float dt,Func<string,bool> paused,Action<AttackEvent> fire)
        {
            finished.Clear();
            foreach(var pair in active)
            {
                var strike=pair.Value;var actor=world?.Find(pair.Key);
                if(actor==null || actor.down || actor.owner!=owner || actor.level!=strike.level || actor.generation!=strike.generation ||
                    actor.lifecycleSequence!=strike.lifecycle) {finished.Add(pair.Key);continue;}
                if(paused(actor.level)) continue;
                var weapon=WeaponCatalog.Get(strike.weapon);
                strike.elapsed+=dt;
                if(!strike.hit && strike.elapsed>=weapon.strike.contact*weapon.cooldown)
                {
                    strike.hit=true;
                    PonyStrikeRules.Segments(actor,weapon,segments);
                    foreach(var (start,end) in segments)
                        fire(new AttackEvent {id=strike.id,attacker=actor.id,weapon=weapon.id,level=actor.level,factoryEnemy=FactoryEnemyScaling.Capture(actor.factoryEnemy),
                            originX=start.x,originY=start.y,endX=end.x,endY=end.y,radius=MeleeSweep.ContactRadius});
                }
                if(strike.elapsed>=weapon.cooldown) finished.Add(pair.Key);
            }
            foreach(string id in finished) active.Remove(id);
        }
    }
}
