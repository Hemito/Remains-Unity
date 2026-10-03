using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;

namespace Remains.Presentation
{
    // Presentation state only. Ownership candidates are not combat notifications.
    public sealed class CombatMusicState
    {
        private readonly Dictionary<string,CombatMusicDefinition> bindings=new(StringComparer.Ordinal);
        private string worldId,level;
        private float remaining;
        public string Track {get;private set;}
        public CombatMusicState(IEnumerable<CombatMusicDefinition> definitions)
        {
            foreach(var definition in definitions) bindings.Add(definition.actorType,definition);
        }
        public void SetContext(string world,string area)
        {
            if(worldId==world && level==area) return;
            worldId=world;level=area;Track=null;remaining=0;
        }
        public void ObservePreparation(WorldState world,ActorState actor)
        {
            if(!actor.attackBusy) return;
            var target=world.Find(actor.attackTarget);
            if(target==null || !target.player || !target.connected || target.down || target.level!=level) return;
            ObserveAttack(actor);
        }
        public void ObserveAttack(ActorState actor)
        {
            if(level==null || actor==null || actor.player || actor.down || actor.health<=0 || actor.level!=level) return;
            if(!bindings.TryGetValue(actor.actorType,out var binding)) return; // Optional content binding, not a fallback.
            Track=binding.id;remaining=binding.holdSeconds;
        }
        public bool Step(float seconds,bool paused)
        {
            if(paused || Track==null) return false;
            remaining=Math.Max(0,remaining-seconds);
            if(remaining>0) return false;
            Track=null;return true;
        }
    }
}
