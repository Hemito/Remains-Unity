using System;
using System.IO;
using UnityEngine;

namespace Remains.Content
{
    [Serializable]
    public sealed class ThrowableLightProfile
    {
        public Color radiance;
        public float reach,width;
        public bool contactProjectile;
        public bool Configured=>reach>0;
        public void Validate(WeaponDefinition weapon,string source)
        {
            if(!float.IsFinite(reach)||reach<=0||reach>12||!float.IsFinite(width)||width<=0||width>1||
                !float.IsFinite(radiance.r)||!float.IsFinite(radiance.g)||!float.IsFinite(radiance.b)||
                radiance.r<0||radiance.g<0||radiance.b<0||Mathf.Max(radiance.r,Mathf.Max(radiance.g,radiance.b))<=0||
                weapon.explosive||weapon.fire?.Configured==true||weapon.grenadeFuseSeconds<=0||weapon.projectileSpeed!=0||
                (contactProjectile?weapon.damage!=1:weapon.damage!=0))
                throw new InvalidDataException($"Некорректный светящийся метательный предмет: source={source}, weapon={weapon.id}.");
        }
    }
}
