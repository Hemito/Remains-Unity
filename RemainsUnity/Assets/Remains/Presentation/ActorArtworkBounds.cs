using System;
using UnityEngine;

namespace Remains.Presentation
{
    // Real attached sprite only; no reach, attack sweep, glow or projectile envelope.
    public static class ActorArtworkBounds
    {
        public static Rect? VisibleSprite(SpriteRenderer artwork)
        {
            if(artwork==null || !artwork.enabled || artwork.forceRenderingOff || !artwork.gameObject.activeInHierarchy || artwork.sprite==null || artwork.color.a<=0) return null;
            return WorldRect(artwork.bounds);
        }
        public static Rect WorldRect(Bounds bounds)
        {
            var minimum=bounds.min;var maximum=bounds.max;
            if(!float.IsFinite(minimum.x) || !float.IsFinite(minimum.y) || !float.IsFinite(maximum.x) || !float.IsFinite(maximum.y) || maximum.x<=minimum.x || maximum.y<=minimum.y)
                throw new InvalidOperationException("Изображение персонажа: некорректные фактические границы "+bounds);
            return Rect.MinMaxRect(minimum.x,minimum.y,maximum.x,maximum.y);
        }
    }
}
