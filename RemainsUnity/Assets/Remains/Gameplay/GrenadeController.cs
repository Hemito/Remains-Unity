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
            // Diagnostics (PT-20261103-08, a grenade snagging mid-flight): a flight dropped while it still flies.
            foreach(var f in flights) if(f.state.remaining>0 && !session.World.grenades.Any(g=>g.id==f.state.id))
                UnityEngine.Debug.LogWarning($"Граната {f.state.id}: полёт снят до конца запала (нет в мире): позиция={f.state.Position}, осталось={f.state.remaining:0.00} с, поколение={f.state.generation}, владелец={f.state.owner}.");
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
            {
                // Diagnostics (PT-20261103-08): the simulated flight replaced by the world's state — a jump of this size.
                UnityEngine.Debug.LogWarning($"Граната {state.id}: полёт заменён состоянием мира: поколение {flight.state.generation}→{state.generation}, "+
                    $"владелец {flight.state.owner}→{state.owner} (свой {session.LocalOwner}), номер {flight.state.sequence}→{state.sequence}, "+
                    $"скачок {Vector2.Distance(flight.state.Position,state.Position):0.00} м ({flight.state.Position}→{state.Position}), скорость {flight.state.Velocity}→{state.Velocity}.");
                flight.state=state.Copy();flight.requested=false;
            }
        }
        // Diagnostics (PT-20261103-08): the first impacts of each flight with the surfaces near it (an invisible snag shows
        // which surface it is). Only on impacts, at most ImpactLogLimit per grenade.
        private const int ImpactLogLimit=8;
        private readonly Dictionary<string,int> loggedImpacts=new(StringComparer.Ordinal);
        private void LogImpact(GrenadeState state,Vector2 before,float speed,RoomLayout room)
        {
            loggedImpacts.TryGetValue(state.id,out int count);if(count>=ImpactLogLimit) return;loggedImpacts[state.id]=count+1;
            var box=new Rect(Mathf.Min(before.x,state.x)-.3f,Mathf.Min(before.y,state.y)-.3f,Mathf.Abs(state.x-before.x)+.6f,Mathf.Abs(state.y-before.y)+.6f);
            var near=new System.Text.StringBuilder();int shown=0;
            if(room?.platforms!=null) foreach(var surface in room.platforms)
                if(box.Overlaps(new Rect(surface.x,surface.y,surface.width,surface.height)) && shown++<4)
                    near.Append($" [{surface.x:0.00};{surface.y:0.00} {surface.width:0.00}×{surface.height:0.00}{(surface.oneWay?" односторонняя":"")}{(surface.sourceDoorId!=null?" дверь "+surface.sourceDoorId:"")}]");
            UnityEngine.Debug.Log($"Граната {state.id}: удар {count+1} со скоростью {speed:0.00} м/с, {before}→{state.Position}, скорость теперь {state.Velocity}, "+
                $"осталось {state.remaining:0.00} с, владелец {state.owner} (свой {session.LocalOwner}); поверхности рядом:{(shown==0?" нет":near.ToString())}.");
        }
        private readonly ConnectedPlayers connected=new();
        public void Step(float dt)
        {
            if(session.World==null) return;
            if(session.LocalActor is { } local && !session.Pause.IsPaused(local.level)) throwCooldown.Advance(dt);
            connected.Refresh(session.World);
            foreach(var flight in flights)
            {
                var state=flight.state;
                if(state.owner==null || !connected.OnLevel(state.level)) continue;
                if(session.Pause.IsPaused(state.level)) continue;
                if(state.remaining>0)
                {
                    var before=state.Position;var room=geometry.Get(state.level);
                    float speed=GrenadeRules.Step(state,room,dt);
                    if(speed>0 && state.owner==session.LocalOwner) Impact?.Invoke(state,speed);
                    if(speed>0) LogImpact(state,before,speed,room);
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
