using System;
using System.Collections;
using System.Collections.Generic;
using Remains.RoomFormat;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    // Pose drawing is lightweight animation. Source/grid analysis is an explicit cached
    // rebuild, scheduled after movement or a lighting revision, never in LateUpdate.
    public sealed class ActorShadowPresentation : IDisposable
    {
        private sealed class Entry
        {
            public ActorCastShadowView View;
            public readonly List<LightOccluder> Parts=new();
            public readonly List<ActorShadowSource> Lamps=new(),Sources=new();
            public readonly List<ActorShadowSource> DisplayLamps=new();
            // A new selection waits here until its sources' direct fields exist (or PendingDeadlineSeconds), so a lamp
            // never takes over a shadow it cannot cast yet: the previous lamps keep it meanwhile.
            public readonly List<ActorShadowSource> PendingLamps=new();
            public bool HasPending;
            public float PendingSince;
            public readonly List<ActorShadowSource> Flashes=new();
            public readonly Dictionary<string,(Vector2 Position,Vector2 ActorPosition,float Transmission,long Revision,int Frame)> FlashVisibility=new();
            public readonly List<string> ExpiredFlashes=new();
            public Vector2 Position,SelectedAt;
            public long Revision=-1;
            public bool Queued,Culled;
        }
        private readonly MonoBehaviour runner;
        private readonly Transform parent;
        private readonly ActorCastShadowView prefab;
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly HashSet<string> retained=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        private readonly HashSet<string> steadySources=new(StringComparer.Ordinal);
        private FactoryLevelView level;
        private Coroutine refresh;
        private readonly List<(Entry Entry,Vector2 At)> batch=new();
        private readonly HashSet<string> warnedPending=new(StringComparer.Ordinal);
        private const float PendingDeadlineSeconds=1;
        // One worker task selects for every queued actor, on a few threads: a task per actor crowded the cores and
        // the round waited for the slowest, so shadows took a new selection only a few times a second.
        private static readonly int SelectionThreads=Mathf.Clamp(Environment.ProcessorCount/4,1,4);

        public ActorShadowPresentation(MonoBehaviour runner,Transform parent)
        {
            this.runner=runner??throw new ArgumentNullException(nameof(runner));
            this.parent=parent;
            prefab=GameAssets.Load<ActorCastShadowView>("Presentation/ActorCastShadow")??
                throw new InvalidOperationException("Нет авторского префаба проецируемой тени персонажа.");
        }
        public void CollectProjectedLightShadows(List<ProjectedLightShadow> output)
        {
            if(output==null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            foreach(var entry in entries.Values) entry.View.AppendProjectedLightShadows(output);
        }
        public static float FlashProjectionScale(RoomLightingTable settings)
        {
            if(settings==null || !double.IsFinite(settings.ActorShadowActorDepth) ||
                !double.IsFinite(settings.ActorShadowFlashDepth) || settings.ActorShadowActorDepth<=0 ||
                settings.ActorShadowFlashDepth<=settings.ActorShadowActorDepth ||
                !double.IsFinite(settings.ActorShadowMaxScale) || settings.ActorShadowMaxScale<1)
                throw new InvalidOperationException("Проекция вспышки: требуется 0 < actorDepth < flashDepth и конечный maxScale >= 1.");
            return Mathf.Clamp((float)(settings.ActorShadowFlashDepth/(settings.ActorShadowFlashDepth-settings.ActorShadowActorDepth)),
                1,(float)settings.ActorShadowMaxScale);
        }

        // area: where shadows can be seen (the camera's animation area); actors outside it keep their entry but draw nothing,
        // so a level of ~240 NPCs costs only the few near the camera. Null draws every actor (checks, previews).
        public void Present(FactoryLevelView current,IReadOnlyDictionary<string,ActorVisual> actors,
            InteractionVisuals weapons,IReadOnlyList<SpriteEmission> flashes,IReadOnlyList<string> flashIds=null,Rect? area=null)
        {
            if(current!=level) {Clear();level=current;}
            if(level==null || !level.IsReady) {Clear();return;}
            var settings=level.ShadowSettings;
            retained.Clear();removed.Clear();
            foreach(var pair in actors)
            {
                if(pair.Value==null || !pair.Value.gameObject.activeInHierarchy) continue;
                retained.Add(pair.Key);
                if(Outside(area,pair.Value))
                {
                    // A hidden shadow keeps no lamp selection: its direct fields are released, and nothing is reselected for it.
                    if(entries.TryGetValue(pair.Key,out var hidden) && !hidden.Culled)
                    {
                        hidden.View.Clear();hidden.Culled=true;hidden.Queued=false;hidden.Revision=-1;   // forces a reselection on return
                        hidden.Lamps.Clear();hidden.DisplayLamps.Clear();hidden.PendingLamps.Clear();hidden.HasPending=false;
                    }
                    continue;
                }
                if(!entries.TryGetValue(pair.Key,out var entry))
                {
                    entry=new Entry{View=UnityEngine.Object.Instantiate(prefab,parent,false)};
                    entry.View.name="Проецируемая тень: "+pair.Key;entries.Add(pair.Key,entry);
                }
                using(Remains.Diagnostics.FrameHitchLog.Section("Тени: силуэты частей"))
                {entry.Parts.Clear();pair.Value.AppendLightOccluders(entry.Parts);
                weapons.AppendWeaponLightOccluders(pair.Key,entry.Parts);}
                // Back in view: select lamps again and wait for their fields (the pending rule), as for a new actor.
                if(entry.Culled) {entry.Culled=false;entry.Queued=true;}
                if(entry.Parts.Count==0) {entry.View.Clear();continue;}
                entry.Position=pair.Value.TryGetDrawnBounds(out var bounds)?(Vector2)bounds.center:
                    PartsCenter(entry.Parts);
                if(entry.Revision!=level.ShadowSourceRevision ||
                    (entry.Position-entry.SelectedAt).sqrMagnitude>=settings.ActorShadowMoveThreshold*settings.ActorShadowMoveThreshold)
                    entry.Queued=true;
                AdoptPending(entry);
                BlendLampSources(entry.DisplayLamps,entry.Lamps,Time.deltaTime,settings.ActorShadowMaxSources);
                // Element copy: Mono's List.AddRange(ICollection) allocates a temporary array (~240 actors every frame).
                entry.Sources.Clear();foreach(var source in entry.DisplayLamps) entry.Sources.Add(source);entry.Flashes.Clear();
                if(flashes!=null) for(int i=0;i<flashes.Count;i++)
                {
                    var flash=flashes[i];
                    if(flash.Sprite==null || flash.Gain<=0 || !float.IsFinite(flash.Gain)) continue;
                    Vector2 position=flash.LocalToWorld.MultiplyPoint3x4(flash.Sprite.bounds.center);
                    var size=flash.Sprite.bounds.size;var scale=flash.LocalToWorld.lossyScale;
                    float radius=Mathf.Max(Mathf.Abs(size.x*scale.x),Mathf.Abs(size.y*scale.y))*.25f;
                    if(flashIds==null || i>=flashIds.Count || string.IsNullOrEmpty(flashIds[i]))
                        throw new InvalidOperationException("Проекция вспышки: отсутствует стабильный ID выстрела.");
                    string id=flashIds[i];
                    float strength=flash.Gain/(1+flash.Gain)/Mathf.Max(1,Vector2.Distance(position,entry.Position));
                    float projectionScale=FlashProjectionScale(settings);
                    entry.Flashes.Add(new ActorShadowSource(id,position,radius,strength,true,projectionScale));
                    if(entry.FlashVisibility.TryGetValue(id,out var cached) && cached.Revision==level.ShadowSourceRevision)
                    {
                        entry.Sources.Add(new ActorShadowSource(id,position,radius,strength*cached.Transmission,true,projectionScale));
                        if((cached.Position-position).sqrMagnitude>=settings.ActorShadowMoveThreshold*settings.ActorShadowMoveThreshold ||
                            (cached.ActorPosition-entry.Position).sqrMagnitude>=settings.ActorShadowMoveThreshold*settings.ActorShadowMoveThreshold)
                            entry.Queued=true;
                        entry.FlashVisibility[id]=(cached.Position,cached.ActorPosition,cached.Transmission,cached.Revision,Time.frameCount);
                    }
                    else entry.Queued=true;
                }
                entry.ExpiredFlashes.Clear();
                foreach(var cached in entry.FlashVisibility)
                    if(Time.frameCount-cached.Value.Frame>4) entry.ExpiredFlashes.Add(cached.Key);
                foreach(string id in entry.ExpiredFlashes) entry.FlashVisibility.Remove(id);
                if(entry.View.NeedsPreparation(entry.Parts,entry.Sources.Count)) entry.Queued=true;
                using(Remains.Diagnostics.FrameHitchLog.Section("Тени: рисование проекции")) entry.View.Draw(entry.Position,entry.Parts,entry.Sources,settings);
            }
            foreach(var pair in entries) if(!retained.Contains(pair.Key)) removed.Add(pair.Key);
            foreach(var id in removed) {Destroy(entries[id]);entries.Remove(id);}
            if(refresh==null) foreach(var pair in entries) if(pair.Value.Queued)
                {refresh=runner.StartCoroutine(RebuildSources());break;}
        }

        // Shot event preparation keeps CPU visibility and pool allocation out of LateUpdate,
        // while making the first visible flash use a ready cache and target slot immediately.
        private static bool Outside(Rect? area,ActorVisual visual)=>area is Rect seen &&
            !seen.Contains(visual.TryGetDrawnBounds(out var drawn)?(Vector2)drawn.center:(Vector2)visual.transform.position);
        // area as in Present: a shot prepares flash shadows only for actors that can be seen.
        public void PrepareFlash(FactoryLevelView current,IReadOnlyDictionary<string,ActorVisual> actors,
            InteractionVisuals weapons,string id,SpriteEmission flash,Rect? area=null)
        {
            if(current!=level) {Clear();level=current;}
            if(level==null || !level.IsReady || flash.Sprite==null) return;
            if(string.IsNullOrEmpty(id)) throw new ArgumentException("Нет стабильного ID вспышки.",nameof(id));
            Vector2 sourcePosition=flash.LocalToWorld.MultiplyPoint3x4(flash.Sprite.bounds.center);
            foreach(var actor in actors)
            {
                if(actor.Value==null || !actor.Value.gameObject.activeInHierarchy || Outside(area,actor.Value)) continue;
                if(!entries.TryGetValue(actor.Key,out var entry))
                {
                    entry=new Entry{View=UnityEngine.Object.Instantiate(prefab,parent,false)};
                    entry.View.name="Проецируемая тень: "+actor.Key;entries.Add(actor.Key,entry);
                }
                entry.Parts.Clear();actor.Value.AppendLightOccluders(entry.Parts);
                weapons.AppendWeaponLightOccluders(actor.Key,entry.Parts);
                if(entry.Parts.Count==0) continue;
                entry.Position=actor.Value.TryGetDrawnBounds(out var bounds)?(Vector2)bounds.center:PartsCenter(entry.Parts);
                // Lamp selection stays with the background rebuild (a synchronous selection of every actor here cost
                // tens of milliseconds per shot); the flash needs only its own visibility.
                if(entry.Revision!=level.ShadowSourceRevision) entry.Queued=true;
                entry.FlashVisibility[id]=(sourcePosition,entry.Position,
                    level.ActorShadowTransmission(entry.Position,sourcePosition),level.ShadowSourceRevision,Time.frameCount);
                entry.View.Prepare(entry.Parts,2*level.ShadowSettings.ActorShadowMaxSources+entry.FlashVisibility.Count);
            }
            PrepareSteadyFields();
        }

        public static void BlendLampSources(List<ActorShadowSource> displayed,IReadOnlyList<ActorShadowSource> targets,float dt,int maxSources=3)
        {
            float blend=Mathf.Clamp01(Mathf.Max(0,dt)/.08f);
            for(int i=displayed.Count-1;i>=0;i--)
            {
                var old=displayed[i];bool found=false;
                for(int j=0;j<targets.Count;j++) if(targets[j].Id==old.Id)
                {
                    var target=targets[j];
                    // A partly occluded long lamp changes its effective emitting centre as well as
                    // its power. Interpolate both so a cached visibility update cannot snap the shadow.
                    displayed[i]=new ActorShadowSource(target.Id,Vector2.Lerp(old.Position,target.Position,blend),target.Radius,Mathf.Lerp(old.Strength,target.Strength,blend),target.IsTransient,target.ProjectionScale,
                        coverage:Mathf.Lerp(old.Coverage,target.Coverage,blend));
                    found=true;break;
                }
                if(found) continue;
                float strength=Mathf.Lerp(old.Strength,0,blend);
                float coverage=Mathf.Lerp(old.Coverage,0,blend);
                if(strength<=.0001f && coverage<=.0001f) displayed.RemoveAt(i);
                else displayed[i]=new ActorShadowSource(old.Id,old.Position,old.Radius,strength,old.IsTransient,old.ProjectionScale,coverage:coverage);
            }
            foreach(var target in targets)
            {
                bool found=false;foreach(var source in displayed) if(source.Id==target.Id) {found=true;break;}
                if(!found && target.Strength>0)
                    displayed.Add(new ActorShadowSource(target.Id,target.Position,target.Radius,target.Strength*blend,target.IsTransient,target.ProjectionScale,coverage:target.Coverage*blend));
            }
            // Reserve two source sets for a crossfade, never an unbounded history
            // of formerly selected lamps during fast movement.
            while(displayed.Count>2*maxSources)
            {
                int weakest=-1;
                for(int i=0;i<displayed.Count;i++)
                {
                    bool active=false;foreach(var target in targets) if(target.Id==displayed[i].Id) {active=true;break;}
                    if(!active && (weakest<0 || displayed[i].Strength<displayed[weakest].Strength)) weakest=i;
                }
                if(weakest<0) throw new InvalidOperationException("Число выбранных ламп превышает лимит проекций тени.");
                displayed.RemoveAt(weakest);
            }
        }

        private IEnumerator RebuildSources()
        {
            // First yield avoids executing the grid traversal inside the pose callback.
            yield return null;
            // Lamp selection (ray traces to every lamp of the window) runs on worker threads; the frame only starts
            // the jobs and adopts results that still match the window and lamp/door revision they were taken for.
            var owner=level;
            if(owner!=null && owner.IsReady)
            {
                batch.Clear();
                System.Threading.Tasks.Task<List<ActorShadowSource>[]> task=null;long revision=owner.ShadowSourceRevision;
                using(Remains.Diagnostics.FrameHitchLog.Section("Тени: запуск выбора ламп"))
                {
                    foreach(var pair in entries)
                    {
                        var entry=pair.Value;
                        if(entry.Queued && (entry.Revision!=revision ||
                            (entry.Position-entry.SelectedAt).sqrMagnitude>=owner.ShadowSettings.ActorShadowMoveThreshold*owner.ShadowSettings.ActorShadowMoveThreshold))
                            batch.Add((entry,entry.Position));
                    }
                    if(batch.Count>0)
                    {
                        var select=owner.CaptureActorShadowSelector();
                        var positions=new Vector2[batch.Count];for(int i=0;i<positions.Length;i++) positions[i]=batch[i].At;
                        task=System.Threading.Tasks.Task.Run(()=>
                        {
                            var results=new List<ActorShadowSource>[positions.Length];
                            System.Threading.Tasks.Parallel.For(0,positions.Length,new System.Threading.Tasks.ParallelOptions {MaxDegreeOfParallelism=SelectionThreads},
                                i=>results[i]=select(positions[i]));
                            return results;
                        });
                    }
                }
                if(task!=null)
                {
                    while(!task.IsCompleted) yield return null;
                    if(level!=owner || !owner.IsReady) {refresh=null;yield break;}
                    var results=task.GetAwaiter().GetResult();   // a failed selection throws here, as the synchronous call did
                    if(revision==owner.ShadowSourceRevision)   // otherwise stale: reselected next frame
                        for(int i=0;i<batch.Count;i++)
                        {
                            var entry=batch[i].Entry;
                            if(!entries.ContainsValue(entry) || entry.Culled) continue;
                            entry.PendingLamps.Clear();entry.PendingLamps.AddRange(results[i]);
                            if(!entry.HasPending) entry.PendingSince=Time.unscaledTime;
                            entry.HasPending=true;
                            entry.SelectedAt=batch[i].At;entry.Revision=revision;
                        }
                }
            }
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Тени: источники персонажей");
            if(level!=null && level.IsReady) foreach(var pair in entries)
            {
                var entry=pair.Value;if(!entry.Queued) continue;
                using(Remains.Diagnostics.FrameHitchLog.Section("Тени: видимость вспышек"))
                foreach(var flash in entry.Flashes)
                    entry.FlashVisibility[flash.Id]=(flash.Position,entry.Position,
                        level.ActorShadowTransmission(entry.Position,flash.Position),level.ShadowSourceRevision,Time.frameCount);
                using(Remains.Diagnostics.FrameHitchLog.Section("Тени: подготовка вида")) entry.View.Prepare(entry.Parts,2*level.ShadowSettings.ActorShadowMaxSources+entry.Flashes.Count);
                entry.Queued=false;
            }
            PrepareSteadyFields();
            refresh=null;
        }
        private void PrepareSteadyFields()
        {
            if(level==null || !level.IsReady) return;
            steadySources.Clear();
            foreach(var entry in entries.Values)
            {
                if(entry.Culled) continue;   // a selection finished after the actor left view must not hold fields
                foreach(var source in entry.Lamps) steadySources.Add(source.Id);
                foreach(var source in entry.DisplayLamps) steadySources.Add(source.Id);
                if(entry.HasPending) foreach(var source in entry.PendingLamps) steadySources.Add(source.Id);
            }
            level.PrepareActorShadowFields(steadySources);
        }
        private void AdoptPending(Entry entry)
        {
            if(!entry.HasPending) return;
            if(!level.ActorShadowFieldsReady(entry.PendingLamps))
            {
                if(Time.unscaledTime-entry.PendingSince<PendingDeadlineSeconds) return;
                foreach(var source in entry.PendingLamps)
                    if(warnedPending.Add(source.Id))
                        Debug.LogWarning($"Тени: поле прямого света {source.Id} не готово за {PendingDeadlineSeconds} с после выбора; выбор принят, тень от этого источника появится, когда поле будет построено.");
            }
            entry.Lamps.Clear();entry.Lamps.AddRange(entry.PendingLamps);
            entry.PendingLamps.Clear();entry.HasPending=false;
        }
        private static Vector2 PartsCenter(List<LightOccluder> parts)
        {
            // Exact bounds of the supplied drawing, also for sprite actors that do not expose pony bounds.
            Vector2 min=new(float.PositiveInfinity,float.PositiveInfinity),max=new(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var part in parts)
            {
                var bounds=part.Sprite!=null?part.Sprite.bounds:part.Mesh.bounds;
                for(int i=0;i<4;i++)
                {
                    Vector2 point=part.LocalToWorld.MultiplyPoint3x4(new Vector3((i&1)==0?bounds.min.x:bounds.max.x,
                        (i&2)==0?bounds.min.y:bounds.max.y,0));
                    min=Vector2.Min(min,point);max=Vector2.Max(max,point);
                }
            }
            return (min+max)*.5f;
        }
        private static void Destroy(Entry entry)
        {entry.View.Clear();entry.View.gameObject.SetActive(false);UnityEngine.Object.Destroy(entry.View.gameObject);}
        public void Clear()
        {
            if(refresh!=null) {runner.StopCoroutine(refresh);refresh=null;}
            foreach(var pair in entries) Destroy(pair.Value);
            entries.Clear();level=null;
        }
        public void Dispose()=>Clear();
    }
}
