using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public sealed class EmptySpriteArtworkException : InvalidOperationException
    {
        public EmptySpriteArtworkException(string message):base(message) {}
    }
    public static class SpriteOpaqueBoundsBaker
    {
        public static Rect Measure(Color32[] pixels,int width,Rect rect,Vector2 pivot,float pixelsPerUnit)
        {
            int minX=int.MaxValue,minY=int.MaxValue,maxX=int.MinValue,maxY=int.MinValue;
            for(int y=0;y<(int)rect.height;y++)
            for(int x=0;x<(int)rect.width;x++)
                if(pixels[((int)rect.y+y)*width+(int)rect.x+x].a>0)
                {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x+1);maxY=Math.Max(maxY,y+1);}
            if(minX==int.MaxValue) throw new EmptySpriteArtworkException("Пустой кадр графики: rect="+rect);
            return Rect.MinMaxRect((minX-pivot.x)/pixelsPerUnit,(minY-pivot.y)/pixelsPerUnit,
                (maxX-pivot.x)/pixelsPerUnit,(maxY-pivot.y)/pixelsPerUnit);
        }
        public static Rect Read(Sprite sprite)
        {
            string path=AssetDatabase.GetAssetPath(sprite.texture);
            if(string.IsNullOrEmpty(path)) throw new InvalidOperationException("Нет исходного файла текстуры спрайта: "+sprite.name);
            var readable=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!readable.LoadImage(File.ReadAllBytes(path)) || readable.width!=sprite.texture.width || readable.height!=sprite.texture.height)
                    throw new InvalidOperationException("Исходный размер текстуры не совпадает с импортированным: "+path);
                return Measure(readable.GetPixels32(),readable.width,sprite.rect,sprite.pivot,sprite.pixelsPerUnit);
            }
            finally {UnityEngine.Object.DestroyImmediate(readable);}
        }
    }
}
