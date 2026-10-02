using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Continuous recovery of the 0..100 availability scale. No legacy tick algorithm.
    public sealed class MagicRecovery
    {
        private string actorId;
        private int generation,lifecycle;
        private float speed;
        public bool Step(ActorState actor,bool holding,float acceleration,float dt)
        {
            if(actor.id!=actorId || actor.generation!=generation || actor.lifecycleSequence!=lifecycle)
            {actorId=actor.id;generation=actor.generation;lifecycle=actor.lifecycleSequence;speed=0;}
            if(holding || actor.mana>=100) {speed=0;return false;}
            if(dt<=0 || acceleration<=0) return false;
            float previous=actor.mana;
            actor.mana=Mathf.Min(100,actor.mana+speed*dt+.5f*acceleration*dt*dt);
            speed+=acceleration*dt;
            if(actor.mana>=100) speed=0;
            return actor.mana!=previous;
        }
    }
}
