using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Presentation
{
    // Explicit aggregate limit: active + one candidate + spare + per-source direct fields.
    // These are render-texture bytes, not a claim about total driver residency or Texture2D CPU copies.
    public static class LightTextureBudget
    {
        public const long MaximumBytes=1536L*1024*1024;
        public const long MaximumSpareBytes=64L*1024*1024;
        private static readonly Dictionary<RenderTexture,long> owned=new();
        public static long Bytes {get;private set;}
        public static int Count=>owned.Count;
        public static long Size(int width,int height,int bytesPerPixel)=>checked((long)width*height*bytesPerPixel);
        public static void Track(RenderTexture texture,long bytes)
        {
            if(texture==null || bytes<=0) throw new ArgumentException("Бюджет света: отсутствует текстура или размер.");
            if(owned.ContainsKey(texture)) throw new InvalidOperationException("Бюджет света: повторное владение текстурой "+texture.name);
            if(bytes>MaximumBytes-Bytes) throw new InvalidOperationException($"Бюджет текстур света превышен: занято={Bytes}, запрос={bytes}, предел={MaximumBytes}, текстура={texture.name}.");
            owned.Add(texture,bytes);Bytes+=bytes;
        }
        public static void Untrack(RenderTexture texture)
        {
            if(ReferenceEquals(texture,null)) return;
            if(owned.Remove(texture,out long bytes)) Bytes-=bytes;
        }
        public static void Release(RenderTexture texture)
        {
            if(ReferenceEquals(texture,null)) return;
            Untrack(texture);
            if(texture==null) return;
            texture.Release();
            if(Application.isPlaying) UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
