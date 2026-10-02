using System;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class RoomSurface { public float x,y,width,height; public string texture; public bool oneWay,shotsPassThrough; }
    [Serializable] public sealed class RoomLadder { public float x,bottom,top,movementOffset,artworkOffset; public string texture="floor",artwork; public bool nativeScale; }
    [Serializable] public sealed class RoomArtwork { public float x,y,width,height,shade=1; public string texture; public int order; public bool repeat,stretchY,selfIlluminated; }
    [Serializable] public sealed class RoomLayout
    {
        public string id,lightingProfile,movementSound;
        public float left,right;
        public float bottom,top=7,spawnX,spawnY;
        public RoomSurface[] platforms;
        public RoomLadder[] ladders;
        public RoomArtwork[] artwork;
        public static RoomLayout Load(string level="factory:0")
        {
            var result=Remains.Content.RoomAuthoring.Load(level).CreateLayout();
            if(result.platforms==null || result.ladders==null || result.left>=result.right)
                throw new InvalidOperationException($"Некорректная геометрия комнаты: {result.id}.");
            return result;
        }
    }
}
