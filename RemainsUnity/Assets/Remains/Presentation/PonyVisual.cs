using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class PonyLibrary { public PonyClip[] clips; }
    [Serializable] public sealed class PonyClip { public string id; public float fps; public PonyFrame[] frames; }
    [Serializable] public sealed class PonyFrame { public PonyPart[] parts; }
    [Serializable] public sealed class PonyPart
    {
        public string shape, path;
        public float[] matrix, color, bounds;
    }

    // A compiled cache is shared by every pony. No asset lookup or mesh rebuild in Update.
    public sealed class PonyVisual : MonoBehaviour
    {
        private sealed class RenderPart { public Mesh mesh; public Material material; public bool hair; }
        private static readonly Dictionary<string, RenderPart[][]> cache = new();
        private readonly List<MeshFilter> filters = new();
        private readonly List<MeshRenderer> renderers = new();
        private ActorState state;
        private string pose;
        private int frame = -1;
        private float started;
        private MaterialPropertyBlock properties;

        public static void Prepare()
        {
            if (cache.Count > 0) return;
            var json = Resources.Load<TextAsset>("Original/Pony/pony");
            if (json == null) throw new InvalidOperationException("Отсутствуют данные анимации пони: Original/Pony/pony.");
            var library = JsonUtility.FromJson<PonyLibrary>(json.text);
            var materials = new Dictionary<string, Material>();
            foreach (var clip in library.clips)
            {
                var frames = new RenderPart[clip.frames.Length][];
                for (int f = 0; f < frames.Length; f++)
                {
                    var source = clip.frames[f].parts;
                    frames[f] = new RenderPart[source.Length];
                    for (int p = 0; p < source.Length; p++)
                    {
                        var part = source[p];
                        if (!materials.TryGetValue(part.shape, out var material))
                        {
                            var texture = Resources.Load<Texture2D>("Original/Pony/" + part.shape);
                            if (texture == null) throw new InvalidOperationException($"Отсутствует часть пони: shape={part.shape}, path={part.path}.");
                            material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture };
                            materials.Add(part.shape, material);
                        }
                        var corners = new[] { new Vector2(part.bounds[0],part.bounds[3]), new Vector2(part.bounds[2],part.bounds[3]),
                            new Vector2(part.bounds[0],part.bounds[1]), new Vector2(part.bounds[2],part.bounds[1]) };
                        var vertices = new Vector3[4];
                        for (int v = 0; v < 4; v++)
                        {
                            var c = corners[v]; var m = part.matrix;
                            vertices[v] = new Vector3((m[0]*c.x+m[2]*c.y+m[4])/1000,
                                -(m[1]*c.x+m[3]*c.y+m[5])/1000, 0);
                        }
                        var tint = new Color(part.color[0],part.color[1],part.color[2],part.color[3]);
                        var mesh = new Mesh { name = $"{clip.id}:{f}:{part.path}", vertices = vertices,
                            uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                            triangles = new[] { 0,2,1,2,3,1 }, colors = new[] {tint,tint,tint,tint} };
                        mesh.RecalculateBounds();
                        frames[f][p] = new RenderPart { mesh = mesh, material = material,
                            hair = part.path.Contains("hair") || part.path.Contains("mane") || part.path.Contains("tail") };
                    }
                }
                cache.Add(clip.id, frames);
            }
        }

        public void Bind(ActorState actor)
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
        private void Awake() => properties = new MaterialPropertyBlock();

        private void Update()
        {
            if (state == null) return;
            transform.position = new Vector3(state.x, state.y, 0);
            transform.localScale = new Vector3(state.facing, 1, 1);
            string next = state.down ? "down" : state.pose;
            if (!cache.ContainsKey(next))
            {
                Debug.LogWarning($"Нет анимации {next} для {state.id}; отображение пропущено.", this);
                gameObject.SetActive(false);
                return;
            }
            if (pose != next) { pose = next; started = Time.time; frame = -1; }
            var frames = cache[pose];
            int nextFrame = pose == "down" ? frames.Length - 1 : (int)((Time.time-started)*30) % frames.Length;
            if (nextFrame == frame) return;
            frame = nextFrame;
            var parts = frames[frame];
            for (int i = 0; i < renderers.Count; i++)
            {
                renderers[i].enabled = i < parts.Length;
                if (i >= parts.Length) continue;
                filters[i].sharedMesh = parts[i].mesh;
                renderers[i].sharedMaterial = parts[i].material;
                renderers[i].sortingOrder = i + 20;
                Color hair = state.hair % 2 == 0 ? Color.white : new Color(0.65f,0.8f,1);
                properties.SetColor("_Color", parts[i].hair ? hair : Color.white);
                renderers[i].SetPropertyBlock(properties);
            }
        }
    }
}
