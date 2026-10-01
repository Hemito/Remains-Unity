using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public static class VerticalMotion
    {
        // Integrate a bounded sustained ascent followed by a ballistic segment.
        public static float Step(ActorState actor,bool launch,bool held,float dt,MovementSettings settings)
        {
            if(launch)
            {
                actor.vy=settings.jumpSpeed;
                actor.jumpSupportRemaining=held?settings.jumpSupportSeconds:0;
            }
            if(!held || actor.vy<=0) actor.jumpSupportRemaining=0;
            float supported=Mathf.Min(Mathf.Max(0,actor.jumpSupportRemaining),dt);
            actor.jumpSupportRemaining=Mathf.Max(0,actor.jumpSupportRemaining-supported);
            float ballistic=dt-supported;
            float distance=actor.vy*dt-.5f*settings.actorGravity*ballistic*ballistic;
            actor.vy-=settings.actorGravity*ballistic;
            return distance;
        }
    }
}
