using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    [Serializable] public sealed class FactoryModuleResource
    {
        public string textureKey,file,sha256;
        public int pixelWidth,pixelHeight;
        public float exportZoom,sourcePixelsPerMetre;
        public float[] sourceBoundsPixelsYDown;
        public FactoryModuleSampler sampler;
    }
    [Serializable] public sealed class FactoryModuleSampler {public string u,v;}
    [Serializable] public sealed class FactoryModuleSurfaceBinding {public int surfaceIndex;public int[] artworkIndices;}
    public sealed class FactoryModulePhysicalBinding
    {
        public string recordId;
        public PropAsset definition;
        public ContainerLootAsset loot;
        public bool container,explicitlyEmpty;
        public Vector2 anchor;
    }
    public sealed class FactoryModuleDoorBinding
    {
        public string recordId,movementSound;
        public GameObject prefab;
        public Vector2 anchor;
    }
    // Explicit inputs to one authoring command. Saved RoomAuthoring remains runtime truth.
    public sealed class FactoryModuleAuthoringInput
    {
        public int sourceIndex;
        public string sourceId,sourceHash,name,prefabPath,artworkFolder,reportPath,physicalPatch,doorPatch;
        public RoomLayout geometry,decor;
        public RoomArtwork[] additionalArtwork=Array.Empty<RoomArtwork>();
        public FactoryModuleResource[] resources;
        public FactoryModuleSurfaceBinding[] sourceBindings;
        public RoomSourceRecord[] records;
        public FactoryModulePhysicalBinding[] physical;
        public FactoryModuleDoorBinding[] doors;
        public bool installLadderLandings;
        public FactoryLadderLandingRecord[] landingRecords; // Null retains the canonical existing-room contract.
    }
}
