namespace Remains.Domain
{
    public static class ActorPose
    {
        public static string Clip(ActorState actor)=>actor.down?"down":actor.pose;
        public static void Select(ActorState actor,string pose)
        {
            if(actor.pose!=pose) {actor.pose=pose;actor.poseTime=0;}
        }
    }
}
