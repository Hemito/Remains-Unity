using System;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    public static class WorldArtworkMaterial
    {
        private static Shader shader;
        public static float ReadFloat(Renderer renderer,string property)
        {
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            return block.HasFloat(property)?block.GetFloat(property):renderer.sharedMaterial.GetFloat(property);
        }
        public static Material Create(Texture texture)
        {
            if(shader==null) shader=GameAssets.Load<Shader>("Presentation/WorldLit");
            if(shader==null) throw new InvalidOperationException("Нет шейдера освещения Presentation/WorldLit.");
            return new Material(shader) {mainTexture=texture};
        }
        // SpriteRenderer supplies _MainTex per renderer; a serialized material must not retain its source PNG.
        // Mesh-based artwork still uses Create(texture), since its texture belongs to the material.
        public static Material CreateForSpriteRenderer()=>Create(null);
    }
}
