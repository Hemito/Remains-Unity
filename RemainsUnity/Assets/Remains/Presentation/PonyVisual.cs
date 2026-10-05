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
            state = actor;appearance=null;frame=-1;
            int capacity = PonyRenderSelection.Capacity;
            if(capacity==0) throw new InvalidOperationException("Графика пони не подготовлена перед созданием экземпляра.");
            if(authoredStructure)
            {
                if(filters.Count<capacity || filters.Count!=renderers.Count)
                    throw new InvalidOperationException($"Префаб пони {name}: нужно частей {capacity}, сохранено {filters.Count}. Пересоберите структуру.");
                for(int i=0;i<filters.Count;i++)
                    if(filters[i]==null || renderers[i]==null || filters[i].transform!=renderers[i].transform || !filters[i].transform.IsChildOf(transform))
                        throw new InvalidOperationException($"Префаб пони {name}: повреждена ссылка на часть {i}.");
                return;
            }
            while (filters.Count < capacity)
            {
                var child = new GameObject("Часть пони"); child.transform.SetParent(transform, false);
                filters.Add(child.AddComponent<MeshFilter>());
                renderers.Add(child.AddComponent<MeshRenderer>());
            }
        }
        public override void SetVisible(bool visible) => gameObject.SetActive(visible);
        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            DrawOrder=20;
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
            transform.position = new Vector3(state.x, state.y, 0);
            transform.localScale = new Vector3(state.facing, 1, 1);
            string next = ActorPose.Clip(state);
            if (!PonyRenderSelection.HasClip(next))
            {
                Debug.LogWarning($"Нет анимации {next} для {state.id}; отображение пропущено.", this);
                gameObject.SetActive(false);
                return;
            }
            var nextAppearance=PonyAppearance.Snapshot(state);
            if(!appearance.HasValue || !appearance.Value.Equals(nextAppearance)) {appearance=nextAppearance;frame=-1;}
            if (pose != next) { pose = next; frame = -1; }
            var frames = PonyRenderSelection.Frames(nextAppearance,pose);
            int nextFrame = rawClip?PonyAnimationData.FrameIndex(pose,sampleTime,ActorSessionRules.IsPassive(state),ActorPose.StationaryClimb(state)):PonyMotionFrames.Sample(state,sampleTime);
            if (nextFrame == frame) return;
            frame = nextFrame;
            var parts = frames[frame];
            for (int i = 0; i < renderers.Count; i++)
            {
                renderers[i].enabled = i < parts.Length && parts[i].visible;
                if (i >= parts.Length) continue;
                filters[i].sharedMesh = parts[i].mesh;
                renderers[i].sharedMaterial = parts[i].material;
                renderers[i].sortingOrder = i + 20;
                properties.SetColor("_Color", PonyAppearance.Tint(parts[i].slot,nextAppearance));
                properties.SetFloat("_Emission",parts[i].slot=="magic"?1:0);
                renderers[i].SetPropertyBlock(properties);
            }
        }
    }
}

