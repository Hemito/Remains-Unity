using UnityEngine;

namespace Remains.Presentation
{
    // Angular shape is a composition property: aiming a stationary source does not rebuild its polar transport.
    // The default value explicitly denotes an omnidirectional emitter.
    public readonly struct LightEmissionCone
    {
        public readonly Vector2 Direction;
        public readonly float InnerCos,OuterCos;
        public bool Directed=>Direction.sqrMagnitude>.5f;
        public Vector4 ShaderValue=>new(Direction.x,Direction.y,OuterCos,InnerCos);
        public LightEmissionCone(Vector2 direction,float innerHalfAngleDegrees,float outerHalfAngleDegrees)
        {
            if(!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude<.000001f ||
                !float.IsFinite(innerHalfAngleDegrees) || !float.IsFinite(outerHalfAngleDegrees) ||
                innerHalfAngleDegrees<0 || outerHalfAngleDegrees<=innerHalfAngleDegrees || outerHalfAngleDegrees>=180)
                throw new System.ArgumentException("Конус света: нужны конечное направление и углы 0 <= внутренний < внешний < 180 градусов.");
            Direction=direction.normalized;InnerCos=Mathf.Cos(innerHalfAngleDegrees*Mathf.Deg2Rad);OuterCos=Mathf.Cos(outerHalfAngleDegrees*Mathf.Deg2Rad);
        }
        public float Weight(Vector2 offset)
        {
            if(!Directed || offset.sqrMagnitude<.000001f)return 1;
            float t=Mathf.Clamp01((Vector2.Dot(Direction,offset.normalized)-OuterCos)/(InnerCos-OuterCos));
            return t*t*(3-2*t);
        }
    }
    public static class LightEmissionGeometry
    {
        public static float AreaScale(Matrix4x4 transform)=>Mathf.Abs(transform.m00*transform.m11-transform.m01*transform.m10);
        public static Rect WorldBounds(Matrix4x4 transform,Bounds bounds)
        {
            Vector2 a=transform.MultiplyPoint3x4(bounds.min),b=transform.MultiplyPoint3x4(bounds.max);
            Vector2 c=transform.MultiplyPoint3x4(new Vector3(bounds.min.x,bounds.max.y,0)),d=transform.MultiplyPoint3x4(new Vector3(bounds.max.x,bounds.min.y,0));
            var min=Vector2.Min(Vector2.Min(a,b),Vector2.Min(c,d));var max=Vector2.Max(Vector2.Max(a,b),Vector2.Max(c,d));
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
    }
    // Physical presentation emitter: positions come from the simulation, not flame animation frames.
    // Samples and geometry stay cached; Gain alone may fade without rebuilding the field.
    public readonly struct WorldLightEmission
    {
        public readonly string Id;
        public readonly Vector4[] Samples;
        public readonly Rect Bounds;
        public readonly Color Radiance;
        public readonly float Width,Reach,Gain;
        public readonly LightEmissionCone Cone;
        public WorldLightEmission(string id,Vector4[] samples,Rect bounds,Color radiance,float width,float reach,float gain=1,LightEmissionCone cone=default)
        {Id=id;Samples=samples;Bounds=bounds;Radiance=radiance;Width=width;Reach=reach;Gain=gain;Cone=cone;}
        public Vector2 Position=>Bounds.center;
        public int CompositionSignature()=>System.HashCode.Combine(GeometrySignature(),Cone.Direction,Cone.InnerCos,Cone.OuterCos,Gain);
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
