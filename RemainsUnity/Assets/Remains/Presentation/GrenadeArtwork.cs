using System;
using UnityEngine;

namespace Remains.Presentation
{
    public static class GrenadeArtwork
    {
        // visgrenade 4142, frame 1, vector export at 4x. Original scene scale: 50 px/m.
        // Retain transparent margins; they must not determine the visible object's size.
        private const float PixelsPerUnit=4*50;
        public static Sprite Load()
        {
            const string path="Original/Props/grenade-flight";
            var texture=Resources.Load<Texture2D>(path);
            if(texture==null) throw new InvalidOperationException($"Нет графики гранаты: {path}.");
            return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),PixelsPerUnit);
        }
    }
}
