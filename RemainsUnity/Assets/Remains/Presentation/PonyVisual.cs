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

        private static readonly Dictionary<string, PonyRenderPart[][]> cache = new();
        private static readonly Dictionary<string, Bounds[]> poseBounds = new();
        private static Bounds cameraEnvelope;
        [SerializeField,HideInInspector] private List<MeshFilter> filters = new();
        [SerializeField,HideInInspector] private List<MeshRenderer> renderers = new();
        [SerializeField,HideInInspector] private bool authoredStructure;
        public void UseAuthoredStructure()=>authoredStructure=true;
        private ActorState state;
        private string pose;
        private int frame = -1;
        private MaterialPropertyBlock properties;
        public override string ActorId=>state?.id;
        public string DrawnClip=>pose;
        public int DrawnFrame=>frame;
        public float DrawnTime {get;private set;}
        public int DrawOrder
        {
            get => GetComponent<SortingGroup>().sortingOrder;
            set => GetComponent<SortingGroup>().sortingOrder=value;
        }

        public static void Prepare()
        {
            PonyAppearance.Prepare();
            if(cache.Count>0 && cache.Values.All(clip=>clip.All(sample=>sample.All(part=>part.mesh!=null && part.material!=null)))) return;
            var asset=Resources.Load<PonyRenderAsset>("Original/Pony/render");
            if(asset==null) throw new InvalidOperationException("Нет сохранённой графики пони Original/Pony/render. Пересоберите её в Unity.");
            asset.Validate();PonyAnimationData.Load();cache.Clear();poseBounds.Clear();
            foreach(var clip in asset.Clips)
            {
                cache.Add(clip.id,clip.frames.Select(frame=>frame.parts).ToArray());
                poseBounds.Add(clip.id,clip.frames.Select(frame=>frame.bounds).ToArray());
            }
            cameraEnvelope=asset.Clips[0].frames[0].bounds;
            foreach(var clip in asset.Clips) foreach(var sample in clip.frames) cameraEnvelope.Encapsulate(sample.bounds);
            // Facing and animation phase cannot change the camera composition envelope.
            float extent=Mathf.Max(Mathf.Abs(cameraEnvelope.min.x),Mathf.Abs(cameraEnvelope.max.x));
            cameraEnvelope.SetMinMax(new Vector3(-extent,cameraEnvelope.min.y,cameraEnvelope.min.z),
                new Vector3(extent,cameraEnvelope.max.y,cameraEnvelope.max.z));
        }
        public static Bounds CameraBounds(ActorState actor)
        {
            if(poseBounds.Count==0) throw new InvalidOperationException("Границы камеры пони не подготовлены.");
            var bounds=cameraEnvelope;bounds.center+=new Vector3(actor.x,actor.y,0);return bounds;
        }
        public static Bounds WorldBounds(ActorState actor)
        {
            string clip=ActorPose.Clip(actor);
            int sample=PonyMotionFrames.Sample(actor,actor.poseTime);
            if(!poseBounds.TryGetValue(clip,out var frames))
                throw new InvalidOperationException($"Нет границ позы: actor={actor.id}, clip={clip}.");
            var bounds=frames[sample];
            bounds.center=new Vector3(actor.x+bounds.center.x*actor.facing,actor.y+bounds.center.y,bounds.center.z);
            return bounds;
        }

        public override void Bind(ActorState actor)
        {
            state = actor;
            int capacity = 0;
            foreach (var clip in cache.Values) foreach (var sample in clip) capacity = Mathf.Max(capacity,sample.Length);
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

        private void Update()
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
            if (!cache.ContainsKey(next))
            {
                Debug.LogWarning($"Нет анимации {next} для {state.id}; отображение пропущено.", this);
                gameObject.SetActive(false);
                return;
            }
            if (pose != next) { pose = next; frame = -1; }
            var frames = cache[pose];
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
                properties.SetColor("_Color", PonyAppearance.Tint(parts[i].slot,state.hair));
                properties.SetFloat("_Emission",parts[i].slot=="magic"?1:0);
                renderers[i].SetPropertyBlock(properties);
            }
        }
    }
}

