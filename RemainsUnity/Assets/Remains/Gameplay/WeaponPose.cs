using UnityEngine;
using Remains.Content;

namespace Remains.Gameplay
{
    public static class WeaponPose
    {
        public static Vector2 Tip(string weapon,Vector2 origin,Vector2 direction,float progress)
        {
            float range=WeaponCatalog.Get(weapon).range;
            if(weapon=="spear") return origin+direction*Mathf.Lerp(0.35f,range,Mathf.Sin(progress*Mathf.PI));
            float angle=Mathf.Lerp(-70,70,progress)*Mathf.Deg2Rad;
            return origin+new Vector2(direction.x*Mathf.Cos(angle)-direction.y*Mathf.Sin(angle),
                direction.x*Mathf.Sin(angle)+direction.y*Mathf.Cos(angle))*range;
        }
    }
}
