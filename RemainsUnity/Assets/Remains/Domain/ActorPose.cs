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
            destination.rest=source.rest;destination.restPose=source.restPose;
            if(!string.IsNullOrEmpty(source.id) && source.id==destination.id && source.owner==destination.owner &&
                source.level==destination.level && source.generation==destination.generation && source.lifecycleSequence==destination.lifecycleSequence)
            {
                destination.crouchPressSeconds=source.crouchPressSeconds;
                destination.crouchPressConsumed=source.crouchPressConsumed;
            }
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
        public const string TacticalProne="tacticalProne";
        public static bool IsTacticallyProne(ActorState actor)=>!actor.player && !actor.down && actor.pose==TacticalProne;
        public static string Clip(ActorState actor)=>ActorSessionRules.IsPassive(actor) || IsTacticallyProne(actor)?"down":
            BedRest.Active(actor)?BedRest.Clip(actor):actor.pose;
        // A clip shown at its last frame: a lying body (down or disconnected) and one resting on a bed.
        public static bool HoldsLastFrame(ActorState actor)=>ActorSessionRules.IsPassive(actor) || BedRest.Active(actor);
        public static bool StationaryClimb(ActorState actor)=>actor.climbing && !ActorSessionRules.IsPassive(actor) && System.Math.Abs(actor.vy)<.001f;
        // Longer than any lying-down clip; the pose stays at its last frame afterwards.
        public const float PassiveSettleSeconds=5;
        public static void Advance(ActorState actor,float dt)
        {
            if(actor.pose=="run" && actor.poseCorrectionRemaining>0)
            {
                // Repay phase lead without freezing a running character's cycle.
                // At least half of each positive simulation step remains visible.
                float held=System.Math.Min(System.Math.Max(0,dt)*.5f,actor.poseCorrectionRemaining);
                actor.poseCorrectionRemaining-=held;dt-=held;
            }
            if(StationaryClimb(actor) || IsTacticallyProne(actor)) {actor.poseTime=0;return;}
            // A passive (down or disconnected) body lies still once its fall has played: its pose time is world state
            // sent with every snapshot, so it must not keep growing.
            if(ActorSessionRules.IsPassive(actor))
            {if(actor.poseTime<PassiveSettleSeconds) actor.poseTime=System.Math.Min(PassiveSettleSeconds,actor.poseTime+dt);return;}
            actor.poseTime+=actor.climbing && !ActorSessionRules.IsPassive(actor) && actor.vy<0?-dt:dt;
        }
        public static void Select(ActorState actor,string pose)
        {
            if(actor.pose!=pose) Restart(actor,pose);
        }
        // A new action can start before its previous visual follow-through ends.
        public static void Restart(ActorState actor,string pose)
        {actor.pose=pose;actor.poseTime=0;actor.poseSequence=unchecked(actor.poseSequence+1);actor.poseCorrectionRemaining=0;}
    }
}
