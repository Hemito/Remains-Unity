using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    public sealed class NpcSenses
    {
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly Dictionary<string,NpcPerception> policies=new(StringComparer.Ordinal);
        private readonly Dictionary<string,float> elapsed=new(StringComparer.Ordinal);
        public NpcSenses(CoopSession session,SimulationGeometry geometry)
        {
            this.session=session;this.geometry=geometry;
            session.AttackReceived+=Hear;
            session.Changed+=Reconcile;
            Reconcile();
        }
        private void Reconcile()
        {
            var remove=new List<string>();
            foreach(var id in elapsed.Keys)
            {
                var actor=session.World?.Find(id);
                if(actor==null || actor.owner!=session.LocalOwner || actor.down) remove.Add(id);
            }
            foreach(var id in remove) elapsed.Remove(id);
            if(session.World==null) return;
            foreach(var actor in session.World.actors)
                if(!actor.player && !policies.ContainsKey(actor.actorType))
                {
                    var definition=ActorTypes.Get(actor.actorType);
                    if(definition.combatAi) policies.Add(definition.id,new NpcPerception(definition.perception));
                }
        }
        public void Step(ActorState actor,float dt)
        {
            elapsed.TryGetValue(actor.id,out float seconds);seconds+=dt;
            if(seconds<.1f) {elapsed[actor.id]=seconds;return;}
            elapsed[actor.id]=0;
            if(!policies.TryGetValue(actor.actorType,out var perception))
                throw new InvalidOperationException($"Нет политики восприятия: actor={actor.id}, type={actor.actorType}.");
            string previous=actor.target;
            var room=geometry.Get(actor.level);
            perception.Step(actor,actor.perception,session.World.actors,(from,to)=>RoomVisibility.Clear(room,from,to),seconds);
            if(previous!=actor.target) {actor.revision++;session.PublishActor(actor);}
        }
        private void Hear(AttackEvent attack)
        {
            var source=session.World?.Find(attack.attacker);
            if(source==null || !source.player || !source.connected || session.Pause.IsPaused(attack.level) ||
                WeaponCatalog.Get(attack.weapon).projectileSpeed<=0) return;
            foreach(var actor in session.World.actors)
            {
                if(actor.player || actor.owner!=session.LocalOwner || !policies.TryGetValue(actor.actorType,out var perception)) continue;
                // This prototype knows the emitted sound's point, never the hidden player's subsequent position.
                if(perception.Hear(actor,actor.perception,attack.level,new Vector2(attack.originX,attack.originY)))
                {actor.revision++;session.PublishActor(actor);}
            }
        }
    }
}
