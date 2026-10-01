using System;
using UnityEngine;

namespace Remains.Presentation
{
    public static class WorldArtworkMaterial
    {
        private static Shader shader;
        public static Material Create(Texture texture)
        {
            if(shader==null) shader=Resources.Load<Shader>("Presentation/WorldLit");
            if(shader==null) throw new InvalidOperationException("Нет шейдера освещения Presentation/WorldLit.");
            return new Material(shader) {mainTexture=texture};
        }
    }
}
