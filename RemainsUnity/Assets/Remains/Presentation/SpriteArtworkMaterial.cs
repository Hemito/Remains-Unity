using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public static class SpriteArtworkMaterial
    {
        private static readonly Dictionary<Texture,Material> materials=new();
        public static void Prepare(Sprite sprite)
        {
            if(!materials.ContainsKey(sprite.texture))
                materials.Add(sprite.texture,WorldArtworkMaterial.Create(sprite.texture));
        }
        public static void Apply(SpriteRenderer renderer,Sprite sprite)
        {
            if(!materials.TryGetValue(sprite.texture,out var material))
                throw new InvalidOperationException($"Материал спрайта не подготовлен: {sprite.texture.name}, объект={renderer.name}.");
            renderer.sharedMaterial=material;renderer.sprite=sprite;
        }
    }
}
