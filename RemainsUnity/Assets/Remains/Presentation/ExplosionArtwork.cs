using System;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class ExplosionArtwork
    {
        public string resource;
        public int cellSize,frameCount;
        public float framesPerSecond,pixelsPerUnit;
        [NonSerialized] public Sprite[] frames;
        public float Duration=>frameCount/framesPerSecond;
        public static ExplosionArtwork Load()
        {
            var asset=Resources.Load<TextAsset>("Presentation/explosion-artwork");
            if(asset==null) throw new InvalidOperationException("Нет Presentation/explosion-artwork.");
            var result=JsonUtility.FromJson<ExplosionArtwork>(asset.text);
            if(result.cellSize<=0 || result.frameCount<=0 || !float.IsFinite(result.framesPerSecond) || result.framesPerSecond<=0 ||
                !float.IsFinite(result.pixelsPerUnit) || result.pixelsPerUnit<=0 || string.IsNullOrEmpty(result.resource))
                throw new InvalidOperationException("Некорректное описание кадров взрыва.");
            var texture=Resources.Load<Texture2D>(result.resource);
            if(texture==null || texture.width!=result.cellSize*result.frameCount || texture.height!=result.cellSize)
                throw new InvalidOperationException($"Неверный атлас взрыва: {result.resource}, ожидалось {result.cellSize*result.frameCount}×{result.cellSize}.");
            result.frames=new Sprite[result.frameCount];
            for(int i=0;i<result.frameCount;i++)
            {
                var sprite=Sprite.Create(texture,new Rect(i*result.cellSize,0,result.cellSize,result.cellSize),new Vector2(.5f,.5f),result.pixelsPerUnit);
                SpriteArtworkMaterial.Prepare(sprite);result.frames[i]=sprite;
            }
            return result;
        }
    }
}
