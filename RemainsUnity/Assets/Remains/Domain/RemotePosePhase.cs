namespace Remains.Domain
{
    // A late sample can briefly hold a run frame, but cannot replay one already shown.
    // The hold is consumed in simulation time, so repeated corrections do not accumulate lead.
    public static class RemotePosePhase
    {
        public static void Apply(ActorState actor,string pose,uint sequence,float time)
        {
            float hold=Hold(actor,pose,sequence,time);
            actor.pose=pose;actor.poseSequence=sequence;actor.poseTime=time+hold;
            actor.poseCorrectionRemaining=hold;
        }
        public static void Reconcile(ActorState current,ActorState incoming)
        {
            if(!ActorMotionReplication.SameLifetime(current,incoming) || incoming.down ||
                !ActorMotionReplication.IsNewer(incoming.motionSequence,current.motionSequence)) return;
            float hold=Hold(current,incoming.pose,incoming.poseSequence,incoming.poseTime);
            incoming.poseTime+=hold;incoming.poseCorrectionRemaining=hold;
        }
        private static float Hold(ActorState current,string pose,uint sequence,float time)=>
            !current.down && current.pose=="run" && pose=="run" && current.poseSequence==sequence
                ?System.Math.Max(0,current.poseTime-time):0;
    }
}
