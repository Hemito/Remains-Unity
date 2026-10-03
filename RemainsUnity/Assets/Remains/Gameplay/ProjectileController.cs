using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class ProjectileFlight
    {
        public ProjectileState state;
        public LinearProjectilePath path;
        public AttackEvent segment;
        public bool contact,cancelled,awaitingConfirmation;
        public float finishedAge;
        public readonly ReplicationCadence progressCadence=new(.05);
        public readonly HashSet<string> hit=new();
        internal readonly HashSet<string> observedContacts=new();
        internal readonly List<(AttackEvent attack,AttackTargetSnapshot target)> pendingContacts=new();
    }
    public sealed class ProjectileController
    {
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly Action<AttackEvent,AttackTargetSnapshot,string> apply;
        private readonly Dictionary<string,ProjectileFlight> flights=new();
        private readonly List<string> expired=new();
        public IEnumerable<ProjectileFlight> Flights=>flights.Values;
        public event Action<ProjectileFlight> Created;
        public event Action<ProjectileContact> ContactObserved;
        public int Contacts {get;private set;}
        public ProjectileController(CoopSession session,SimulationGeometry geometry,Action<AttackEvent,AttackTargetSnapshot,string> apply)
        {
            this.session=session;this.geometry=geometry;this.apply=apply;
            session.Changed+=Reconcile;session.Projectiles.Launched+=Reconcile;
            session.Projectiles.Rejected+=id=>
            {if(flights.Remove(id,out var flight)) {flight.cancelled=true;flight.pendingContacts.Clear();}};
        }
        private void Reconcile()
        {
            if(session.World==null) {flights.Clear();return;}
            foreach(var state in session.World.projectiles)
            {
                if(flights.TryGetValue(state.launch.id,out var existing))
                {
                    if(state.generation>existing.state.generation)
                    {
                        existing.state=state.Copy();existing.path=state.Path();existing.contact=false;existing.finishedAge=0;
                        // A previous observer may already have predicted this contact without
                        // owning its decision. Re-evaluate from canonical progress; the target's
                        // durable attack receipts, not this visual cache, prevent double damage.
                        existing.hit.Clear();
                        existing.pendingContacts.Clear();existing.awaitingConfirmation=false;
                    }
                    else if(existing.awaitingConfirmation && !session.Projectiles.IsPending(state.launch.id))
                    {
                        // Confirm the same flight without resetting path, contacts, age or sequence.
                        existing.awaitingConfirmation=false;
                        existing.state.launch.friendlyFireDecision=state.launch.friendlyFireDecision;
                        existing.segment.friendlyFireDecision=state.launch.friendlyFireDecision;
                    }
                    continue;
                }
                if(state.finished) continue;
                var flight=new ProjectileFlight {state=state.Copy(),path=state.Path(),segment=Remains.Serialization.StateJson.Copy(state.launch),
                    awaitingConfirmation=session.Projectiles.IsPending(state.launch.id)};
                flights.Add(state.launch.id,flight);Created?.Invoke(flight);
            }
        }
        public void Step(float dt)
        {
            if(session.World==null) return;
            expired.Clear();
            foreach(var pair in flights)
            {
                var flight=pair.Value;var state=flight.state;
                if(state.owner==null || !LevelActivity.HasConnectedPlayer(session.World,state.launch.level) || session.Pause.IsPaused(state.launch.level)) continue;
                if(!flight.awaitingConfirmation && flight.pendingContacts.Count>0)
                {
                    foreach(var pending in flight.pendingContacts)
                    {
                        pending.attack.friendlyFireDecision=state.launch.friendlyFireDecision;
                        apply(pending.attack,pending.target,state.owner);Contacts++;
                    }
                    flight.pendingContacts.Clear();
                }
                if(flight.path.Finished)
                {
                    flight.finishedAge+=dt;
                    // Retain local receipt until the canonical state has also finished.
                    if(flight.finishedAge>1 && session.World.projectiles.Find(p=>p.launch.id==pair.Key)?.finished!=false) expired.Add(pair.Key);
                    continue;
                }
                var result=flight.path.Advance(dt,(start,end)=>Resolve(flight,start,end));
                flight.contact|=result.contact;
                if(state.owner==session.LocalOwner && (flight.path.Finished || flight.progressCadence.TryAdvance(Time.unscaledTimeAsDouble)))
                    session.Projectiles.Publish(state,flight.path.Traveled,flight.path.Finished);
            }
            foreach(string id in expired) flights.Remove(id);
        }
        private float? Resolve(ProjectileFlight flight,Vector2 start,Vector2 end)
        {
            // This per-flight scratch segment is consumed synchronously; launch stays immutable.
            var attack=flight.segment;
            // Saved launch.speed in older worlds was a hitscan estimate. The actual
            // restored trajectory is the source of contact speed for damage rules.
            attack.speed=flight.path.Speed;
            attack.originX=start.x;attack.originY=start.y;attack.endX=end.x;attack.endY=end.y;
            var definition=WeaponCatalog.Get(attack.weapon);
            RoomSurface surface=null;
            float? terrain=definition.stopAtTerrain?ShotObstacles.FirstContact(geometry.Get(attack.level),start,end,out surface):null;
            var contacts=AttackContacts.Capture(session.World.actors,attack,session.World.friendlyFire,definition.stopAtFirstActor,terrain??float.PositiveInfinity);
            float? stop=terrain;
            bool stoppedAtActor=false;
            foreach(var target in contacts)
            {
                var actor=session.World.Find(target.id);
                Anatomy.Contact(actor,attack,out float entry);
                if(definition.stopAtFirstActor) {stop=entry;stoppedAtActor=true;}
                if(flight.hit.Add(target.id))
                {
                    // NPC contact is a report, not a local health change. The reliable
                    // launch precedes this report on the same connection, so the host can
                    // validate it and route it to the NPC owner without a return trip first.
                    // Player contacts still wait for the confirmed friendly-fire policy.
                    if(flight.awaitingConfirmation && target.player) flight.pendingContacts.Add((Remains.Serialization.StateJson.Copy(attack),target));
                    else {apply(attack,target,flight.state.owner);Contacts++;}
                }
                Observe(flight,"actor:"+target.id,Vector2.Lerp(start,end,entry),target.id,actor.actorType,null);
            }
            if(terrain.HasValue && !stoppedAtActor)
                Observe(flight,"terrain",Vector2.Lerp(start,end,terrain.Value),null,null,surface.texture);
            return stop;
        }
        private void Observe(ProjectileFlight flight,string key,Vector2 position,string target,string targetType,string texture)
        {
            // Keep observation receipts across ownership recovery; damage receipts remain separate.
            if(!flight.observedContacts.Add(key)) return;
            var launch=flight.state.launch;
            ContactObserved?.Invoke(new ProjectileContact(launch.id,launch.weapon,launch.level,position,target,targetType,texture));
        }
    }
}
