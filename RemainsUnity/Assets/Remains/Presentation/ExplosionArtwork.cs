using System;
using Remains.Content;
using UnityEngine;
namespace Remains.Presentation
{
    public sealed class ExplosionArtwork
    {
        public float framesPerSecond;
        public Sprite[] frames;
        public float Duration=>frames.Length/framesPerSecond;
        public static ExplosionArtwork Load()
        {
            var asset=Resources.Load<ExplosionAnimationAsset>("Presentation/explosion-animation");
            if(asset==null) throw new InvalidOperationException("Нет каталога Presentation/explosion-animation.");
            var result=asset.CreateArtwork();
            foreach(var sprite in result.frames) SpriteArtworkMaterial.Prepare(sprite);
            return result;
        }
    }
}
