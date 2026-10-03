using System;
using UnityEngine;

namespace Remains.Presentation
{
    // Immutable CPU pixels, prepared once while the room light map is built.
    public sealed class RoomLightMask
    {
        private readonly Color[] pixels;
        private readonly int width,height;
        private readonly Rect bounds;
        public RoomLightMask(Texture2D texture,Rect bounds)
        {
            if(texture==null || !texture.isReadable || !float.IsFinite(bounds.x) || !float.IsFinite(bounds.y) ||
                !float.IsFinite(bounds.width) || !float.IsFinite(bounds.height) || bounds.width<=0 || bounds.height<=0)
                throw new InvalidOperationException($"Некорректная маска света: texture={texture?.name}, bounds={bounds}. Требуется читаемая текстура.");
            pixels=texture.GetPixels();width=texture.width;height=texture.height;this.bounds=bounds;
            // Interpolate premultiplied colour so invisible RGB cannot tint the edge.
            for(int i=0;i<pixels.Length;i++)
            {
                var pixel=pixels[i];pixels[i]=new Color(pixel.r*pixel.a,pixel.g*pixel.a,pixel.b*pixel.a,pixel.a);
            }
        }
        public Color Sample(Vector2 point)
        {
            if(!bounds.Contains(point)) return Color.clear;
            float x=Mathf.Clamp((point.x-bounds.x)/bounds.width*width-.5f,0,width-1);
            float y=Mathf.Clamp((point.y-bounds.y)/bounds.height*height-.5f,0,height-1);
            int left=Mathf.FloorToInt(x),bottom=Mathf.FloorToInt(y),right=Mathf.Min(left+1,width-1),top=Mathf.Min(bottom+1,height-1);
            return Color.Lerp(Color.Lerp(pixels[bottom*width+left],pixels[bottom*width+right],x-left),
                Color.Lerp(pixels[top*width+left],pixels[top*width+right],x-left),y-bottom);
        }
    }
}
