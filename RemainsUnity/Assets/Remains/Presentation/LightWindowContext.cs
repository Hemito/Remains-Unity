using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Content;
namespace Remains.Presentation
{
    // All state published at a window boundary travels together. A candidate never touches active references.
    internal sealed class LightWindowContext : IDisposable
    {
        public readonly RectInt Rooms;
        public readonly long DoorRevision,LampRevision;
        public readonly LightShaderContext Shader=new();
        public GameObject Root;
        public RoomRadianceField Field;
        public RoomDecorResources Erase;
        public readonly List<(RoomSpriteAuthoring sprite,RoomSpritePlacement placement)> Pictures=new();
        public readonly List<ActorShadowSource> Sources=new();
        public readonly List<(RoomSpriteAuthoring source,bool shown,Sprite image,Sprite erase,Matrix4x4 matrix,int frame,bool dark)> Signatures=new();
        public float[,] Terrain;
        public Vector2 Origin;
        public bool Ready;
        public bool Synchronous;
        public LightWindowContext(RectInt rooms,long door,long lamp) {Rooms=rooms;DoorRevision=door;LampRevision=lamp;}
        public bool Matches(long door,long lamp)=>DoorRevision==door && LampRevision==lamp;
        public void Dispose()
        {
            Shader.Release();
            Field?.ReleaseResources();Erase?.ReleaseResources();
            // Immediate cleanup cannot later clear another owner's globals. Context gates all resource callbacks.
            if(Root!=null) UnityEngine.Object.DestroyImmediate(Root);
            Root=null;Field=null;Erase=null;Pictures.Clear();Sources.Clear();Signatures.Clear();Terrain=null;
        }
    }
}
