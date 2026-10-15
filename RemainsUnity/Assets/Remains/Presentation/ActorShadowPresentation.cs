using System;
using System.Collections;
using System.Collections.Generic;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Presentation
{
    // Pose drawing is lightweight animation. Source/grid analysis is an explicit cached
    // rebuild, scheduled after movement or a lighting revision, never in LateUpdate.
    public sealed class ActorShadowPresentation : IDisposable
    {
        private sealed class Entry
        {
            public ActorCastShadowView View;
            public readonly List<RoomRadianceField.LightOccluder> Parts=new();
            public readonly List<ActorShadowSource> Lamps=new(),Sources=new();
            public readonly List<ActorShadowSource> DisplayLamps=new();
            public readonly List<ActorShadowSource> Flashes=new();
            public readonly Dictionary<string,(Vector2 Position,Vector2 ActorPosition,float Transmission,long Revision,int Frame)> FlashVisibility=new();
            public readonly List<string> ExpiredFlashes=new();
            public Vector2 Position,SelectedAt;
            public long Revision=-1;
            public bool Queued;
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
        private readonly List<(Entry Entry,Vector2 At,long Revision,System.Threading.Tasks.Task<List<ActorShadowSource>> Task)> jobs=new();

        public ActorShadowPresentation(MonoBehaviour runner,Transform parent)
        {
            this.runner=runner??throw new ArgumentNullException(nameof(runner));
            this.parent=parent;
            prefab=Resources.Load<ActorCastShadowView>("Presentation/ActorCastShadow")??
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

        public void Present(FactoryLevelView current,IReadOnlyDictionary<string,ActorVisual> actors,
            InteractionVisuals weapons,IReadOnlyList<RoomRadianceField.SpriteEmission> flashes,IReadOnlyList<string> flashIds=null)
        {
            if(current!=level) {Clear();level=current;}
            if(level==null || !level.IsReady) {Clear();return;}
            var settings=level.ShadowSettings;
            retained.Clear();removed.Clear();
            foreach(var pair in actors)
            {
                if(pair.Value==null || !pair.Value.gameObject.activeInHierarchy) continue;
                retained.Add(pair.Key);
                if(!entries.TryGetValue(pair.Key,out var entry))
                {
                    entry=new Entry{View=UnityEngine.Object.Instantiate(prefab,parent,false)};
                    entry.View.name="Проецируемая тень: "+pair.Key;entries.Add(pair.Key,entry);
                }
                entry.Parts.Clear();pair.Value.AppendLightOccluders(entry.Parts);
                weapons.AppendWeaponLightOccluders(pair.Key,entry.Parts);
                if(entry.Parts.Count==0) {entry.View.Clear();continue;}
                entry.Position=pair.Value.TryGetDrawnBounds(out var bounds)?(Vector2)bounds.center:
                    PartsCenter(entry.Parts);
                if(entry.Revision!=level.ShadowSourceRevision ||
                    (entry.Position-entry.SelectedAt).sqrMagnitude>=settings.ActorShadowMoveThreshold*settings.ActorShadowMoveThreshold)
                    entry.Queued=true;
                BlendLampSources(entry.DisplayLamps,entry.Lamps,Time.deltaTime,settings.ActorShadowMaxSources);
                entry.Sources.Clear();entry.Sources.AddRange(entry.DisplayLamps);entry.Flashes.Clear();
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
                entry.View.Draw(entry.Position,entry.Parts,entry.Sources,settings);
            }
            foreach(var pair in entries) if(!retained.Contains(pair.Key)) removed.Add(pair.Key);
            foreach(var id in removed) {Destroy(entries[id]);entries.Remove(id);}
            if(refresh==null) foreach(var pair in entries) if(pair.Value.Queued)
                {refresh=runner.StartCoroutine(RebuildSources());break;}
        }

        // Shot event preparation keeps CPU visibility and pool allocation out of LateUpdate,
        // while making the first visible flash use a ready cache and target slot immediately.
        public void PrepareFlash(FactoryLevelView current,IReadOnlyDictionary<string,ActorVisual> actors,
            InteractionVisuals weapons,string id,RoomRadianceField.SpriteEmission flash)
        {
            if(current!=level) {Clear();level=current;}
            if(level==null || !level.IsReady || flash.Sprite==null) return;
            if(string.IsNullOrEmpty(id)) throw new ArgumentException("Нет стабильного ID вспышки.",nameof(id));
            Vector2 sourcePosition=flash.LocalToWorld.MultiplyPoint3x4(flash.Sprite.bounds.center);
            foreach(var actor in actors)
            {
                if(actor.Value==null || !actor.Value.gameObject.activeInHierarchy) continue;
                if(!entries.TryGetValue(actor.Key,out var entry))
                {
                    entry=new Entry{View=UnityEngine.Object.Instantiate(prefab,parent,false)};
                    entry.View.name="Проецируемая тень: "+actor.Key;entries.Add(actor.Key,entry);
                }
                entry.Parts.Clear();actor.Value.AppendLightOccluders(entry.Parts);
                weapons.AppendWeaponLightOccluders(actor.Key,entry.Parts);
                if(entry.Parts.Count==0) continue;
                entry.Position=actor.Value.TryGetDrawnBounds(out var bounds)?(Vector2)bounds.center:PartsCenter(entry.Parts);
                if(entry.Revision!=level.ShadowSourceRevision)
                {
                    level.SelectActorShadowSources(entry.Position,entry.Lamps);
                    entry.SelectedAt=entry.Position;entry.Revision=level.ShadowSourceRevision;
                }
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
                jobs.Clear();
                using(Remains.Diagnostics.FrameHitchLog.Section("Тени: запуск выбора ламп"))
                foreach(var pair in entries)
                {
                    var entry=pair.Value;
                    if(entry.Queued && (entry.Revision!=owner.ShadowSourceRevision ||
                        (entry.Position-entry.SelectedAt).sqrMagnitude>=owner.ShadowSettings.ActorShadowMoveThreshold*owner.ShadowSettings.ActorShadowMoveThreshold))
                        jobs.Add((entry,entry.Position,owner.ShadowSourceRevision,owner.SelectActorShadowSourcesAsync(entry.Position)));
                }
                foreach(var job in jobs) while(!job.Task.IsCompleted) yield return null;
                if(level!=owner || !owner.IsReady) {refresh=null;yield break;}
                foreach(var job in jobs)
                {
                    var selected=job.Task.GetAwaiter().GetResult();   // a failed selection throws here, as the synchronous call did
                    if(job.Revision!=owner.ShadowSourceRevision || !entries.ContainsValue(job.Entry)) continue;   // stale: reselected next frame
                    job.Entry.Lamps.Clear();job.Entry.Lamps.AddRange(selected);
                    job.Entry.SelectedAt=job.At;job.Entry.Revision=job.Revision;
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
                foreach(var source in entry.Lamps) steadySources.Add(source.Id);
                foreach(var source in entry.DisplayLamps) steadySources.Add(source.Id);
            }
            level.PrepareActorShadowFields(steadySources);
        }
        private static Vector2 PartsCenter(List<RoomRadianceField.LightOccluder> parts)
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
