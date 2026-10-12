using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public readonly struct SteadyLightEmitter
    {
        public readonly string Id;
        public readonly Sprite Sprite;
        public readonly Matrix4x4 LocalToWorld;
        public readonly Color Radiance;

        public SteadyLightEmitter(string id, Sprite sprite, Matrix4x4 localToWorld, Color radiance)
        { Id = id; Sprite = sprite; LocalToWorld = localToWorld; Radiance = radiance; }
    }

    // Owns isolated direct-light fields. Catalog changes and preparation are explicit events;
    // drawing an actor's current shadow only reads already prepared textures.
    public sealed class SteadyLightShadowCache : IDisposable
    {
        private readonly Dictionary<string, SteadyLightEmitter> catalog = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RenderTexture> fields = new(StringComparer.Ordinal);
        private readonly HashSet<string> requested = new(StringComparer.Ordinal);
        private readonly HashSet<string> retired = new(StringComparer.Ordinal);
        private readonly List<string> inactive = new();
        private bool disposed;

        public int Count => fields.Count;
        public int Generation { get; private set; }

        public void Register(IReadOnlyList<SteadyLightEmitter> emitters)
        {
            ThrowIfDisposed();
            if (emitters == null) throw new ArgumentNullException(nameof(emitters));
            ClearFields();
            foreach(string id in catalog.Keys) retired.Add(id);
            catalog.Clear();
            Generation++;
            for (int i = 0; i < emitters.Count; i++)
            {
                var emitter = emitters[i];
                if (string.IsNullOrEmpty(emitter.Id) || emitter.Sprite == null)
                {
                    Debug.LogWarning($"SteadyLightShadowCache: источник #{i}, ID='{emitter.Id}', поколение {Generation}: отсутствует ID или спрайт излучения; источник пропущен.");
                    continue;
                }
                if (!catalog.TryAdd(emitter.Id, emitter))
                    Debug.LogWarning($"SteadyLightShadowCache: повторный ID='{emitter.Id}', источник #{i}, поколение {Generation}; повторная запись пропущена.");
                retired.Remove(emitter.Id);
            }
        }

        public void Prepare(IReadOnlyCollection<string> ids, Func<SteadyLightEmitter, RenderTexture> build)
        {
            ThrowIfDisposed();
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            if (build == null) throw new ArgumentNullException(nameof(build));
            requested.Clear();
            foreach (string id in ids)
            {
                // A source explicitly removed by a light-state event emits nothing during its
                // old presentation mask's crossfade. This is known state, not missing content.
                if(id!=null && retired.Contains(id)) continue;
                if (string.IsNullOrEmpty(id) || !catalog.ContainsKey(id))
                {
                    Debug.LogWarning($"SteadyLightShadowCache: не найден источник ID='{id}', поколение {Generation}, записей каталога {catalog.Count}; поле прямого света не построено.");
                    continue;
                }
                requested.Add(id);
            }

            inactive.Clear();
            foreach (var entry in fields)
                if (!requested.Contains(entry.Key)) inactive.Add(entry.Key);
            foreach (string id in inactive)
            {
                Release(fields[id]);
                fields.Remove(id);
            }
            inactive.Clear();

            foreach (string id in requested)
            {
                if (fields.ContainsKey(id)) continue;
                RenderTexture texture = build(catalog[id]);
                if (texture == null)
                {
                    Debug.LogWarning($"SteadyLightShadowCache: построение поля вернуло пустую текстуру для ID='{id}', поколение {Generation}; поле пропущено.");
                    continue;
                }
                fields.Add(id, texture);
            }
        }

        public bool TryGet(string id, out RenderTexture texture)
        {
            texture = null;
            return !disposed && !string.IsNullOrEmpty(id) && fields.TryGetValue(id, out texture);
        }
        public bool IsRegistered(string id)=>!disposed && id!=null && catalog.ContainsKey(id);

        public void InvalidateFields()
        {
            ThrowIfDisposed();
            ClearFields();
            Generation++;
        }

        public void Dispose()
        {
            if (disposed) return;
            ClearFields();
            catalog.Clear();
            requested.Clear();
            retired.Clear();
            inactive.Clear();
            disposed = true;
        }

        private void ClearFields()
        {
            foreach (var entry in fields) Release(entry.Value);
            fields.Clear();
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SteadyLightShadowCache));
        }
    }
}
