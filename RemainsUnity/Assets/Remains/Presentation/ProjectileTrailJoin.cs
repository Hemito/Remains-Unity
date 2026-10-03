using UnityEngine;

namespace Remains.Presentation
{
    // Cosmetic launch seam only. The head, collision path and impact never move.
    public sealed class ProjectileTrailJoin
    {
        private bool captured;
        private Vector2 offset;
        public Vector2 Tail(Vector2 origin,Vector2 direction,float headDistance,float tailDistance,float length,bool beam,Vector2? muzzle)
        {
            if(!captured)
            {
                if(!beam && tailDistance>0) muzzle=null; // The first visible trace has already left the launch zone.
                if(muzzle.HasValue && !IsForward(muzzle.Value,origin+direction*headDistance,direction)) return muzzle.Value;
                captured=true;offset=muzzle.HasValue?muzzle.Value-origin:Vector2.zero;
            }
            float weight=beam?1:Mathf.Clamp01(1-tailDistance/Mathf.Max(.001f,length));
            return origin+direction*tailDistance+offset*weight;
        }
        public static bool IsForward(Vector2 tail,Vector2 head,Vector2 direction)=>Vector2.Dot(head-tail,direction)>.00001f;
    }
}
