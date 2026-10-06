using System;
using System.Threading;
using UnityEngine;

namespace Remains.Presentation
{
    // Immutable CPU pixels, prepared once while the room light map is built.
    public sealed class RoomLightMask
    {
        private readonly Pixels source;
        private readonly Rect bounds;
        public RoomLightMask(Texture2D texture,Rect bounds):this(new Pixels(texture),bounds) { }
        internal RoomLightMask(Pixels source,Rect bounds)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            ValidateBounds(bounds);
            this.source=source;this.bounds=bounds;
        }
        internal static void ValidateBounds(Rect bounds)
        {
            if(!float.IsFinite(bounds.x) || !float.IsFinite(bounds.y) || !float.IsFinite(bounds.width) ||
                !float.IsFinite(bounds.height) || bounds.width<=0 || bounds.height<=0)
                throw new InvalidOperationException($"Некорректная привязка маски света: bounds={bounds}.");
        }
        public Color Sample(Vector2 point)
        {
            if(!bounds.Contains(point)) return Color.clear;
            var pixels=source.Values;int width=source.Width,height=source.Height;
            float x=Mathf.Clamp((point.x-bounds.x)/bounds.width*width-.5f,0,width-1);
            float y=Mathf.Clamp((point.y-bounds.y)/bounds.height*height-.5f,0,height-1);
            int left=Mathf.FloorToInt(x),bottom=Mathf.FloorToInt(y),right=Mathf.Min(left+1,width-1),top=Mathf.Min(bottom+1,height-1);
            return Color.Lerp(Color.Lerp(pixels[bottom*width+left],pixels[bottom*width+right],x-left),
                Color.Lerp(pixels[top*width+left],pixels[top*width+right],x-left),y-bottom);
        }
        // Raw capture alone touches Unity objects. The single-use build owns this copy.
        internal sealed class RawPixels
        {
            internal readonly Color[] Values;
            internal readonly int Width,Height;
            internal readonly bool Decode;
            internal readonly double GetPixelsMilliseconds;
            internal RawPixels(Texture2D texture)
            {
                if(texture==null || !texture.isReadable)
                    throw new InvalidOperationException($"Некорректная маска света: texture={texture?.name}. Требуется читаемая текстура.");
                Width=texture.width;Height=texture.height;
                Decode=QualitySettings.activeColorSpace==ColorSpace.Linear &&
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
                var timer=System.Diagnostics.Stopwatch.StartNew();Values=texture.GetPixels();
                GetPixelsMilliseconds=timer.Elapsed.TotalMilliseconds;
            }
        }
        // One immutable decoded buffer serves differently placed fixtures and both maps.
        // Decoding transfers ownership of a private raw array; it never accesses Texture.
        internal sealed class Pixels
        {
            internal readonly Color[] Values;
            internal readonly int Width,Height;
            internal Pixels(Texture2D texture):this(new RawPixels(texture),default) { }
            internal Pixels(RawPixels raw,CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                Values=raw.Values;Width=raw.Width;Height=raw.Height;
                for(int i=0;i<Values.Length;i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var pixel=Values[i];
                    if(raw.Decode) pixel=pixel.linear;
                    Values[i]=new Color(pixel.r*pixel.a,pixel.g*pixel.a,pixel.b*pixel.a,pixel.a);
                }
            }
        }
    }
}
