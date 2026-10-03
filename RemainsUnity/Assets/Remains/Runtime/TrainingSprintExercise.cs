using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;
namespace Remains.Runtime
{
    // Uses normal movement commands; never writes actor coordinates or animation time.
    public sealed class TrainingSprintExercise
    {
        private int leg;
        private string previousPose;
        private float previousPhase;
        public int Samples {get;private set;}
        public int BackwardSteps {get;private set;}
        public float LongestContinuousRun {get;private set;}
        public bool Complete=>leg==3;
        public PlayerCommand Step(ActorState actor)
        {
            if(Complete) return default;
            if(actor.pose=="run" && previousPose=="run")
            {
                Samples++;
                if(actor.poseTime+.0001f<previousPhase) BackwardSteps++;
                LongestContinuousRun=Mathf.Max(LongestContinuousRun,actor.poseTime);
            }
            previousPose=actor.pose;previousPhase=actor.poseTime;
            float target=leg==0?16:leg==1?34:26;
            if(Mathf.Abs(target-actor.x)<(leg==2?.08f:.3f))
            {
                leg++;
                if(Complete)
                {
                    if(Samples<20 || LongestContinuousRun<2 || BackwardSteps!=0)
                        throw new InvalidOperationException($"Непрерывный бег: samples={Samples}, longest={LongestContinuousRun}, backwards={BackwardSteps}.");
                    return default;
                }
                target=leg==1?34:26;
            }
            return new PlayerCommand {movement=new Vector2(Mathf.Sign(target-actor.x),0),sprint=leg<2};
        }
    }
}
