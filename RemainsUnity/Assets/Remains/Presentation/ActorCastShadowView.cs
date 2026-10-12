using System;
using System.Collections.Generic;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    public readonly struct ProjectedLightShadow
    {
        public readonly string SourceId;
        public readonly Texture Mask;
        public readonly Rect WorldBounds;
        public readonly float Opacity;
        public readonly int Revision;
        public ProjectedLightShadow(string sourceId,Texture mask,Rect worldBounds,float opacity,int revision=0)
        {SourceId=sourceId;Mask=mask;WorldBounds=worldBounds;Opacity=opacity;Revision=revision;}
    }
    // A presentation-only projection: the source actor meshes and world transforms are never modified.
    // Body/weapon alpha is unioned once per pose. Every source projects onto a separate wall plane;
    // masks remove only their own direct-light contribution, never a black overlay.
    public sealed class ActorCastShadowView : MonoBehaviour
    {
        public const string ResourcePath="Presentation/ActorCastShadow";
        public const int ShadowOrder=1,MaskResolution=256;
        [SerializeField,InspectorName("Материал маски и тени")] private Material shadowMaterial;
        private readonly List<Slot> slots=new();
        private readonly Dictionary<Sprite,Mesh> spriteMeshes=new();
        private readonly HashSet<int> warned=new();
        private MaterialPropertyBlock properties;
        private Material maskMaterial;
        private int lastSignature;
        private bool hasSignature;
        private RenderTexture poseMask;
        private Bounds poseBounds;
        private int lastPoseSignature;
        private bool hasPoseMask;
        public int MaskRebuilds {get;private set;}
        public int PoseMaskRebuilds {get;private set;}
        private sealed class Slot
        {
            public GameObject root;
            public MeshRenderer renderer;
            public MeshFilter filter;
            public Mesh mesh;
            public RenderTexture mask;
            public readonly Vector3[] vertices=new Vector3[4];
            public Bounds lastBounds;
            public Matrix4x4 lastInverse;
            public bool hasQuad;
            public bool transient;
            public string sourceId;
            public float coverage;
        }
        public int ActiveSourceCount {get;private set;}
        public void Configure(Material material)
        {shadowMaterial=material;Validate();}
        public void Validate()
        {
            if(shadowMaterial==null || shadowMaterial.shader==null || shadowMaterial.passCount<4)
                throw new InvalidOperationException($"Проекция тени {name}: нет авторского материала с проходами силуэта, протяжённой тени и композиции.");
        }

        public static Matrix4x4 Projection(Vector2 actorPosition,Vector2 sourcePosition,float depth,float maxOffset)
        {
            Vector2 offset=Vector2.ClampMagnitude((actorPosition-sourcePosition)*Mathf.Max(0,depth),Mathf.Max(0,maxOffset));
            float scale=1+Mathf.Max(0,depth);
            return Matrix4x4.Translate(new Vector3(actorPosition.x+offset.x,actorPosition.y+offset.y,0))
                *Matrix4x4.Scale(new Vector3(scale,scale,1))*Matrix4x4.Translate(new Vector3(-actorPosition.x,-actorPosition.y,0));
        }
        public static float Penumbra(Vector2 actorPosition,Vector2 sourcePosition,float radius,float depth)
            =>Mathf.Clamp(.25f*Mathf.Max(0,radius)*Mathf.Max(0,depth)/Mathf.Max(.1f,Vector2.Distance(actorPosition,sourcePosition)),0,.06f);

        public static Matrix4x4 TransientProjection(Vector2 actorPosition,Vector2 sourcePosition,float requestedScale,float maxScale,float maxOffset)
        {
            if(!float.IsFinite(requestedScale) || requestedScale<1 || !float.IsFinite(maxScale) || maxScale<1 || !float.IsFinite(maxOffset) || maxOffset<=0)
                throw new ArgumentOutOfRangeException(nameof(requestedScale),"Неверные параметры центральной проекции тени вспышки.");
            float scale=Mathf.Min(requestedScale,maxScale);
            float distance=Vector2.Distance(actorPosition,sourcePosition);
            if(distance>.0001f) scale=Mathf.Min(scale,1+maxOffset/distance);
            return Matrix4x4.Translate(sourcePosition)*Matrix4x4.Scale(new Vector3(scale,scale,1))*Matrix4x4.Translate(-sourcePosition);
        }
        public static float WallPenumbra(float radius,float scale)
            =>Mathf.Clamp(Mathf.Max(0,radius)*Mathf.Max(0,scale-1),0,.06f);
        public void AppendProjectedLightShadows(List<ProjectedLightShadow> output)
        {
            if(output==null) throw new ArgumentNullException(nameof(output));
            for(int i=0;i<ActiveSourceCount;i++)
            {
                var slot=slots[i];
                var bounds=slot.lastBounds;
                output.Add(new ProjectedLightShadow(slot.sourceId,slot.mask,new Rect(bounds.min.x,bounds.min.y,bounds.size.x,bounds.size.y),slot.coverage,MaskRebuilds));
            }
        }

        public bool NeedsPreparation(IReadOnlyList<RoomRadianceField.LightOccluder> parts,int sourceCount)
        {
            if(parts==null) throw new ArgumentNullException(nameof(parts));
            if(sourceCount<0) throw new ArgumentOutOfRangeException(nameof(sourceCount));
            if(maskMaterial==null || poseMask==null || properties==null || slots.Count<sourceCount) return true;
            for(int i=0;i<parts.Count;i++)
                if(parts[i].Sprite!=null && !spriteMeshes.ContainsKey(parts[i].Sprite)) return true;
            return false;
        }
        // Explicit cached rebuild, queued by the coordinator outside the pose/drawing callback.
        public void Prepare(IReadOnlyList<RoomRadianceField.LightOccluder> parts,int sourceCount)
        {
            Validate();
            if(parts==null) throw new ArgumentNullException(nameof(parts));
            if(sourceCount<0) throw new ArgumentOutOfRangeException(nameof(sourceCount));
            properties??=new MaterialPropertyBlock();
            if(maskMaterial==null) maskMaterial=new Material(shadowMaterial){name="Рабочая маска проекции тени"};
            if(poseMask==null) poseMask=CreateMask("Исходный объединённый силуэт тела и оружия");
            for(int i=0;i<parts.Count;i++)
            {
                var sprite=parts[i].Sprite;
                if(sprite==null || spriteMeshes.ContainsKey(sprite)) continue;
                var source=sprite.vertices;var vertices=new Vector3[source.Length];
                for(int vertex=0;vertex<vertices.Length;vertex++) vertices[vertex]=source[vertex];
                var triangles=sprite.triangles;var indices=new int[triangles.Length];
                for(int triangle=0;triangle<indices.Length;triangle++) indices[triangle]=triangles[triangle];
                var mesh=new Mesh{name="Маска спрайта тени "+sprite.name};
                mesh.vertices=vertices;mesh.uv=sprite.uv;mesh.triangles=indices;
                var colors=new Color[vertices.Length];for(int vertex=0;vertex<colors.Length;vertex++) colors[vertex]=Color.white;mesh.colors=colors;
                mesh.RecalculateBounds();spriteMeshes.Add(sprite,mesh);
            }
            while(slots.Count<sourceCount) GetSlot(slots.Count);
            hasSignature=false;
        }

        public void Draw(Vector2 actorPosition,IReadOnlyList<RoomRadianceField.LightOccluder> parts,IReadOnlyList<ActorShadowSource> sources,RoomLightingTable settings)
        {
            Validate();
            if(parts==null || sources==null || settings==null) throw new ArgumentNullException("Нет силуэтов, источников или настроек проекции тени.");
            if(parts.Count==0 || sources.Count==0) {Clear();return;}
            // A queued first/new-content preparation deliberately leaves no projection for this pose.
            // Never allocate render targets or generate sprite topology in the per-pose callback.
            if(NeedsPreparation(parts,sources.Count)) {Clear();return;}
            int signature=Signature(actorPosition,parts,sources,settings);
            if(hasSignature && signature==lastSignature) return;
            int poseSignature=PoseSignature(parts);
            if(!hasPoseMask || lastPoseSignature!=poseSignature)
            {
                bool validBounds=false;poseBounds=default;
                for(int partIndex=0;partIndex<parts.Count;partIndex++)
                {
                    var part=parts[partIndex];
                    if(TryMesh(part,out var mesh,out _)) ExpandBounds(ref poseBounds,ref validBounds,mesh.bounds,part.LocalToWorld);
                }
                if(!validBounds || poseBounds.size.x<=.0001f || poseBounds.size.y<=.0001f) {Clear();return;}
                poseBounds.Expand(new Vector3(.01f,.01f,0));
                DrawMask(poseMask,parts,poseBounds);
                lastPoseSignature=poseSignature;hasPoseMask=true;PoseMaskRebuilds++;
            }
            // The selector limits steady lamps. Transient flashes appended by the caller must remain visible.
            int count=sources.Count;
            ActiveSourceCount=0;
            for(int i=0;i<count;i++)
            {
                var source=sources[i];
                // The maximum is an explicit artistic coverage control, not material transparency.
                // Source power already scales the light contribution, so never apply it twice.
                float opacity=Mathf.Clamp01((float)settings.ActorShadowMaxOpacity)*Mathf.Clamp01(source.Coverage);
                if(source.Strength<=0 || opacity<=0 || parts.Count==0) continue;
                var projection=source.IsTransient
                    ?TransientProjection(actorPosition,source.Position,source.ProjectionScale,(float)settings.ActorShadowMaxScale,(float)settings.ActorShadowMaxOffset)
                    :Projection(actorPosition,source.Position,(float)settings.ActorShadowDepth,(float)settings.ActorShadowMaxOffset);
                float blur=WallPenumbra(source.Radius,projection.m00);
                bool hasBounds=false;Bounds bounds=default;
                ExpandBounds(ref bounds,ref hasBounds,poseBounds,projection);
                bounds.Expand(new Vector3(Mathf.Max(.02f,blur*4),Mathf.Max(.02f,blur*4),0));
                if(bounds.size.x<=.0001f || bounds.size.y<=.0001f) continue;
                var slot=slots[ActiveSourceCount++];
                slot.transient=source.IsTransient;slot.sourceId=source.Id;slot.coverage=opacity;
                DrawProjection(slot,actorPosition,projection,bounds,blur);
                SetQuad(slot,bounds);
                properties.Clear();properties.SetTexture("_MainTex",slot.mask);
                properties.SetFloat("_Opacity",opacity);
                properties.SetVector("_BlurUv",new Vector4(blur/bounds.size.x,blur/bounds.size.y,0,0));
                slot.renderer.SetPropertyBlock(properties);slot.renderer.enabled=false;
            }
            for(int i=ActiveSourceCount;i<slots.Count;i++) slots[i].renderer.enabled=false;
            lastSignature=signature;hasSignature=true;MaskRebuilds++;
        }
        private int Signature(Vector2 actorPosition,IReadOnlyList<RoomRadianceField.LightOccluder> parts,IReadOnlyList<ActorShadowSource> sources,RoomLightingTable settings)
        {
            var hash=new HashCode();hash.Add(actorPosition);hash.Add(transform.localToWorldMatrix);
            hash.Add(settings.ActorShadowDepth);hash.Add(settings.ActorShadowMaxOffset);hash.Add(settings.ActorShadowMaxOpacity);
            hash.Add(settings.ActorShadowMoveThreshold);hash.Add(settings.ActorShadowMaxSources);hash.Add(parts.Count);hash.Add(sources.Count);
            hash.Add(settings.ActorShadowMaxScale);
            for(int i=0;i<parts.Count;i++)
            {
                var part=parts[i];hash.Add(part.Sprite!=null?part.Sprite.GetInstanceID():0);hash.Add(part.Mesh!=null?part.Mesh.GetInstanceID():0);
                hash.Add(part.Texture!=null?part.Texture.GetInstanceID():0);hash.Add(part.LocalToWorld);hash.Add(part.Opacity);
                if(part.Sprite!=null) {hash.Add(part.Sprite.texture.GetInstanceID());hash.Add(part.Sprite.bounds);}
                if(part.Mesh!=null) hash.Add(part.Mesh.bounds);
            }
            for(int i=0;i<sources.Count;i++) {var source=sources[i];hash.Add(source.Id);hash.Add(source.Position);hash.Add(source.Radius);hash.Add(source.Strength);hash.Add(source.Coverage);hash.Add(source.IsTransient);hash.Add(source.ProjectionScale);}
            return hash.ToHashCode();
        }
        private static int PoseSignature(IReadOnlyList<RoomRadianceField.LightOccluder> parts)
        {
            var hash=new HashCode();hash.Add(parts.Count);
            for(int i=0;i<parts.Count;i++)
            {
                var part=parts[i];hash.Add(part.Sprite!=null?part.Sprite.GetInstanceID():0);hash.Add(part.Mesh!=null?part.Mesh.GetInstanceID():0);
                hash.Add(part.Texture!=null?part.Texture.GetInstanceID():0);hash.Add(part.LocalToWorld);hash.Add(part.Opacity);
                if(part.Sprite!=null) {hash.Add(part.Sprite.texture.GetInstanceID());hash.Add(part.Sprite.bounds);}
                if(part.Mesh!=null) hash.Add(part.Mesh.bounds);
            }
            return hash.ToHashCode();
        }
        private bool TryMesh(RoomRadianceField.LightOccluder part,out Mesh mesh,out Texture texture)
        {
            mesh=part.Mesh;texture=part.Texture;
            if(part.Opacity<=0) return false;
            if(part.Sprite!=null)
            {
                var sprite=part.Sprite;texture=sprite.texture;
                if(!spriteMeshes.TryGetValue(sprite,out mesh)) return false;
            }
            if(mesh!=null && texture!=null) return true;
            int key=part.Sprite!=null?part.Sprite.GetInstanceID():mesh!=null?mesh.GetInstanceID():0;
            if(warned.Add(key)) Debug.LogWarning($"Проекция тени {name}: пропущена часть {key}; отсутствует mesh или texture, исходная графика не подменяется.",this);
            return false;
        }
        private void DrawMask(RenderTexture target,IReadOnlyList<RoomRadianceField.LightOccluder> parts,Bounds bounds)
        {
            var previous=RenderTexture.active;
            RenderTexture.active=target;GL.PushMatrix();
            try
            {
                GL.Clear(false,true,Color.clear);
                GL.LoadProjectionMatrix(Matrix4x4.Ortho(bounds.min.x,bounds.max.x,bounds.min.y,bounds.max.y,-100,100));
                GL.modelview=Matrix4x4.identity;
                for(int partIndex=0;partIndex<parts.Count;partIndex++)
                {
                    var part=parts[partIndex];
                    if(!TryMesh(part,out var mesh,out var texture)) continue;
                    maskMaterial.SetTexture("_MainTex",texture);maskMaterial.SetFloat("_Opacity",Mathf.Clamp01(part.Opacity));
                    maskMaterial.SetPass(0);Graphics.DrawMeshNow(mesh,part.LocalToWorld);
                }
            }
            finally {GL.PopMatrix();RenderTexture.active=previous;}
        }
        private void DrawProjection(Slot slot,Vector2 actorPosition,Matrix4x4 projection,Bounds bounds,float blur)
        {
            var end=(Vector2)projection.MultiplyPoint3x4(actorPosition);
            var offset=end-actorPosition;
            maskMaterial.SetVector("_SourceRect",new Vector4(poseBounds.min.x,poseBounds.min.y,poseBounds.size.x,poseBounds.size.y));
            maskMaterial.SetVector("_OutputRect",new Vector4(bounds.min.x,bounds.min.y,bounds.size.x,bounds.size.y));
            maskMaterial.SetVector("_SweepActor",new Vector4(actorPosition.x,actorPosition.y,0,0));
            maskMaterial.SetVector("_SweepProjection",new Vector4(offset.x,offset.y,projection.m00-1,0));
            maskMaterial.SetVector("_WallPenumbra",new Vector4(blur,blur,0,0));
            Graphics.Blit(poseMask,slot.mask,maskMaterial,3);
        }
        private static RenderTexture CreateMask(string maskName)
        {
            var target=new RenderTexture(MaskResolution,MaskResolution,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
            {name=maskName,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,useMipMap=false};
            target.Create();return target;
        }
        private Slot GetSlot(int index)
        {
            if(index<slots.Count) return slots[index];
            var slot=new Slot{root=new GameObject("Проекция тени "+index),mesh=new Mesh{name="Квадрат проекции тени"}};
            slot.root.transform.SetParent(transform,false);
            slot.filter=slot.root.AddComponent<MeshFilter>();slot.renderer=slot.root.AddComponent<MeshRenderer>();
            slot.filter.sharedMesh=slot.mesh;slot.renderer.sharedMaterial=shadowMaterial;
            slot.renderer.sortingOrder=ShadowOrder;slot.renderer.shadowCastingMode=ShadowCastingMode.Off;slot.renderer.receiveShadows=false;
            slot.renderer.lightProbeUsage=LightProbeUsage.Off;slot.renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            slot.mesh.MarkDynamic();slot.mesh.vertices=slot.vertices;
            slot.mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            slot.mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};slot.mesh.triangles=new[]{0,1,2,0,2,3};
            slot.mask=CreateMask("Непрерывная проекция силуэта");
            slots.Add(slot);return slot;
        }
        private void SetQuad(Slot slot,Bounds bounds)
        {
            var inverse=slot.root.transform.worldToLocalMatrix;
            if(slot.hasQuad && slot.lastBounds==bounds && slot.lastInverse==inverse) return;
            slot.hasQuad=true;slot.lastBounds=bounds;slot.lastInverse=inverse;
            slot.vertices[0]=inverse.MultiplyPoint3x4(new Vector3(bounds.min.x,bounds.min.y,0));
            slot.vertices[1]=inverse.MultiplyPoint3x4(new Vector3(bounds.max.x,bounds.min.y,0));
            slot.vertices[2]=inverse.MultiplyPoint3x4(new Vector3(bounds.max.x,bounds.max.y,0));
            slot.vertices[3]=inverse.MultiplyPoint3x4(new Vector3(bounds.min.x,bounds.max.y,0));
            slot.mesh.vertices=slot.vertices;
            var local=new Bounds(slot.vertices[0],Vector3.zero);
            for(int i=1;i<slot.vertices.Length;i++) local.Encapsulate(slot.vertices[i]);
            slot.mesh.bounds=local;
        }
        private static void ExpandBounds(ref Bounds bounds,ref bool hasBounds,Bounds local,Matrix4x4 matrix)
        {
            for(int corner=0;corner<8;corner++)
            {
                var p=local.center+Vector3.Scale(local.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                p=matrix.MultiplyPoint3x4(p);p.z=0;
                if(!hasBounds) {bounds=new Bounds(p,Vector3.zero);hasBounds=true;} else bounds.Encapsulate(p);
            }
        }
        public void Clear() {hasSignature=false;ActiveSourceCount=0;foreach(var slot in slots) slot.renderer.enabled=false;}
        private void OnDestroy()
        {
            foreach(var slot in slots) {if(slot.mask!=null) {slot.mask.Release();ReleaseObject(slot.mask);}if(slot.mesh!=null) ReleaseObject(slot.mesh);}
            foreach(var mesh in spriteMeshes.Values) if(mesh!=null) ReleaseObject(mesh);
            spriteMeshes.Clear();slots.Clear();
            if(maskMaterial!=null) ReleaseObject(maskMaterial);
            if(poseMask!=null) {poseMask.Release();ReleaseObject(poseMask);}
        }
        private static void ReleaseObject(UnityEngine.Object value)
        {if(Application.isPlaying) Destroy(value);else DestroyImmediate(value);}
    }
}
