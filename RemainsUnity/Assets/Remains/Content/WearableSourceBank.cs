using System;
namespace Remains.Content
{
    // Exported artwork declarations. Editor compilation is the only consumer of
    // affine registration; runtime views receive ready actor-local meshes.
    [Serializable] public sealed class WearableSourceBank
    {
        public int schema;public string visualId,itemId,anatomy,baseSourceHash;
        public WearableSourceTexture[] textures;public WearableSourceTemplate[] templates;
        public WearableSourceClip[] clips;
    }
    [Serializable] public sealed class WearableSourceTexture
    {public string key,resource,sha256,shape;public int width,height;public float[] bounds;}
    [Serializable] public sealed class WearableSourceTemplate
    {public string id;public int sourceSelectorId,sourceFrameOneBased;public WearableSourcePart[] parts;}
    [Serializable] public sealed class WearableSourcePart
    {
        public string shape,texture,path,slot;public int hairStyle,eyeStyle;
        public float[] matrix,color,bounds;public int[] depthPath;
    }
    [Serializable] public sealed class WearableSourceClip
    {public string id;public WearableSourceFrame[] frames;}
    [Serializable] public sealed class WearableSourceFrame
    {public int[][] baseDepthPaths;public int[] hiddenBasePartIndices;public WearableSourceInstance[] instances;}
    [Serializable] public sealed class WearableSourceInstance
    {
        public string templateId,basePath;public int[] basePartIndices,depthPath;
        public float[] matrix,color;
    }
}
