using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Gameplay
{
    // Independent launch model: cursor effort controls kinetic energy, not a landing target.
    // No frame clock and no original-game implementation are used here.
    public static class GrenadeLaunch
    {
        public static float AngularVelocity(Vector2 velocity,float facing,WeaponDefinition definition)
        {
            float direction=velocity.x==0?facing:Mathf.Sign(velocity.x);
            return -direction*definition.grenadeSpinRate*Mathf.Clamp01(velocity.magnitude/definition.grenadeMaxLaunchSpeed);
        }
        public static Vector2 Velocity(Vector2 origin,Vector2 aim,float feetY,WeaponDefinition definition)
        {
            var offset=aim-origin;
            if(!float.IsFinite(offset.sqrMagnitude) || !float.IsFinite(feetY))
                throw new ArgumentException("Бросок гранаты: неконечная позиция или точка прицеливания.");
            double effort=Math.Abs((double)offset.x)+Math.Max(0,(double)aim.y-feetY);
            if(effort==0 || offset==Vector2.zero) return Vector2.zero;
            float fraction=(float)Math.Min(1,effort/definition.grenadeFullEffortDistance);
            float speed=definition.grenadeMaxLaunchSpeed*Mathf.Sqrt(fraction);
            // Horizontal throws arc upwards; a vertical aim retains its direction.
            var direction=new Vector2(offset.x,offset.y+Mathf.Abs(offset.x)*definition.grenadeAimLift);
            return direction.normalized*speed;
        }
    }
}
