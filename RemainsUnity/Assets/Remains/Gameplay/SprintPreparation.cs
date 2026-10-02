using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Transient input history, measured in simulation seconds rather than render frames.
    public sealed class SprintPreparation
    {
        private string actorId;
        private int generation,lifecycle;
        private float heldSeconds;

        public float AccelerationTime(ActorState actor,float horizontal,bool sprint,float dt,float delay)
        {
            if(actor.id!=actorId || actor.generation!=generation || actor.lifecycleSequence!=lifecycle)
            {actorId=actor.id;generation=actor.generation;lifecycle=actor.lifecycleSequence;heldSeconds=0;}
            dt=Mathf.Max(0,dt);
            if(actor.down || Mathf.Abs(horizontal)<.001f) {heldSeconds=0;return dt;}
            float waiting=Mathf.Max(0,delay-heldSeconds);
            heldSeconds=Mathf.Min(delay,heldSeconds+dt);
            return sprint?Mathf.Max(0,dt-waiting):dt;
        }
    }
}
