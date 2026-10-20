using UnityEngine;

namespace Remains.Domain
{
    public static class TelekinesisFollow
    {
        public const float ResponseRate=8;
        public static Vector2 Displacement(Vector2 origin,Vector2 target,float dt)
            =>(target-origin)*(1-Mathf.Exp(-ResponseRate*dt));
    }
}
