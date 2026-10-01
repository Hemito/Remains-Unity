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
        public RoomLayout room;
        public bool requested;
        public float nextPublish;
        public Vector2 position=>state.Position;
    }
    public sealed class GrenadeController
    {
        public IReadOnlyList<GrenadeFlight> Flights=>flights;
        public event Action<GrenadeFlight> Created;
        private readonly List<GrenadeFlight> flights=new();
        private readonly CoopSession session;
        private readonly InteractionAdapter adapter;
        private readonly Dictionary<string,Vector2> observedExplosions=new();
        public GrenadeController(CoopSession session,InteractionAdapter adapter,CombatController combat)
        {
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
            if(session.World==null) {flights.Clear();observedExplosions.Clear();return;}
            foreach(var state in session.World.grenades) Accept(state);
            flights.RemoveAll(f=>!session.World.grenades.Any(g=>g.id==f.state.id));
        }
        private void Accept(GrenadeState state)
        {
            var flight=flights.Find(f=>f.state.id==state.id);
            if(flight==null)
            {
                flight=new GrenadeFlight {state=state.Copy(),room=RoomLayout.Load(state.level),
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
            foreach(var flight in flights)
            {
                var state=flight.state;
                if(state.owner==null || !session.World.actors.Any(a=>a.player && a.connected && a.level==state.level)) continue;
                if(state.remaining>0) GrenadeRules.Step(state,flight.room,dt);
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
                else if(state.owner==session.LocalOwner && Time.unscaledTime>=flight.nextPublish)
                {flight.nextPublish=Time.unscaledTime+.1f;adapter.SendGrenade(state,false);}
            }
        }
    }
}
