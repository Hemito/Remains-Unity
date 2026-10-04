using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    // Navigation commands for the authored room, never actor-state injection.
    public sealed class JourneyHubRoute
    {
        private int stage;
        public PlayerCommand Command(ActorState actor,float target,RoomLayout room)
        {
            bool landed=!actor.climbing && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
            if(stage==0 && actor.y<1 && landed) stage=2;
            if(stage==0 && Mathf.Abs(actor.x-14.48f)<.05f) stage=1;
            if(stage==1 && actor.y<1 && landed) stage=2;
            float x=stage==0?14.48f:stage==1?actor.x:target;
            return new PlayerCommand {movement=stage==1?Vector2.down:
                Vector2.right*(Mathf.Abs(actor.x-x)>.05f?Mathf.Sign(x-actor.x):0)};
        }
    }
}
