namespace Remains.Domain
{
    public static class ActorPose
    {
        public static void CopyMotion(ActorState source,ActorState destination)
        {
            destination.Position=source.Position;destination.Velocity=source.Velocity;
            destination.facing=source.facing;destination.pose=source.pose;destination.poseTime=source.poseTime;
            destination.weapon=source.weapon;destination.weaponAngle=source.weaponAngle;
            destination.weaponTipX=source.weaponTipX;destination.weaponTipY=source.weaponTipY;
            destination.weaponReach=source.weaponReach;
        }
        public static string Clip(ActorState actor)=>actor.down?"down":actor.pose;
        public static void Select(ActorState actor,string pose)
        {
            if(actor.pose!=pose) {actor.pose=pose;actor.poseTime=0;}
        }
    }
}
