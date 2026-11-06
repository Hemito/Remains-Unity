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
        private readonly HashSet<string> noLightWarned=new(StringComparer.Ordinal);
        private readonly Func<ActorState,float> brightness;
        // How lit a point of a level is on this computer, null without the light of that level.
        public Func<string,Vector2,float?> LevelBrightness {get;set;}
        private string reconciledWorld,reconciledOwner;
        private bool initialized;
        public NpcSenses(CoopSession session,SimulationGeometry geometry)
        {
            this.session=session;this.geometry=geometry;brightness=Brightness;
            session.AttackReceived+=Hear;
            session.Changed+=Reconcile;
            Reconcile();
        }
        private void Reconcile()
        {
            string world=session.World?.worldId,owner=session.LocalOwner;
            bool same=initialized && reconciledWorld==world && reconciledOwner==owner;
            initialized=true;reconciledWorld=world;reconciledOwner=owner;
            if(!same) elapsed.Clear();
            if(same && session.World!=null && session.LastChangedParts!=null && !session.LastChange.Any(WorldChangeKind.Identity))
            {
                foreach(string part in session.LastChangedParts)
                {
                    if(!part.StartsWith("actors:",StringComparison.Ordinal)) continue;
                    string id=part.Substring(7);var actor=session.World.Find(id);
                    if(actor==null || actor.owner!=session.LocalOwner || actor.down) elapsed.Remove(id);
                    PreparePolicy(actor);
                }
                return;
            }
            var remove=new List<string>();
            foreach(var id in elapsed.Keys)
            {
                var actor=session.World?.Find(id);
                if(actor==null || actor.owner!=session.LocalOwner || actor.down) remove.Add(id);
            }
            foreach(var id in remove) elapsed.Remove(id);
            if(session.World==null) return;
            foreach(var actor in session.World.actors)
                PreparePolicy(actor);
        }
        private void PreparePolicy(ActorState actor)
        {
            if(actor==null || actor.player || policies.ContainsKey(actor.actorType)) return;
            var definition=ActorTypes.Get(actor.actorType);
            if(definition.combatAi) policies.Add(definition.id,new NpcPerception(definition.perception));
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
            perception.Step(actor,actor.perception,session.World.actors,(from,to)=>RoomVisibility.Clear(room,from,to),seconds,brightness);
            if(previous!=actor.target) {actor.revision++;session.PublishActor(actor);}
        }
        // The owner of an NPC plays near it, so its computer has that light. Without it the NPC sees as in full light.
        private float Brightness(ActorState subject)
        {
            var read=LevelBrightness?.Invoke(subject.level,subject.BodyPosition+Vector2.up*.8f);
            if(read.HasValue) return read.Value;
            if(noLightWarned.Add(subject.level))
                Debug.LogWarning($"Зрение NPC: на этом компьютере нет света уровня {subject.level}, NPC видят как на свету.");
            return 1;
        }
        public void Alarm(ActorState actor,ActorState attacker)
        {
            if(!policies.TryGetValue(actor.actorType,out var perception))
                throw new InvalidOperationException($"Нет политики восприятия: actor={actor.id}, type={actor.actorType}.");
            var room=geometry.Get(actor.level);
            perception.Alarm(actor,actor.perception,attacker,(from,to)=>RoomVisibility.Clear(room,from,to));
        }
        private void Hear(AttackEvent attack)
        {
            var source=session.World?.Find(attack.attacker);
            // A volley is one sound: its pellets leave from one point (attack.volley names the first shot).
            if(attack.volley!=null || source==null || !source.player || !source.connected || session.Pause.IsPaused(attack.level) ||
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
