using Remains.Gameplay;
using UnityEngine;

namespace Remains.Presentation
{
    // Bullet.step translates the whole fading laser by the weapon's muzzle delta.
    // This never changes the canonical projectile or its collision endpoint.
    public sealed class BeamMuzzleAttachment
    {
        private readonly Vector2 origin;
        private readonly string weapon,level;
        private readonly int lifecycle;
        public BeamMuzzleAttachment(Remains.Domain.AttackEvent attack,Remains.Domain.ActorState actor)
        {
            // A remote visual can arrive after the muzzle has already moved. Its initial
            // discrepancy must not translate the whole canonical ray when fading starts.
            origin=WeaponPose.Muzzle(actor);weapon=Remains.Content.WeaponRigs.PresentationId(attack.weapon);
            level=attack.level;lifecycle=actor.lifecycleSequence;
        }
        public Vector2 Offset(Remains.Domain.ActorState actor)
            =>actor!=null && actor.level==level && actor.lifecycleSequence==lifecycle && actor.weapon==weapon && !actor.down
                ?WeaponPose.Muzzle(actor)-origin:Vector2.zero;
    }
    // Cosmetic launch seam only. The head, collision path and impact never move.
    public sealed class ProjectileTrailJoin
    {
        private bool captured;
        private Vector2 offset;
        public Vector2 Tail(Vector2 origin,Vector2 direction,float headDistance,float tailDistance,float length,bool beam,Vector2? muzzle)
        {
            if(!captured)
            {
                if(headDistance<=tailDistance) return origin+direction*tailDistance;
                // Treat sub-millimetre departure as the launch seam. Frame-time jitter
                // and float subtraction must not abruptly discard the muzzle offset.
                const float departureTolerance=.001f;
                if(!beam && tailDistance>departureTolerance) muzzle=null;
                if(muzzle.HasValue && !IsForward(muzzle.Value,origin+direction*headDistance,direction)) return muzzle.Value;
                captured=true;offset=muzzle.HasValue?muzzle.Value-origin:Vector2.zero;
            }
            float weight=beam?1:Mathf.Clamp01(1-tailDistance/Mathf.Max(.001f,length));
            return origin+direction*tailDistance+offset*weight;
        }
        public static bool IsForward(Vector2 tail,Vector2 head,Vector2 direction)=>Vector2.Dot(head-tail,direction)>.00001f;
    }
}
