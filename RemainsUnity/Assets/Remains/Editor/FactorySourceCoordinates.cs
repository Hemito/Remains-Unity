using UnityEngine;
namespace Remains.Editor
{
    // Independent binding of researched cell coordinates to the existing Unity modules.
    internal static class FactorySourceCoordinates
    {
        public static Vector2 Placement(float x,float y,float cells)=>new((x+cells*.5f)*.8f,(24-y)*.8f);
    }
}
