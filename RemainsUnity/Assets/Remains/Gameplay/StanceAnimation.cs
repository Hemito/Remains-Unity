using Remains.Content;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Presentation of an already resolved stance; never changes collision or input.
    public static class StanceAnimation
    {
        public static void Select(ActorState actor,string target,bool wasCrouched)
        {
            if(actor.crouched!=wasCrouched && target==(actor.crouched?"crouch":"idle"))
            {
                ActorPose.Select(actor,actor.crouched?"crouchLower":"crouchRise");
                return;
            }
            bool compatible=actor.pose=="crouchLower" && target=="crouch" || actor.pose=="crouchRise" && target=="idle";
            if(compatible && actor.poseTime<PonyAnimationData.Duration(actor.pose)) return;
            ActorPose.Select(actor,target);
        }
    }
}
