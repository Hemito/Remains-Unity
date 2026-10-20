using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public static class CrouchMotion
    {
        // Stance is simulation state; renderers never choose the movement hull.
        public static void Step(ActorState actor,RoomLayout room,MovementSettings settings,Vector2 input,
            bool jump,bool grounded,bool ladder,bool dropping,float dt)
        {
            bool wantsDown=input.y<-.1f;
            if(!wantsDown) actor.crouchPressConsumed=false;
            // UnitPlayer.downp continues while S operates a ladder. Reaching its landing
            // does not turn the same held key into a fresh crouch press (downp==1).
            else if(ladder || dropping) actor.crouchPressConsumed=true;
            if(!wantsDown || !grounded || ladder || dropping || jump || actor.crouchPressConsumed) actor.crouchPressSeconds=0;
            else actor.crouchPressSeconds+=Mathf.Max(0,dt);
            if(actor.crouched)
            {
                if((input.y>.1f || jump || !grounded || ladder || dropping) &&
                    ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor.anatomy),actor.Position)) actor.crouched=false;
                return;
            }
            if(actor.crouchPressSeconds<=0 || actor.crouchPressConsumed) return;
            float delay=ActorTerrainMotion.CanDrop(room,Anatomy.Movement(actor),actor.Position)?settings.crouchPlatformSeconds:settings.crouchSolidSeconds;
            if(actor.crouchPressSeconds>=delay && ActorTerrainMotion.IsClear(room,Anatomy.Crouching(actor.anatomy),actor.Position)) actor.crouched=true;
        }
    }
}
