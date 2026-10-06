using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // The rectangle describes the selected interaction volume. Its sorting owner
    // is the actual object's outer group, so a foreground actor occludes both.
    public sealed class WorldInteractionOutline : IDisposable
    {
        public const float PhysicalPixels=3;
        public const int LocalOrder=short.MaxValue;
        private readonly Camera camera;
        private readonly Func<InteractionTarget,WorldOutlineSource> resolve;
        private readonly Material material;
        private readonly Sprite sprite;
        private readonly SpriteRenderer[] bars=new SpriteRenderer[4];
        private Transform compensation,plane;
        private InteractionTarget target;
        private WorldOutlineSource source;
        private bool requested,disposed,hasPlaced;
        private Rect placedBounds;
        private float placedDepth,placedWidth;
        private Matrix4x4 ownerMatrix;
        private bool hasOwnerMatrix;
        private string warning;
        public IReadOnlyList<SpriteRenderer> Renderers=>bars;
        public bool Visible
        {
            get {foreach(var bar in bars) if(bar==null || !bar.enabled || bar.forceRenderingOff || !bar.gameObject.activeInHierarchy) return false;return true;}
        }
        public int GeometryRevisionCount {get;private set;}
        public int BindingRevisionCount {get;private set;}
        public WorldInteractionOutline(Camera camera,Func<InteractionTarget,WorldOutlineSource> resolve)
        {
            this.camera=camera;this.resolve=resolve??throw new ArgumentNullException(nameof(resolve));
            material=WorldArtworkMaterial.Create(Texture2D.whiteTexture);material.name="Материал мирового контура взаимодействия";
            material.SetFloat("_Emission",1);
            var texture=Texture2D.whiteTexture;
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f,texture.width,0,SpriteMeshType.FullRect);
            sprite.name="Белый квадрат мирового контура";
            CreateRenderers();Camera.onPreCull+=BeforeBuiltinCamera;RenderPipelineManager.beginCameraRendering+=BeforePipelineCamera;
        }
        private void CreateRenderers()
        {
            compensation=new GameObject("Контур выбранного объекта").transform;
            plane=new GameObject("Мировая плоскость контура").transform;plane.SetParent(compensation,false);
            for(int i=0;i<bars.Length;i++)
            {
                var bar=new GameObject("Сторона контура: "+i).AddComponent<SpriteRenderer>();bar.transform.SetParent(plane,false);
                bar.enabled=false;bar.forceRenderingOff=true;bar.sprite=sprite;bar.sharedMaterial=material;bar.color=new Color(1,.85f,.1f,.85f);
                bar.sortingOrder=LocalOrder;bar.shadowCastingMode=ShadowCastingMode.Off;bar.receiveShadows=false;bars[i]=bar;
            }
            hasPlaced=false;hasOwnerMatrix=false;
        }
        // Called by an explicit target refresh. Source lifetime changes rebuild only
        // this small cached renderer; no objects/assets are created in camera hooks.
        public void SetTarget(InteractionTarget value,bool visible)
        {
            if(disposed) return;
            if(target.kind!=value.kind || target.id!=value.id) warning=null;
            target=value;requested=visible && value.kind!=InteractionKind.None;
            if(!requested) {Hide();warning=null;return;}
            if(camera==null) {Unavailable("камера не задана");return;}
            source=resolve(target);
            if(source.Visible && compensation==null) CreateRenderers();
            if(!source.Visible || !Valid(target.bounds)) {Hide();return;}
            BindSource();
        }
        private void BindSource()
        {
            // Group membership and enabled state must exist before the engine's
            // sorting-group pass. Camera callbacks are too late to register an
            // initially ungrouped renderer with its high *local* order.
            bool changed=false;var parent=source.Group.transform;
            if(compensation.parent!=parent) {compensation.SetParent(parent,false);hasOwnerMatrix=false;changed=true;}
            foreach(var bar in bars)
            {
                if(bar.gameObject.layer!=source.GameLayer) {bar.gameObject.layer=source.GameLayer;changed=true;}
                if(bar.sortingLayerID!=source.Group.sortingLayerID) {bar.sortingLayerID=source.Group.sortingLayerID;changed=true;}
                if(!bar.enabled) {bar.enabled=true;changed=true;}
                if(bar.forceRenderingOff) bar.forceRenderingOff=false;
            }
            if(changed) BindingRevisionCount++;
        }
        // Suppression does not unregister the already-enabled renderer from its
        // sorting group. In particular, late source hide/recovery must not turn a
        // high local order into a newly enabled ungrouped global renderer.
        public void Hide() {foreach(var bar in bars) if(bar!=null && !bar.forceRenderingOff) bar.forceRenderingOff=true;}
        private void BeforeBuiltinCamera(Camera value)=>Prepare(value);
        private void BeforePipelineCamera(ScriptableRenderContext context,Camera value)=>Prepare(value);
        public void Prepare(Camera value)
        {
            if(disposed || !requested || value!=camera) return;
            if(!TryWorldThickness(camera,out float width,out string reason)) {Unavailable(reason);return;}
            if(!Valid(target.bounds)) {Unavailable("невалидные границы цели");return;}
            if(source.Group==null) {Unavailable("нет фактической внешней SortingGroup выбранного изображения");return;}
            if(!source.Visible) {Unavailable("изображение или его SortingGroup выключены");return;}
            if(compensation==null) {Unavailable("изображение цели удалено до обновления выбранного представления");return;}
            float depth=source.Depth;
            if(!float.IsFinite(depth)) {Unavailable("невалидная глубина фактического изображения");return;}
            if((camera.cullingMask & (1<<source.GameLayer))==0 ||
                camera.WorldToViewportPoint(new Vector3(target.bounds.center.x,target.bounds.center.y,depth)).z<=0) {Hide();return;}
            var parent=source.Group.transform;
            if(compensation.parent!=parent)
            {Unavailable("actual owner сменился после target refresh; требуется ранняя привязка до camera pass");return;}
            foreach(var bar in bars)
                if(bar==null || !bar.enabled || bar.gameObject.layer!=source.GameLayer || bar.sortingLayerID!=source.Group.sortingLayerID)
                {Unavailable("стороны контура не зарегистрированы до camera pass; требуется ранний target refresh");return;}
            var matrix=parent.localToWorldMatrix;
            if(!hasOwnerMatrix || ownerMatrix!=matrix)
            {
                if(!Neutralize(parent,matrix,out reason)) {Unavailable(reason);return;}
                ownerMatrix=matrix;hasOwnerMatrix=true;
            }
            if(!hasPlaced || placedBounds!=target.bounds || placedDepth!=depth || placedWidth!=width)
            {
                var rect=target.bounds;
                PlaceBar(0,new Vector3(rect.center.x,rect.yMin,depth),new Vector2(rect.width+width,width));
                PlaceBar(1,new Vector3(rect.center.x,rect.yMax,depth),new Vector2(rect.width+width,width));
                PlaceBar(2,new Vector3(rect.xMin,rect.center.y,depth),new Vector2(width,rect.height+width));
                PlaceBar(3,new Vector3(rect.xMax,rect.center.y,depth),new Vector2(width,rect.height+width));
                placedBounds=rect;placedDepth=depth;placedWidth=width;hasPlaced=true;GeometryRevisionCount++;
            }
            warning=null;foreach(var bar in bars) if(bar.forceRenderingOff) bar.forceRenderingOff=false;
        }
        private void PlaceBar(int index,Vector3 center,Vector2 size)
        {var part=bars[index].transform;part.localPosition=center;part.localRotation=Quaternion.identity;part.localScale=new Vector3(size.x,size.y,1);}
        private bool Neutralize(Transform parent,Matrix4x4 matrix,out string reason)
        {
            reason=null;
            var x=new Vector3(matrix.m00,matrix.m10,matrix.m20);var y=new Vector3(matrix.m01,matrix.m11,matrix.m21);
            float sx=x.magnitude,sy=y.magnitude;
            if(!float.IsFinite(sx) || !float.IsFinite(sy) || sx<=.000001f || sy<=.000001f || !float.IsFinite(matrix.m22) || Mathf.Abs(matrix.m22)<=.000001f)
            {reason="внешняя группа имеет нулевой или невалидный масштаб";return false;}
            if(Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(matrix.m02)+Mathf.Abs(matrix.m12)>.00001f ||
                Mathf.Abs(Vector3.Dot(x,y))>sx*sy*.00001f)
            {reason="внешняя группа наклонена к XY или содержит shear; мировая рамка недоступна";return false;}
            // Two prepared transforms cancel planar R*S in the correct reverse
            // order. A single inverse TRS would shear bars under nonuniform scale.
            float signedY=(matrix.m00*matrix.m11-matrix.m01*matrix.m10)/sx;
            compensation.localPosition=parent.InverseTransformPoint(Vector3.zero);
            compensation.localRotation=Quaternion.identity;compensation.localScale=new Vector3(1/sx,1/signedY,1/matrix.m22);
            plane.localPosition=Vector3.zero;plane.localScale=Vector3.one;
            plane.localRotation=Quaternion.AngleAxis(-Mathf.Atan2(x.y,x.x)*Mathf.Rad2Deg,Vector3.forward);
            return true;
        }
        public static bool TryWorldThickness(Camera camera,out float width,out string reason)
        {
            width=0;reason=null;
            if(camera==null) {reason="камера не задана";return false;}
            if(!camera.orthographic) {reason="мировому контуру нужна ортографическая камера";return false;}
            var direction=camera.transform.forward;
            if(Mathf.Abs(direction.x)>.00001f || Mathf.Abs(direction.y)>.00001f)
            {reason="камера не перпендикулярна мировой плоскости XY";return false;}
            if(camera.pixelHeight<=0 || !float.IsFinite(camera.orthographicSize) || camera.orthographicSize<=0)
            {reason="невалидные размер камеры или высота viewport";return false;}
            width=PhysicalPixels*2*camera.orthographicSize/camera.pixelHeight;
            if(!float.IsFinite(width) || width<=0) {reason="невалидный мировой размер физического пикселя";return false;}
            return true;
        }
        private static bool Valid(Rect bounds)=>float.IsFinite(bounds.xMin) && float.IsFinite(bounds.xMax) &&
            float.IsFinite(bounds.yMin) && float.IsFinite(bounds.yMax) && bounds.width>0 && bounds.height>0;
        private void Unavailable(string reason)
        {
            Hide();if(warning==reason) return;warning=reason;
            Debug.LogWarning($"Контур взаимодействия скрыт: target={target.kind}/{target.id}, camera={(camera!=null?camera.name:"нет")}, bounds={target.bounds}: {reason}.");
        }
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            Hide();requested=false;
            Camera.onPreCull-=BeforeBuiltinCamera;RenderPipelineManager.beginCameraRendering-=BeforePipelineCamera;
            if(compensation!=null) Remove(compensation.gameObject);Remove(sprite);Remove(material);
        }
        private static void Remove(UnityEngine.Object value)
        {if(Application.isPlaying) UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }
}
