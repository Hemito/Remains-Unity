using System;
using System.Collections;
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
    // drawing an actor's current shadow only reads already prepared textures. A scene change (a door) makes the
    // fields stale instead of dropping them: shadows keep the previous field until RebuildOneStale replaces it,
    // one field per call, so a door does not rebuild every lamp's field in one frame.
    public sealed class SteadyLightShadowCache : IDisposable
    {
        private readonly Dictionary<string, SteadyLightEmitter> catalog = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RenderTexture> fields = new(StringComparer.Ordinal);
        private readonly HashSet<string> requested = new(StringComparer.Ordinal);
        private readonly HashSet<string> retired = new(StringComparer.Ordinal);
        private readonly HashSet<string> stale = new(StringComparer.Ordinal);
        private readonly List<string> inactive = new();
        private bool disposed;

        public int Count => fields.Count;
        public int Generation { get; private set; }

        public void Register(IReadOnlyList<SteadyLightEmitter> emitters,IReadOnlyCollection<string> previousSourceIds=null)
        {
            ThrowIfDisposed();
            if (emitters == null) throw new ArgumentNullException(nameof(emitters));
            var previous=new Dictionary<string,SteadyLightEmitter>(catalog,StringComparer.Ordinal);
            foreach(string id in catalog.Keys) retired.Add(id);
            // A light-window replacement owns a new GPU cache, but actor shadow crossfades
            // can still reference sources published by the preceding window of this level.
            if(previousSourceIds!=null) foreach(string id in previousSourceIds)
            {
                if(string.IsNullOrEmpty(id)) throw new ArgumentException("История источников тени содержит пустой ID.");
                retired.Add(id);
            }
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
            inactive.Clear();
            foreach(var pair in fields)
            {
                if(!catalog.TryGetValue(pair.Key,out var next) || !previous.TryGetValue(pair.Key,out var old) ||
                    old.Sprite!=next.Sprite || old.LocalToWorld!=next.LocalToWorld || old.Radiance!=next.Radiance)
                    inactive.Add(pair.Key);
            }
            foreach(string id in inactive) {Release(fields[id]);fields.Remove(id);stale.Remove(id);}
            inactive.Clear();
        }

        public void Prepare(IReadOnlyCollection<string> ids,Func<SteadyLightEmitter,RenderTexture> build)
        {
            var steps=PrepareSteps(ids,build);
            try {while(steps.MoveNext()) {}} finally {(steps as IDisposable)?.Dispose();}
        }
        public IEnumerator PrepareSteps(IReadOnlyCollection<string> ids,Func<SteadyLightEmitter,RenderTexture> build)
            =>PrepareSteps(ids,(source,done)=>ImmediateBuild(source,done,build));
        private static IEnumerator ImmediateBuild(SteadyLightEmitter source,Action<RenderTexture> done,Func<SteadyLightEmitter,RenderTexture> build)
        {done(build(source));yield break;}
        public IEnumerator PrepareSteps(IReadOnlyCollection<string> ids, Func<SteadyLightEmitter,Action<RenderTexture>,IEnumerator> build)
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
                stale.Remove(id);
            }
            inactive.Clear();

            foreach (string id in requested)
            {
                if (fields.ContainsKey(id)) continue;
                RenderTexture texture=null;bool transferred=false;
                try
                {
                    var steps=build(catalog[id],result=>texture=result);
                    if(steps==null) throw new InvalidOperationException("Подготовка прямого света не вернула этапы: ID="+id);
                    try {while(steps.MoveNext()) yield return null;}
                    finally {(steps as IDisposable)?.Dispose();}
                    if (texture == null)
                    {
                        Debug.LogWarning($"SteadyLightShadowCache: построение поля вернуло пустую текстуру для ID='{id}', поколение {Generation}; поле пропущено.");
                        continue;
                    }
                    fields.Add(id, texture);transferred=true;
                }
                finally {if(!transferred && !ReferenceEquals(texture,null)) Release(texture);}
                yield return null;
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
            stale.UnionWith(fields.Keys);
            Generation++;
        }
        public bool HasStale => stale.Count > 0;
        // Rebuilds one stale field (the scene changed since it was built); false when none is left.
        public bool RebuildOneStale(Func<SteadyLightEmitter, RenderTexture> build)
        {
            ThrowIfDisposed();
            if (build == null) throw new ArgumentNullException(nameof(build));
            string id = null;
            foreach (string candidate in stale) { id = candidate; break; }
            if (id == null) return false;
            stale.Remove(id);
            if (!fields.TryGetValue(id, out var old) || !catalog.TryGetValue(id, out var emitter)) return true;
            RenderTexture texture = build(emitter);
            if (texture == null)
            {
                Debug.LogWarning($"SteadyLightShadowCache: пересборка поля вернула пустую текстуру для ID='{id}', поколение {Generation}; оставлено прежнее поле.");
                return true;
            }
            fields[id] = texture;
            if (!ReferenceEquals(old, texture)) Release(old);
            return true;
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
            stale.Clear();
        }

        private static void Release(RenderTexture texture)=>LightTextureBudget.Release(texture);

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SteadyLightShadowCache));
        }
    }
}
