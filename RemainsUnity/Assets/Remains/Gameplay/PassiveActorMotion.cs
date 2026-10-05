using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    // Incapacitation removes input, not momentum, gravity or terrain contacts.
    public static class PassiveActorMotion
    {
        public static void Step(ActorState actor,RoomLayout room,float dt,MovementSettings settings,PassengerMotionState passenger=null)
        {
            if(dt<=0 || !ActorSessionRules.IsPassive(actor)) return;
            actor.climbing=false;actor.jumpSupportRemaining=0;
            ActorPose.Select(actor,"down");
            var hull=Anatomy.Movement(actor);
            actor.Position=PassengerMotion.Carry(room,hull,actor.Position,dt,passenger,actor.vy<=0 && !ActorHoldRules.Active(actor));
            var previous=actor.Position;
            bool grounded=ActorTerrainMotion.Supported(room,hull,previous);
            actor.vx=HorizontalMotion.Step(actor.vx,0,grounded,false,dt,settings);
            float vertical=VerticalMotion.Step(actor,false,false,dt,settings);
            var delta=new Vector2(actor.vx*dt,vertical);
            actor.Position=ActorTerrainMotion.Move(room,hull,previous,delta,false);
            if(Mathf.Abs(actor.x-previous.x-delta.x)>.0001f) actor.vx=0;
            if(Mathf.Abs(actor.y-previous.y-delta.y)>.0001f) actor.vy=0;
        }
    }
}
