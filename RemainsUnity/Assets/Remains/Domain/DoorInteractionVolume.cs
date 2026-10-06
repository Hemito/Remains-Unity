using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class DoorInteractionVolume : ExtensibleState
    {
        public int version=1;
        public float offsetX,offsetY,width,height;
        public Rect BoundsFrom(Rect physical)=>new(physical.x+offsetX,physical.y+offsetY,width,height);
        public void Validate(Rect physical,string context)
        {
            var bounds=BoundsFrom(physical);
            if(version!=1 || !float.IsFinite(offsetX) || !float.IsFinite(offsetY) || !float.IsFinite(width) || !float.IsFinite(height) || width<=0 || height<=0 ||
                !float.IsFinite(bounds.xMin) || !float.IsFinite(bounds.yMin) || !float.IsFinite(bounds.xMax) || !float.IsFinite(bounds.yMax))
                throw new InvalidOperationException($"Некорректная область взаимодействия проёма {context}: version={version}, offset=({offsetX},{offsetY}), size=({width},{height}).");
        }
    }
}
