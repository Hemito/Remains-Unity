using UnityEngine;

namespace Remains.Presentation
{
    // A transient emitter for one frame (a muzzle flash): its sprite's bright pixels emit, scaled by the gain.
    public readonly struct SpriteEmission
    {
        public readonly Sprite Sprite;
        public readonly Matrix4x4 LocalToWorld;
        public readonly float Gain;
        public SpriteEmission(Sprite sprite,Matrix4x4 localToWorld,float gain)
        {Sprite=sprite;LocalToWorld=localToWorld;Gain=gain;}
    }

    // A drawn part of an actor or weapon whose silhouette casts a projected shadow (sprite or mesh with its texture).
    public readonly struct LightOccluder
    {
        public readonly Sprite Sprite;
        public readonly Mesh Mesh;
        public readonly Texture Texture;
        public readonly Matrix4x4 LocalToWorld;
        public readonly float Opacity;
        public LightOccluder(Sprite sprite,Matrix4x4 matrix,float opacity=1)
        {Sprite=sprite;Mesh=null;Texture=null;LocalToWorld=matrix;Opacity=opacity;}
        public LightOccluder(Mesh mesh,Texture texture,Matrix4x4 matrix,float opacity=1)
        {Sprite=null;Mesh=mesh;Texture=texture;LocalToWorld=matrix;Opacity=opacity;}
    }
}
