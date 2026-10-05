using System;
using UnityEngine;

namespace Remains.Presentation
{
    // Integrates dC/dt = rate * (T - C), with T moving linearly between
    // two confirmed samples. This is presentation history, never actor motion.
    public sealed class CameraFollowMotion
    {
        public const float DefaultResponseRate=8;
        private readonly float responseRate;
        private bool hasPreviousTarget;
        private Vector3 previousTarget;

        public CameraFollowMotion(float responseRate=DefaultResponseRate)
        {
            if(!float.IsFinite(responseRate) || responseRate<=0)
                throw new ArgumentOutOfRangeException(nameof(responseRate),"Скорость следования камеры должна быть положительной и конечной.");
            this.responseRate=responseRate;
        }
        // Reset only sample history. The caller retains the actual camera center.
        public void Reset()=>hasPreviousTarget=false;

        public Vector3 Step(Vector3 center,Vector3 target,float dt)
        {
            if(!Finite(center) || !Finite(target) || !float.IsFinite(dt) || dt<0)
                throw new ArgumentException($"Неверный шаг камеры: center={center}, target={target}, dt={dt}.");
            if(!hasPreviousTarget) previousTarget=target;
            if(dt==0) {previousTarget=target;hasPreviousTarget=true;return center;}
            double interval=(double)responseRate*dt;
            double oldTargetWeight,travelWeight;
            if(interval<.001)
            {
                // Avoid cancellation in 1-exp(-x) and 1-(1-exp(-x))/x.
                double squared=interval*interval,cubed=squared*interval,fourth=cubed*interval;
                oldTargetWeight=interval-squared/2+cubed/6-fourth/24;
                travelWeight=interval/2-squared/6+cubed/24-fourth/120;
            }
            else
            {
                oldTargetWeight=1-Math.Exp(-interval);
                travelWeight=1-oldTargetWeight/interval;
            }
            var result=new Vector3(
                Integrate(center.x,previousTarget.x,target.x,oldTargetWeight,travelWeight),
                Integrate(center.y,previousTarget.y,target.y,oldTargetWeight,travelWeight),
                Integrate(center.z,previousTarget.z,target.z,oldTargetWeight,travelWeight));
            previousTarget=target;hasPreviousTarget=true;
            return result;
        }
        private static float Integrate(float center,float previous,float current,double oldWeight,double travelWeight)
            =>(float)(center+((double)previous-center)*oldWeight+((double)current-previous)*travelWeight);
        private static bool Finite(Vector3 value)=>float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
