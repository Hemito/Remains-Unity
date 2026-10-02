using UnityEngine;

namespace Remains.Gameplay
{
    // Observation of the simulated contact, not a damage decision or renderer callback.
    public readonly struct ProjectileContact
    {
        public readonly string projectile,weapon,level,target,targetType,surfaceTexture;
        public readonly Vector2 position;
        public ProjectileContact(string projectile,string weapon,string level,Vector2 position,
            string target,string targetType,string surfaceTexture)
        {
            this.projectile=projectile;this.weapon=weapon;this.level=level;this.position=position;
            this.target=target;this.targetType=targetType;this.surfaceTexture=surfaceTexture;
        }
    }
}
