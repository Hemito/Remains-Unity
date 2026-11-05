using System;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class RoomSurface { public float x,y,width,height; public string texture,movementSound; [NonSerialized] public string sourceDoorId; public bool oneWay,shotsPassThrough; public int slopeDirection; }
    public enum LadderFacing { [InspectorName("Влево")] Left=-1, [InspectorName("Вправо")] Right=1 }
    [Serializable] public sealed class RoomLadder { public float x,bottom,top,movementOffset,artworkOffset; public LadderFacing facing=LadderFacing.Right; public string texture="floor",artwork; public bool nativeScale; }
    [Serializable] public sealed class RoomArtwork { public float x,y,width,height,shade=1; public string texture; public int order,slopeDirection; public bool repeat,stretchY,selfIlluminated,backgroundLighting; }
    // Spikes of the level (SpikeCatalog, loc/Trap.as): the hurting box and the danger (locDifLevel) of its room.
    [Serializable] public sealed class RoomSpike { public string id,definition; public float x,y,width,height,danger; }
    // A bed to lie on (BedRest): its box, bottom-left corner, the top is where a resting body lies.
    [Serializable] public sealed class RoomRest
    {
        public string id;public float x,y,width,height;
        // What it is called in the action hint, accusative («лечь на …»); empty — a bed. Its head end is on the right.
        public string label;public bool headRight;
    }
    [Serializable] public sealed class RoomLayout
    {
        public string id,lightingProfile,movementSound;
        public float left,right;
        public float bottom,top=7,spawnX,spawnY;
        public RoomSurface[] platforms;
        public RoomLadder[] ladders;
        public RoomArtwork[] artwork;
        public RoomSpike[] spikes=Array.Empty<RoomSpike>();
        public RoomRest[] beds=Array.Empty<RoomRest>();
        [NonSerialized] public DynamicSurfaceFrame dynamicSurfaces;
        [NonSerialized] public System.Collections.Generic.List<RoomSurface> bodySurfaces;   // BodyTopSurfaces: actor movement only
        [NonSerialized] internal RoomSurfaceIndex surfaceIndex;
        public static RoomLayout Load(string level="factory:0")
            =>Load(level,null);
        public static RoomLayout Load(string level,WorldState world)
        {
            var result=Remains.Content.RoomAuthoring.Load(level,world).CreateLayout();
            if(result.platforms==null || result.ladders==null || result.left>=result.right)
                throw new InvalidOperationException($"Некорректная геометрия комнаты: {result.id}.");
            RoomSurfaces.Prepare(result);
            return result;
        }
    }
}
