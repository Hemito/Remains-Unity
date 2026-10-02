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
        public bool contact;
        public float finishedAge,nextPublish;
        public readonly HashSet<string> hit=new();
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
        public int Contacts {get;private set;}
        public ProjectileController(CoopSession session,SimulationGeometry geometry,Action<AttackEvent,AttackTargetSnapshot,string> apply)
        {this.session=session;this.geometry=geometry;this.apply=apply;session.Changed+=Reconcile;session.Projectiles.Launched+=Reconcile;}
        private void Reconcile()
        {
            if(session.World==null) {flights.Clear();return;}
            foreach(var state in session.World.projectiles)
            {
                if(flights.TryGetValue(state.launch.id,out var existing))
                {
                    if(state.generation>existing.state.generation)
                    {existing.state=state.Copy();existing.path=state.Path();existing.contact=false;existing.finishedAge=0;}
                    continue;
                }
                if(state.finished) continue;
                var flight=new ProjectileFlight {state=state.Copy(),path=state.Path(),segment=Remains.Serialization.StateJson.Copy(state.launch)};
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
                if(flight.path.Finished)
                {
                    flight.finishedAge+=dt;
                    // Retain local receipt until the canonical state has also finished.
                    if(flight.finishedAge>1 && session.World.projectiles.Find(p=>p.launch.id==pair.Key)?.finished!=false) expired.Add(pair.Key);
                    continue;
                }
                var result=flight.path.Advance(dt,(start,end)=>Resolve(flight,start,end));
                flight.contact|=result.contact;
                if(state.owner==session.LocalOwner && (flight.path.Finished || Time.unscaledTime>=flight.nextPublish))
                {flight.nextPublish=Time.unscaledTime+.05f;session.Projectiles.Publish(state,flight.path.Traveled,flight.path.Finished);}
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
            float? terrain=definition.stopAtTerrain?ShotObstacles.FirstContact(geometry.Get(attack.level),start,end):null;
            var contacts=AttackContacts.Capture(session.World.actors,attack,session.World.friendlyFire,definition.stopAtFirstActor,terrain??float.PositiveInfinity);
            float? stop=terrain;
            foreach(var target in contacts)
            {
                var actor=session.World.Find(target.id);
                Anatomy.Contact(actor,attack,out float entry);
                if(definition.stopAtFirstActor) stop=entry;
                if(flight.hit.Add(target.id)) {apply(attack,target,flight.state.owner);Contacts++;}
            }
            return stop;
        }
    }
}
