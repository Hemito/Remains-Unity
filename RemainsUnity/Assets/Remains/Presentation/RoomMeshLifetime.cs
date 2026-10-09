using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class RoomMeshLifetime : MonoBehaviour
    {
        public readonly List<Mesh> Meshes=new();
        public readonly Dictionary<string,Material> Materials=new();
        public readonly List<Texture2D> Textures=new();
        private void OnDestroy()
        {
            foreach(var mesh in Meshes) if(mesh!=null) {if(Application.isPlaying) Destroy(mesh);else DestroyImmediate(mesh);}
            foreach(var texture in Textures) if(texture!=null) {if(Application.isPlaying) Destroy(texture);else DestroyImmediate(texture);}
            foreach(var material in Materials.Values) if(material!=null) {if(Application.isPlaying) Destroy(material);else DestroyImmediate(material);}
        }
    }
}
