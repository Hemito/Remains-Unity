using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using Remains.Content;

namespace Remains.Presentation
{
    // A compiled cache is shared by every pony. No asset lookup or mesh rebuild in Update.
    [RequireComponent(typeof(SortingGroup))]
    public sealed class PonyVisual : ActorVisual
    {

        [SerializeField,HideInInspector] private List<MeshFilter> filters = new();
        [SerializeField,HideInInspector] private List<MeshRenderer> renderers = new();
        [SerializeField,HideInInspector] private bool authoredStructure;
        public void UseAuthoredStructure()=>authoredStructure=true;
        private ActorState state;
        private string pose;
        private int frame = -1;
        private MaterialPropertyBlock properties;
        private PonyAppearanceKey? appearance;
        private WearableDrawPlan drawPlan;
        private bool drawPlanDirty;
        public WearableDrawPlan CurrentDrawPlan=>drawPlan;
        private Bounds drawnBounds;
        private bool hasDrawnBounds;
        private IPonyMagicActivitySource magicSource;
        // Detached asset previews intentionally show the complete authored glow.
        // A live world always installs its confirmed source before Bind/Draw.
        private float previewMagicOpacity=1;
        private bool magicDirty=true;
        private int magicRoleVersion=-1;
        private PonyRenderPart[] drawnParts;
        public bool UsesMagicPreview=>magicSource==null;
        public PonyMagicActivity DrawnMagicActivity {get;private set;}
        public void ConfigureMagicSource(IPonyMagicActivitySource source)
        {magicSource=source??throw new ArgumentNullException(nameof(source));magicDirty=true;}
        public void ConfigureMagicPreview(float opacity)
        {
            if(!float.IsFinite(opacity) || opacity<0 || opacity>1) throw new ArgumentOutOfRangeException(nameof(opacity));
            magicSource=null;previewMagicOpacity=opacity;magicDirty=true;
        }
        public bool TryGetMagicRenderer(out MeshRenderer renderer)
        {
            if(drawnParts!=null) for(int i=0;i<drawnParts.Length;i++)
                if(PonyMagicRenderRoles.Contains(drawnParts[i])) {renderer=renderers[i];return true;}
            renderer=null;return false;
        }
        public int DrawnPartCount {get;private set;}
        public override bool TryGetDrawnBounds(out Bounds bounds)
        {bounds=drawnBounds;return hasDrawnBounds && gameObject.activeInHierarchy;}
        public override string ActorId=>state?.id;
        public string DrawnClip=>pose;
        public int DrawnFrame=>frame;
        public float DrawnTime {get;private set;}
        public int DrawOrder
        {
            get => GetComponent<SortingGroup>().sortingOrder;
            set => GetComponent<SortingGroup>().sortingOrder=value;
        }

        public static void Prepare()=>PonyRenderSelection.Prepare();
        public static Bounds CameraBounds(ActorState actor)=>PonyRenderSelection.CameraBounds(actor);
        public static Bounds WorldBounds(ActorState actor)=>PonyRenderSelection.WorldBounds(actor);

        public override void Bind(ActorState actor)
        {
            // A replacement snapshot of the same actor has not changed the visible
            // meshes yet. Keep their actual bounds/frame until the next DrawPose.
            if(string.IsNullOrEmpty(actor?.id) || actor.id!=state?.id)
            {appearance=null;frame=-1;hasDrawnBounds=false;DrawnPartCount=0;drawnParts=null;magicDirty=true;}
            state = actor;
            var prepared=actor==null?null:PonyRenderSelection.PrepareEquipment(actor);
            // Preparing a different wardrobe does not draw it. Keep the published
            // frame/mesh metadata truthful until the next actual DrawPose, while
            // ensuring a stationary pose still applies the new plan then.
            if(!ReferenceEquals(drawPlan,prepared)) {drawPlan=prepared;drawPlanDirty=true;}
            int capacity = drawPlan?.Capacity??0;
            if(actor!=null && capacity==0) throw new InvalidOperationException("Графика пони не подготовлена перед созданием экземпляра.");
            if(authoredStructure)
            {
                if(filters.Count==0 || filters.Count!=renderers.Count)
                    throw new InvalidOperationException($"Префаб пони {name}: повреждён сохранённый пул частей filters={filters.Count}, renderers={renderers.Count}.");
                for(int i=0;i<filters.Count;i++)
                    if(filters[i]==null || renderers[i]==null || filters[i].transform!=renderers[i].transform || !filters[i].transform.IsChildOf(transform))
                        throw new InvalidOperationException($"Префаб пони {name}: повреждена ссылка на часть {i}.");
            }
            // A valid compiled mod may exceed the stock authored pool. Provision
            // on the explicit Bind event, after validating every authored link.
            // Unselected registered profiles never affect this actor's capacity.
            while (filters.Count < capacity)
            {
                var child = new GameObject("Часть пони"); child.transform.SetParent(transform, false);
                filters.Add(child.AddComponent<MeshFilter>());
                var renderer=child.AddComponent<MeshRenderer>();renderer.enabled=false;renderers.Add(renderer);
            }
        }
        public override void SetVisible(bool visible) => gameObject.SetActive(visible);
        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            DrawOrder=WorldVisualOrderPolicy.ActorPartOrder;
        }

        // Draw only after the movement/pose contact decision for this frame.
        private void LateUpdate()
        {
            DrawPose(state?.poseTime??0);
        }

        public void DrawPose(float sampleTime,bool rawClip=false)
        {
            if (state == null) return;
            DrawnTime=sampleTime;
            properties ??=new MaterialPropertyBlock();
            var body=state.BodyPosition;
            transform.position = new Vector3(body.x, body.y, 0);
            transform.localScale = new Vector3(state.facing, 1, 1);
            string next = ActorPose.Clip(state);
            if (!PonyRenderSelection.HasClip(next))
            {
                hasDrawnBounds=false;DrawnPartCount=0;
                Debug.LogWarning($"Нет анимации {next} для {state.id}; отображение пропущено.", this);
                gameObject.SetActive(false);
                return;
            }
            var nextAppearance=PonyAppearance.Snapshot(state);
            if(!appearance.HasValue || !appearance.Value.Equals(nextAppearance)) {appearance=nextAppearance;frame=-1;}
            if (pose != next) { pose = next; frame = -1; }
            if(drawPlan==null) throw new InvalidOperationException("Нет подготовленного плана пони: actor="+state.id+".");
            if((drawPlan.HasEquipment || drawPlan.HeadStyleId!=CharacterAppearance.OriginalHeadStyleId || nextAppearance.HeadStyleId!=CharacterAppearance.OriginalHeadStyleId) &&
                !drawPlan.MatchesShape(nextAppearance))
                throw new InvalidOperationException($"Внешность снаряжения изменена без подготовки: actor={state.id}, head={nextAppearance.HeadStyleId}, hair={nextAppearance.HairStyle}, eyes={nextAppearance.EyeStyle}, secondary={nextAppearance.SecondaryEnabled}. Вызовите Bind при событии смены формы.");
            // Original-head bare selections retain all 60 precompiled choices.
            // Custom head/style and equipment events prepare their plans in Bind.
            var frames = drawPlan.HasEquipment?drawPlan.Frames(pose):PonyRenderSelection.Frames(nextAppearance,pose);
            int nextFrame = rawClip?PonyAnimationData.FrameIndex(pose,sampleTime,ActorSessionRules.IsPassive(state),ActorPose.StationaryClimb(state)):PonyMotionFrames.Sample(state,sampleTime);
            if (nextFrame == frame && !drawPlanDirty && magicRoleVersion==PonyMagicRenderRoles.Version) {RefreshMagic(nextAppearance);RefreshDrawnBounds();return;}
            frame = nextFrame;
            var parts = frames[frame];
            drawnParts=parts;
            for (int i = 0; i < renderers.Count; i++)
            {
                renderers[i].enabled = i < parts.Length && parts[i].visible;
                if (i >= parts.Length) continue;
                filters[i].sharedMesh = parts[i].mesh;
                renderers[i].sharedMaterial = parts[i].material;
                renderers[i].sortingOrder = i + WorldVisualOrderPolicy.ActorPartOrder;
                properties.SetColor("_Color", PonyAppearance.Tint(parts[i].slot,nextAppearance));
                properties.SetFloat("_Emission",parts[i].slot=="magic"?1:0);
                renderers[i].SetPropertyBlock(properties);
            }
            drawPlanDirty=false;
            magicDirty=true;RefreshMagic(nextAppearance);
            RefreshDrawnBounds();
        }
        private void RefreshMagic(PonyAppearanceKey tint)
        {
            var activity=magicSource==null?new PonyMagicActivity(previewMagicOpacity,PonyMagicAction.None):magicSource.Get(state);
            float previous=DrawnMagicActivity.Opacity;DrawnMagicActivity=activity;
            if(!magicDirty && magicRoleVersion==PonyMagicRenderRoles.Version && activity.Opacity==previous) return;
            magicDirty=false;magicRoleVersion=PonyMagicRenderRoles.Version;
            if(drawnParts==null) return;
            for(int i=0;i<drawnParts.Length;i++)
            {
                var part=drawnParts[i];if(!PonyMagicRenderRoles.Contains(part)) continue;
                var color=PonyAppearance.Tint(part.slot,tint);color.a*=activity.Opacity;
                properties.SetColor("_Color",color);properties.SetFloat("_Emission",1);
                renderers[i].enabled=part.visible && activity.Opacity>0;renderers[i].SetPropertyBlock(properties);
            }
        }
        private void RefreshDrawnBounds()
        {
            hasDrawnBounds=false;DrawnPartCount=0;
            // These references were authored/captured at Bind. Renderer bounds read
            // the final mesh and Transform, respecting disabled parts and facing.
            for(int i=0;i<renderers.Count;i++)
            {
                var renderer=renderers[i];
                if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy || filters[i].sharedMesh==null) continue;
                var bounds=renderer.bounds;
                if(!hasDrawnBounds) {drawnBounds=bounds;hasDrawnBounds=true;}
                else drawnBounds.Encapsulate(bounds);
                DrawnPartCount++;
            }
        }
    }
}

