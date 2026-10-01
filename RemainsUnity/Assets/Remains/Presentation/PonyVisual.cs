using System;
using System.Collections.Generic;
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
        private sealed class RenderPart { public Mesh mesh; public Material material; public string slot; public bool visible; }
        private static readonly Dictionary<string, RenderPart[][]> cache = new();
        private static readonly Dictionary<string, Bounds[]> poseBounds = new();
        private readonly List<MeshFilter> filters = new();
        private readonly List<MeshRenderer> renderers = new();
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
            if (cache.Count > 0) return;
            PonyAnimationData.Load();
            var library = PonyAnimationData.Library;
            var materials = new Dictionary<string, Material>();
            foreach (var clip in library.clips)
            {
                var frames = new RenderPart[clip.frames.Length][];
                var bounds = new Bounds[frames.Length];
                for (int f = 0; f < frames.Length; f++)
                {
                    var source = clip.frames[f].parts;
                    frames[f] = new RenderPart[source.Length];
                    bool hasBounds=false;
                    for (int p = 0; p < source.Length; p++)
                    {
                        var part = source[p];
                        if(part.slot is not ("none" or "fur" or "hair" or "hairSecondary" or "eye" or "magic"))
                            throw new InvalidOperationException($"Неизвестная цветовая область {part.slot}: shape={part.shape}, path={part.path}.");
                        if (string.IsNullOrEmpty(part.texture)) throw new InvalidOperationException($"Не задана текстура части пони: shape={part.shape}, path={part.path}.");
                        if (!materials.TryGetValue(part.texture, out var material))
                        {
                            var texture = Resources.Load<Texture2D>("Original/Pony/" + part.texture);
                            if (texture == null) throw new InvalidOperationException($"Отсутствует часть пони: shape={part.shape}, path={part.path}.");
                            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
                            materials.Add(part.texture, material);
                        }
                        var corners = new[] { new Vector2(part.bounds[0],part.bounds[3]), new Vector2(part.bounds[2],part.bounds[3]),
                            new Vector2(part.bounds[0],part.bounds[1]), new Vector2(part.bounds[2],part.bounds[1]) };
                        var vertices = new Vector3[4];
                        for (int v = 0; v < 4; v++)
                        {
                            vertices[v] = PonyAnimationData.TransformPoint(part,corners[v]);
                        }
                        var tint = new Color(part.color[0],part.color[1],part.color[2],part.color[3]);
                        var mesh = new Mesh { name = $"{clip.id}:{f}:{part.path}", vertices = vertices,
                            uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                            triangles = new[] { 0,2,1,2,3,1 }, colors = new[] {tint,tint,tint,tint} };
                        mesh.RecalculateBounds();
                        frames[f][p] = new RenderPart { mesh = mesh, material = material,
                            slot = part.slot, visible = PonyAppearance.IsVisible(part) };
                        if(frames[f][p].visible)
                        {
                            if(!hasBounds) {bounds[f]=mesh.bounds;hasBounds=true;}
                            else bounds[f].Encapsulate(mesh.bounds);
                        }
                    }
                    if(!hasBounds) throw new InvalidOperationException($"Нет видимых частей позы: clip={clip.id}, frame={f}.");
                }
                cache.Add(clip.id, frames);
                poseBounds.Add(clip.id,bounds);
            }
        }

        public static Bounds WorldBounds(ActorState actor)
        {
            string clip=ActorPose.Clip(actor);
            int sample=PonyAnimationData.FrameIndex(clip,actor.poseTime,actor.down);
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

        public void DrawPose(float sampleTime)
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
            int nextFrame = PonyAnimationData.FrameIndex(pose,sampleTime,state.down);
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
                Color hair = state.hair % 2 == 0 ? new Color(133/255f,70/255f,9/255f) : new Color(0.15f,0.5f,0.85f);
                Color tint = parts[i].slot switch
                {
                    "hair"=>hair, "fur"=>new Color(163/255f,163/255f,163/255f),
                    "eye"=>new Color(22/255f,243/255f,67/255f), "magic"=>Color.green, _=>Color.white
                };
                properties.SetColor("_Color", tint);
                renderers[i].SetPropertyBlock(properties);
            }
        }
    }
}
