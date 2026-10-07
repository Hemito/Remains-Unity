namespace Remains.Domain
{
    public static class ActorPose
    {
        public static void CopyMotion(ActorState source,ActorState destination)
        {
            destination.Position=source.Position;destination.Velocity=source.Velocity;
            destination.facing=source.facing;destination.pose=source.pose;destination.poseTime=source.poseTime;
            destination.motionSequence=source.motionSequence;
            destination.motionSentAt=source.motionSentAt;destination.motionClockGeneration=source.motionClockGeneration;
            destination.poseSequence=source.poseSequence;destination.poseCorrectionRemaining=source.poseCorrectionRemaining;
            destination.climbing=source.climbing;
            destination.crouched=source.crouched;
            destination.jumpSupportRemaining=source.jumpSupportRemaining;
            if(source.weaponSelectionRevision==destination.weaponSelectionRevision)
            {
                bool copyRecoil=WeaponRecoilReplication.SameSelection(source,destination);
                destination.weapon=source.weapon;destination.weaponAngle=source.weaponAngle;
                destination.weaponFlipY=source.weaponFlipY;
                destination.weaponTipX=source.weaponTipX;destination.weaponTipY=source.weaponTipY;
                destination.weaponReach=source.weaponReach;destination.weaponLift=source.weaponLift;
                if(copyRecoil)
                    destination.weaponRecoil=source.weaponRecoil?.Copy()??WeaponRecoilPose.Neutral(-1);
                HeldItemSelection.CopyPose(source,destination);
            }
        }
        public static string Clip(ActorState actor)=>ActorSessionRules.IsPassive(actor)?"down":actor.pose;
        public static bool StationaryClimb(ActorState actor)=>actor.climbing && !ActorSessionRules.IsPassive(actor) && System.Math.Abs(actor.vy)<.001f;
        public static void Advance(ActorState actor,float dt)
        {
            if(actor.pose=="run" && actor.poseCorrectionRemaining>0)
            {
                // Repay phase lead without freezing a running character's cycle.
                // At least half of each positive simulation step remains visible.
                float held=System.Math.Min(System.Math.Max(0,dt)*.5f,actor.poseCorrectionRemaining);
                actor.poseCorrectionRemaining-=held;dt-=held;
            }
            if(StationaryClimb(actor)) {actor.poseTime=0;return;}
            actor.poseTime+=actor.climbing && !ActorSessionRules.IsPassive(actor) && actor.vy<0?-dt:dt;
        }
        public static void Select(ActorState actor,string pose)
        {
            if(actor.pose!=pose) {actor.pose=pose;actor.poseTime=0;actor.poseSequence=unchecked(actor.poseSequence+1);actor.poseCorrectionRemaining=0;}
        }
    }
}
