using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class GrenadeFlight
    {
        public GrenadeState state;
        public GrenadeMessage message;
        public bool requested;
        public readonly ReplicationCadence progressCadence=new(.1);
        public Vector2 position=>state.Position;
    }
    public sealed class GrenadeController
    {
        public IReadOnlyList<GrenadeFlight> Flights=>flights;
        public event Action<GrenadeFlight> Created;
        public event Action<GrenadeState,float> Impact;
        private readonly List<GrenadeFlight> flights=new();
        private readonly CoopSession session;
        private readonly InteractionAdapter adapter;
        private readonly SimulationGeometry geometry;
        private readonly Dictionary<string,Vector2> observedExplosions=new();
        private readonly SimulationTimer throwCooldown=new();
        private string localActorId;
        private int localGeneration,localLifecycle;
        public bool TryThrow(Vector2 aim)
        {
            if(!throwCooldown.Ready || !adapter.ThrowGrenade(aim,session.LocalActor is { } actor?geometry.Get(actor.level):null)) return false;
            throwCooldown.Restart(Remains.Content.WeaponCatalog.Get("grenade").cooldown);
            return true;
        }
        public GrenadeController(CoopSession session,InteractionAdapter adapter,CombatController combat,SimulationGeometry geometry)
        {
            this.geometry=geometry;
            this.session=session;this.adapter=adapter;
            session.Changed+=Reconcile;
            adapter.GrenadeUpdated+=Accept;
            combat.LocalExplosionPosition=(id,generation)=>
            {
                if(observedExplosions.TryGetValue(id+":"+generation,out var position)) return position;
                var active=flights.Find(f=>f.state.id==id && f.state.generation==generation);
                return active==null?(Vector2?)null:active.position;
            };
        }
        private void Reconcile()
        {
            var local=session.LocalActor;
            if(localActorId!=local?.id || localGeneration!=local?.generation || localLifecycle!=local?.lifecycleSequence)
            {
                localActorId=local?.id;localGeneration=local?.generation??0;localLifecycle=local?.lifecycleSequence??0;
                throwCooldown.Restart(0);
            }
            if(session.World==null) {flights.Clear();observedExplosions.Clear();return;}
            foreach(var state in session.World.grenades) Accept(state);
            flights.RemoveAll(f=>!session.World.grenades.Any(g=>g.id==f.state.id));
        }
        private void Accept(GrenadeState state)
        {
            var flight=flights.Find(f=>f.state.id==state.id);
            if(flight==null)
            {
                flight=new GrenadeFlight {state=state.Copy(),
                    message=new GrenadeMessage {id=state.id,attacker=state.attacker,level=state.level}};
                flights.Add(flight);Created?.Invoke(flight);return;
            }
            if(state.generation<flight.state.generation) return;
            if(state.generation!=flight.state.generation || (state.owner!=session.LocalOwner && unchecked((int)(state.sequence-flight.state.sequence))>0))
            {flight.state=state.Copy();flight.requested=false;}
        }
        public void Step(float dt)
        {
            if(session.World==null) return;
            if(session.LocalActor is { } local && !session.Pause.IsPaused(local.level)) throwCooldown.Advance(dt);
            foreach(var flight in flights)
            {
                var state=flight.state;
                if(state.owner==null || !LevelActivity.HasConnectedPlayer(session.World,state.level)) continue;
                if(session.Pause.IsPaused(state.level)) continue;
                if(state.remaining>0)
                {
                    float speed=GrenadeRules.Step(state,geometry.Get(state.level),dt);
                    if(speed>0 && state.owner==session.LocalOwner) Impact?.Invoke(state,speed);
                }
                if(state.remaining<=0)
                {
                    observedExplosions[state.id+":"+state.generation]=state.Position;
                    if(state.owner==session.LocalOwner && !flight.requested)
                    {
                        flight.requested=true;
                        var attack=state.Attack();
                        var hits=session.World.actors.Where(a=>!a.player && AttackScope.Includes(attack,a) && HitGeometry.Contacts(a,attack)).Select(a=>a.id).ToArray();
                        adapter.SendGrenade(state,true,hits);
                    }
                }
                else if(state.owner==session.LocalOwner && flight.progressCadence.TryAdvance(Time.unscaledTimeAsDouble))
                    adapter.SendGrenade(state,false);
            }
        }
    }
}
