using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Remains.Presentation
{
    // Per-source local fields: stationary fires cost no transport between events. Moving lights rebuild only themselves.
    // LRU is a storage budget, never a cap on the number of lights drawn.
    internal sealed class WorldLightFields : IDisposable
    {
        internal sealed class Field
        {public RenderTexture Texture;public Rect World;public RectInt Texels;public int Signature;public bool Dirty;public LinkedListNode<string> Node;}
        public const long Budget=64L*1024*1024;
        private readonly LevelLight light;
        private readonly PolarDirectLight polar;
        private readonly int scale;
        private readonly Dictionary<string,Field> fields=new(StringComparer.Ordinal);
        private readonly LinkedList<string> lru=new();
        private readonly HashSet<string> retained=new(StringComparer.Ordinal);
        private readonly List<string> gone=new();
        public int Builds {get;private set;}
        public long GeometryRevision {get;private set;}
        public long Bytes {get;private set;}
        public WorldLightFields(LevelLight light,int scale)
        {this.light=light;this.scale=scale;polar=light.Direct as PolarDirectLight??throw new ArgumentException("Динамический свет требует полярный backend.");light.GeometryChanged+=Invalidate;}
        public void Begin(){retained.Clear();}
        public Field Get(WorldLightEmission emission)
        {
            Validate(emission);int signature=emission.GeometrySignature();retained.Add(emission.Id);
            var scene=light.Scene;var b=emission.Bounds;var texels=scene.Texels(Rect.MinMaxRect(b.xMin-emission.Reach,b.yMin-emission.Reach,b.xMax+emission.Reach,b.yMax+emission.Reach));
            if(texels.width<=0 || texels.height<=0)return null;
            int width=texels.width*scale,height=texels.height*scale;long bytes=(long)width*height*8;
            if(bytes>Budget)throw new InvalidOperationException($"Поле динамического света {emission.Id} требует {bytes} байт, бюджет={Budget}.");
            fields.TryGetValue(emission.Id,out var field);
            if(field!=null && (field.Texture.width!=width || field.Texture.height!=height)) {Remove(emission.Id);field=null;}
            if(field==null)
            {
                while(Bytes+bytes>Budget)Remove(lru.First.Value);
                var target=new RenderTexture(width,height,0,GraphicsFormat.R16G16B16A16_SFloat){name="Динамический свет: "+emission.Id,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                try{LightTextureBudget.Track(target,bytes);if(!target.Create())throw new InvalidOperationException("Не создано поле "+emission.Id);}
                catch{LightTextureBudget.Release(target);throw;}
                field=new Field{Texture=target,Dirty=true,Node=lru.AddLast(emission.Id)};fields.Add(emission.Id,field);Bytes+=bytes;
            }
            else {lru.Remove(field.Node);lru.AddLast(field.Node);}
            if(field.Dirty || field.Signature!=signature)
            {
                var source=new LevelLightSource(emission.Id,LevelLightSourceKind.Screen,emission.Samples,emission.Radiance,emission.Width,emission.Reach,b,texels,true);
                field.World=new Rect(scene.WorldRect.x+texels.x*scene.Texel,scene.WorldRect.y+texels.y*scene.Texel,texels.width*scene.Texel,texels.height*scene.Texel);field.Texels=texels;
                polar.BuildDetailTransient(source,field.Texture,field.World);field.Signature=signature;field.Dirty=false;Builds++;
            }
            return field;
        }
        public void End()
        {gone.Clear();foreach(var id in fields.Keys)if(!retained.Contains(id))gone.Add(id);foreach(var id in gone)Remove(id);}
        private void Invalidate(IReadOnlyList<RectInt> changed)
        {bool hit=false;foreach(var field in fields.Values)foreach(var rect in changed)if(field.Texels.Overlaps(rect)){field.Dirty=true;hit=true;break;}if(hit)GeometryRevision++;}
        private void Remove(string id)
        {var field=fields[id];Bytes-=(long)field.Texture.width*field.Texture.height*8;LightTextureBudget.Release(field.Texture);lru.Remove(field.Node);fields.Remove(id);}
        private static void Validate(WorldLightEmission e)
        {
            if(string.IsNullOrEmpty(e.Id) || e.Samples==null || e.Samples.Length==0 || e.Samples.Length>DirectLight.MaximumSamples ||
                !float.IsFinite(e.Gain) || e.Gain<0 || !float.IsFinite(e.Width) || e.Width<=0 || !float.IsFinite(e.Reach) || e.Reach<PolarDirectLight.OuterPitch ||
                !float.IsFinite(e.Bounds.x) || !float.IsFinite(e.Bounds.y) || !float.IsFinite(e.Bounds.width) || !float.IsFinite(e.Bounds.height) || e.Bounds.width<=0 || e.Bounds.height<=0 ||
                !float.IsFinite(e.Radiance.r) || !float.IsFinite(e.Radiance.g) || !float.IsFinite(e.Radiance.b) || e.Radiance.r<0 || e.Radiance.g<0 || e.Radiance.b<0)
                throw new ArgumentException("Некорректный динамический источник света: "+e.Id);
            float total=0;foreach(var p in e.Samples){if(!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || p.z<=0)throw new ArgumentException("Некорректная точка динамического света: "+e.Id);total+=p.z;}
            if(Mathf.Abs(total-1)>.001f)throw new ArgumentException($"Динамический свет {e.Id}: сумма весов={total} вместо1.");
        }
        public void Dispose(){light.GeometryChanged-=Invalidate;foreach(var field in fields.Values)LightTextureBudget.Release(field.Texture);fields.Clear();lru.Clear();Bytes=0;}
    }
}
