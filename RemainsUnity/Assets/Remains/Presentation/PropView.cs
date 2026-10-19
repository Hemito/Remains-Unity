using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    public enum PropVisualAnchor { BodyCenter, PlacementAnchor, Feet }
    public sealed class PropView : MonoBehaviour
    {
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer artwork;
        [SerializeField,InspectorName("Привязка графики к состоянию")] private PropVisualAnchor visualAnchor;
        [SerializeField,InspectorName("Масштабировать по ширине тела")] private bool fitStateWidth=true;
        [SerializeField,InspectorName("Явный профиль контейнера")] private Remains.Content.ContainerArtworkProfileAsset containerArtwork;
        [Header("Свечение при удержании")]
        [SerializeField,ColorUsage(false),InspectorName("Цвет автономной редакторской диагностики")] private Color heldColor=new(.1f,1,.15f,1);
        [SerializeField,Range(.005f,.25f),InspectorName("Радиус в координатах спрайта")] private float heldRadius=.08f;
        [SerializeField,Range(0,1),InspectorName("Интенсивность")] private float heldIntensity=.7f;
        public SpriteRenderer Artwork=>artwork;
        private readonly RemotePropMotion motion=new();
        private HeldPropGlow glow;
        private SortingGroup sorting;
        private WorldVisualOrderBinding depth;
        private bool? standaloneHeld;
        private HeldPropColor heldAppearance;
        private bool hasHeldAppearance,previewWarning;
        private (string world,string id,string owner,string level,int generation)? missingColor;
        public SortingGroup Sorting=>sorting;
        public WorldVisualOrderBinding Depth=>depth;
        public int Order=>sorting!=null?sorting.sortingOrder:artwork.sortingOrder;
        public void BindOrder(WorldVisualOrderTable orders,WorldVisualKind kind,string id)
        {
            PrepareHeldGlow();
            if(depth!=null) throw new InvalidOperationException("Повторная привязка слоя предмета: "+id);
            depth=gameObject.AddComponent<WorldVisualOrderBinding>();depth.Initialize(orders,kind,id,sorting);
        }
        public bool HeldGlowVisible=>glow!=null && glow.Visible;
        public SpriteRenderer HeldGlowRenderer=>glow?.Renderer;
        public string HeldGlowHolder=>hasHeldAppearance?heldAppearance.HolderId:null;
        public int? HeldGlowMagic=>hasHeldAppearance?heldAppearance.Magic:null;
        public int HeldGlowPreparedFrameCount=>glow?.PreparedFrameCount??0;
        public bool FitsStateWidth=>fitStateWidth;
        public PropVisualAnchor VisualAnchor=>visualAnchor;
        public Remains.Content.ContainerArtworkProfileAsset ContainerArtwork=>containerArtwork;
        public void ConfigureContainerArtwork(Remains.Content.ContainerArtworkProfileAsset profile)=>containerArtwork=profile;
        public void ApplyHeldAppearance(Remains.Domain.PropState state,HeldPropColorSource source)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            PrepareHeldGlow();
            hasHeldAppearance=source.TryResolve(state,out heldAppearance,out string reason);
            if(hasHeldAppearance)
            {
                var color=heldAppearance.Color;color.a=heldIntensity;glow.SetColor(color);missingColor=null;
            }
            else if(state.held) WarnMissingColor(source.WorldId,state,reason);
            else missingColor=null;
            glow.Draw(state.held && hasHeldAppearance);
        }
        private void WarnMissingColor(string world,Remains.Domain.PropState state,string reason)
        {
            var context=(world,state.id,state.owner,state.level,state.generation);
            if(missingColor.HasValue && missingColor.Value==context) return;
            missingColor=context;
            Debug.LogWarning($"Свечение предмета скрыто: world={world}, prop={state.id}, owner={state.owner}, level={state.level}, generation={state.generation}; {reason}",this);
        }
        public void ApplyContainerAppearance(Remains.Domain.ContainerState state)
        {
            if(state.presentationItem!=null) return;
            if(containerArtwork==null) throw new InvalidOperationException($"Нет явного профиля контейнера: id={state.id}, type={state.definition}, view={name}.");
            if(state.appearance==null) throw new InvalidOperationException("Нет состояния вида контейнера: "+state.id);
            state.appearance.Validate(state.id);containerArtwork.Validate(state.definition);
            var sprite=containerArtwork.Select(state.appearance.opened);
            if(artwork.sprite==sprite) return;
            SpriteArtworkMaterial.Apply(artwork,sprite);glow?.SetFrame(sprite);
            // Registration/scale is authored per frame. Never fit the alternate to the closed body.
        }
        public void PrepareHeldGlow()
        {
            if(glow!=null) return;
            var group=GetComponent<SortingGroup>();
            if(group==null)
            {
                group=gameObject.AddComponent<SortingGroup>();
                group.sortingLayerID=artwork.sortingLayerID;group.sortingOrder=artwork.sortingOrder;
            }
            sorting=group;sorting.sortingOrder=ActorVisualLayers.RestingObjectOrder;
            artwork.sortingOrder=0;
            var child=new GameObject("Свечение удерживаемого предмета");
            child.transform.SetParent(artwork.transform,false);
            glow=child.AddComponent<HeldPropGlow>();
            var color=heldColor;color.a=heldIntensity;
            var frames=containerArtwork==null?null:containerArtwork.Mode==Remains.Content.ContainerArtworkMode.ClosedOpened?
                new[]{containerArtwork.Closed,containerArtwork.Opened}:new[]{containerArtwork.Closed};
            glow.Initialize(artwork,color,heldRadius,frames);
        }
        private void OnValidate()
        {
            if(!float.IsFinite(heldRadius) || !float.IsFinite(heldIntensity))
                Debug.LogWarning($"Некорректные настройки свечения {name}: radius={heldRadius}, intensity={heldIntensity}; восстановлены допустимые значения.",this);
            heldRadius=float.IsFinite(heldRadius)?Mathf.Clamp(heldRadius,.005f,.25f):.08f;
            heldIntensity=float.IsFinite(heldIntensity)?Mathf.Clamp01(heldIntensity):.7f;
        }
        private bool hasDrawn,drawnMirrored;private Vector2 drawnPosition;private float drawnAngle;
        private Remains.Content.DroppedItemShape droppedShape;
        private bool shapeReady,shapeMirrored;private float shapeAngle;private Vector2 shapeCenter;
        public void DrawMotion(Remains.Domain.PropState state,string localOwner,bool paused,float dt)
        {
            var position=motion.Sample(state,localOwner,paused,dt);
            if(droppedShape!=null && state is Remains.Domain.ContainerState item)
            {
                if(!shapeReady || shapeAngle!=item.presentationAngle || shapeMirrored!=item.presentationMirrored)
                {
                    shapeCenter=droppedShape.WorldBounds(item.presentationAngle,item.presentationMirrored).center;
                    shapeAngle=item.presentationAngle;shapeMirrored=item.presentationMirrored;shapeReady=true;
                }
                position-=shapeCenter;
            }
            if(visualAnchor==PropVisualAnchor.PlacementAnchor) position-=Vector2.up*(state.height*.5f-state.anchorHeight);
            else if(visualAnchor==PropVisualAnchor.Feet) position-=Vector2.up*state.anchorHeight;
            // Written only on change: ~1400 resting factory bodies otherwise dirtied their transforms every frame.
            if(!hasDrawn || position!=drawnPosition) {transform.position=position;drawnPosition=position;}
            if(state is Remains.Domain.ContainerState {presentationItem:not null} loose &&
                (!hasDrawn || loose.presentationAngle!=drawnAngle || loose.presentationMirrored!=drawnMirrored))
            {
                transform.rotation=Quaternion.Euler(0,0,loose.presentationAngle);artwork.flipY=loose.presentationMirrored;
                drawnAngle=loose.presentationAngle;drawnMirrored=loose.presentationMirrored;
            }
            hasDrawn=true;
            // Standalone editor previews have no world/table. Runtime instances bind
            // once at creation; their category changes only through table events.
            if(depth==null && sorting!=null && standaloneHeld!=state.held)
            {
                if(Application.isPlaying) throw new InvalidOperationException("Предмет без общей таблицы слоёв: "+state.id);
                standaloneHeld=state.held;sorting.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualOrderPolicy.Category(state));
            }
            bool show=state.held;
            if(show && (!hasHeldAppearance || !heldAppearance.Matches(depth?.Orders.WorldId,state)))
            {
                if(depth!=null || Application.isPlaying)
                {show=false;WarnMissingColor(depth?.Orders.WorldId,state,"Нет цвета подтверждённого текущего держателя для этой версии предмета.");}
                else
                {
                    if(!previewWarning)
                    {previewWarning=true;Debug.LogWarning($"Автономная редакторская диагностика свечения: prop={state.id}, view={name}; мира/держателя нет, использован явно заданный диагностический цвет. Это не игровой вид удержания.",this);}
                    var color=heldColor;color.a=heldIntensity;glow?.SetColor(color);
                }
            }
            if(!state.held) missingColor=null;
            glow?.Draw(show);
        }
        public void SetArtwork(SpriteRenderer value)=>artwork=value;
        public void ConfigurePlacementArtwork() {visualAnchor=PropVisualAnchor.PlacementAnchor;fitStateWidth=false;}
        // Artwork at world scale whose pivot is the object's feet (exported original units: mines).
        public void ConfigureFeetArtwork() {visualAnchor=PropVisualAnchor.Feet;fitStateWidth=false;}
        public void ConfigureDroppedArtwork(Remains.Content.DroppedItemShape shape)
        {
            if(shape==null) throw new ArgumentNullException(nameof(shape));shape.Validate();
            droppedShape=shape;visualAnchor=PropVisualAnchor.BodyCenter;fitStateWidth=false;
            transform.localScale=new Vector3(shape.Scale,shape.Scale,1);shapeReady=false;
        }
        public void FitWidth(float width)
        {
            if(artwork==null || artwork.sprite==null) throw new InvalidOperationException($"Объект {name}: нет основного спрайта.");
            if(!float.IsFinite(width) || width<=0 || artwork.bounds.size.x<=0) throw new InvalidOperationException($"Объект {name}: некорректная ширина {width}.");
            if(!fitStateWidth) return;
            transform.localScale*=width/artwork.bounds.size.x;
        }
        public void SetVisible(bool visible)=>gameObject.SetActive(visible);
    }
}
