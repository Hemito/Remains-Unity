using System;
using System.IO;
using UnityEngine;

namespace Remains.Content
{
    // A content value, independent of attack range and the firearm's horizontal mount.
    // Version zero with zero radii is explicitly inapplicable to ranged definitions.
    [Serializable]
    public struct HoldingArea
    {
        [InspectorName("Версия области удержания")] public int version;
        [InspectorName("Радиус удержания (м)")] public float radius;
        [InspectorName("Радиус на лестнице (м)")] public float climbingRadius;
        public HoldingArea(float radius,float climbingRadius)
        {version=1;this.radius=radius;this.climbingRadius=climbingRadius;}
        public float RadiusFor(bool climbing)=>climbing?climbingRadius:radius;
        public bool Inapplicable=>version==0 && radius==0 && climbingRadius==0;
        public void Validate(string context)
        {
            if(version!=1 || !float.IsFinite(radius) || radius<=0 ||
                !float.IsFinite(climbingRadius) || climbingRadius<0 || climbingRadius>radius)
                throw new InvalidDataException($"Некорректная область удержания: {context}, version={version}, radius={radius}, climbingRadius={climbingRadius}.");
        }
    }
}
