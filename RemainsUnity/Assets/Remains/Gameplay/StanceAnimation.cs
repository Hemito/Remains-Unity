using Remains.Content;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Presentation of an already resolved stance; never changes collision or input.
    public static class StanceAnimation
    {
        public static void Select(ActorState actor,string target,bool wasCrouched,StandingSupportPose standing=null,RoomLayout room=null,SlopeLocomotionPose slope=null)
        {
            bool standingTarget=target=="idle" || slope!=null && slope.IsStanding(actor,target);
            string landing=Anatomy.LandingPose(actor);
            if(!string.IsNullOrEmpty(landing) && standingTarget)
            {
                if(actor.pose=="jump" || actor.pose==Anatomy.FallPose(actor)) {ActorPose.Select(actor,landing);return;}
                if(actor.pose==landing && actor.poseTime<Anatomy.PoseDuration(actor,landing)) return;
            }
            if(actor.crouched!=wasCrouched && (actor.crouched?target=="crouch":standingTarget))
            {
                ActorPose.Select(actor,actor.crouched?"crouchLower":"crouchRise");
                return;
            }
            bool compatible=actor.pose=="crouchLower" && target=="crouch" || actor.pose=="crouchRise" && standingTarget;
            if(compatible && actor.poseTime<PonyAnimationData.Duration(actor.pose)) return;
            if(slope!=null) target=slope.Select(actor,room,target);
            if(standing!=null) target=standing.Select(actor,room,target);
            ActorPose.Select(actor,target);
        }
    }
}
