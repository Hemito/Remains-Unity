using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class RoomMeshLifetime : MonoBehaviour
    {
        public readonly List<Mesh> Meshes=new();
        private void OnDestroy()
        {
            foreach(var mesh in Meshes) if(mesh!=null) Destroy(mesh);
        }
    }
}
