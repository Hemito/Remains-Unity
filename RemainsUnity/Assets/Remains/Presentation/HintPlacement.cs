using UnityEngine;
namespace Remains.Presentation
{
    public static class HintPlacement
    {
        // All inputs use the same canvas-local coordinates.
        public static bool TryPlace(Vector2 preferred,Vector2 size,Rect area,Rect actor,out Vector2 result)
        {
            const float gap=8;
            result=preferred;
            if(size.x>area.width || size.y>area.height) return false;
            var half=size*.5f;
            Vector2 Clamp(Vector2 point)=>new Vector2(Mathf.Clamp(point.x,area.xMin+half.x,area.xMax-half.x),Mathf.Clamp(point.y,area.yMin+half.y,area.yMax-half.y));
            bool Clear(Vector2 point)=>!new Rect(point-half,size).Overlaps(actor);
            result=Clamp(preferred);if(Clear(result)) return true;
            result=Clamp(new Vector2(preferred.x,actor.yMax+gap+half.y));if(Clear(result)) return true;
            result=Clamp(new Vector2(preferred.x,actor.yMin-gap-half.y));if(Clear(result)) return true;
            result=Clamp(new Vector2(actor.xMax+gap+half.x,preferred.y));if(Clear(result)) return true;
            result=Clamp(new Vector2(actor.xMin-gap-half.x,preferred.y));return Clear(result);
        }
    }
}
