using UnityEngine;

namespace Remains.Domain
{
    public readonly struct MechanismInteractionPoint
    {
        public readonly MechanismButtonState Button;
        public readonly Rect Bounds;
        public readonly float Radius;
        public MechanismInteractionPoint(MechanismButtonState button,Rect bounds,float radius)
        {Button=button;Bounds=bounds;Radius=radius;}
    }
}
