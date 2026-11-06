using UnityEngine;

namespace Remains.Presentation
{
    // Physical presentation emitter: positions come from the simulation, not flame animation frames.
    // Samples and geometry stay cached; Gain alone may fade without rebuilding the field.
    public readonly struct WorldLightEmission
    {
        public readonly string Id;
        public readonly Vector4[] Samples;
        public readonly Rect Bounds;
        public readonly Color Radiance;
        public readonly float Width,Reach,Gain;
        public WorldLightEmission(string id,Vector4[] samples,Rect bounds,Color radiance,float width,float reach,float gain=1)
        {Id=id;Samples=samples;Bounds=bounds;Radiance=radiance;Width=width;Reach=reach;Gain=gain;}
        public Vector2 Position=>Bounds.center;
        public int GeometrySignature()
        {
            var hash=new System.HashCode();hash.Add(Id);hash.Add(Bounds);hash.Add(Radiance);hash.Add(Width);hash.Add(Reach);
            if(Samples!=null)foreach(var p in Samples)hash.Add(p);
            return hash.ToHashCode();
        }
    }
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
