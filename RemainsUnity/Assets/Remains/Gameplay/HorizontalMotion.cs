using Remains.Content;
using UnityEngine;

namespace Remains.Gameplay
{
    // Velocity relaxation in seconds; independent of the original game's tick loop.
    public static class HorizontalMotion
    {
        public static float Step(float velocity,float input,bool grounded,bool sprint,float dt,MovementSettings settings)
        {
            float target=Mathf.Clamp(input,-1,1)*(grounded && sprint?settings.runSpeed:settings.walkSpeed);
            bool slowing=Mathf.Abs(target)<.001f || Mathf.Sign(target)==Mathf.Sign(velocity) && Mathf.Abs(velocity)>Mathf.Abs(target);
            float rate=grounded?(slowing?settings.groundBraking:settings.groundAcceleration):
                (slowing?settings.airBraking:settings.airAcceleration);
            return Mathf.MoveTowards(velocity,target,rate*Mathf.Max(0,dt));
        }
    }
}
