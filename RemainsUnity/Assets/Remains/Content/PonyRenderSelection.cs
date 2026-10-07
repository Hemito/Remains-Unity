using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Remains.Domain;
using UnityEngine;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace Remains.Content
{
    // Prepared graphics only. No source parsing, filtering or bounds analysis in Update.
    public static class PonyRenderSelection
    {
        private sealed class Clip
        {
            public PonyRenderPart[][] frames;
            public Bounds[] bounds;
        }
        private static readonly Dictionary<(int,int,bool,string),Dictionary<string,Clip>> selections=new();
        private static Bounds cameraEnvelope;
        private static PonyRenderAsset baseline;
        private static PonyHeadRenderResolver headResolver;
        private sealed class Binding
        {public PonyAppearanceKey appearance;public CosmeticEquipmentState equipment;public long revision;public WearableDrawPlan plan;}
        private static ConditionalWeakTable<ActorState,Binding> bindings=new();
        private static readonly Dictionary<(int,int,bool,string,string),WeakReference<WearableDrawPlan>> plans=new();
        private static readonly Queue<(int,int,bool,string,string)> planOrder=new();
        private static readonly HashSet<string> warnings=new(StringComparer.Ordinal);
        public static int Capacity {get;private set;}
        public static double PrepareElapsedMilliseconds {get;private set;}
        public static double LastShapePrepareMilliseconds {get;private set;}
        public static int PreparedSelectionCount=>selections.Count;
        public static void Prepare()
        {
            PonyAppearance.Prepare();
            if(selections.Count!=0) return;
            var timer=Stopwatch.StartNew();
            var asset=Resources.Load<PonyRenderAsset>("Original/Pony/render");
            if(asset==null) throw new InvalidOperationException("Нет сохранённой графики пони Original/Pony/render.");
            asset.Validate();PonyAnimationData.Load();
            var resolver=new PonyHeadRenderResolver(asset);
            CosmeticCatalog.Load();WearableRenderCatalog.Prepare();WearableRenderCatalog.Freeze();
            cameraEnvelope=asset.Clips[0].frames[0].bounds;Capacity=0;
            foreach(var clip in asset.Clips) foreach(var frame in clip.frames) cameraEnvelope.Encapsulate(frame.bounds);
            foreach(var style in asset.HeadStyles) foreach(var clip in style.clips) foreach(var frame in clip.frames) cameraEnvelope.Encapsulate(frame.bounds);
            float extent=Mathf.Max(Mathf.Abs(cameraEnvelope.min.x),Mathf.Abs(cameraEnvelope.max.x));
            cameraEnvelope.SetMinMax(new Vector3(-extent,cameraEnvelope.min.y,cameraEnvelope.min.z),new Vector3(extent,cameraEnvelope.max.y,cameraEnvelope.max.z));
            // Publish only after the complete cache passes validation.
            var prepared=new Dictionary<(int,int,bool,string),Dictionary<string,Clip>>();
            for(int hair=1;hair<=PonyAnimationData.Library.hairStyles;hair++) for(int eyes=1;eyes<=PonyAnimationData.Library.eyeStyles;eyes++) for(int secondary=0;secondary<2;secondary++)
            {
                var clips=new Dictionary<string,Clip>();
                foreach(var source in asset.Clips)
                {
                    var clip=new Clip {frames=new PonyRenderPart[source.frames.Length][],bounds=new Bounds[source.frames.Length]};
                    for(int f=0;f<source.frames.Length;f++)
                    {
                        var parts=source.frames[f].parts.Where(part=>part.visible && PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,hair,eyes,secondary==1)).OrderBy(part=>part.drawOrder).ToArray();
                        if(parts.Length==0) throw new InvalidOperationException($"Пустой кадр внешности: head={CharacterAppearance.OriginalHeadStyleId}, hair={hair}, eyes={eyes}, secondary={secondary}, clip={source.id}, frame={f}.");
                        for(int p=1;p<parts.Length;p++) if(parts[p-1].drawOrder>=parts[p].drawOrder)
                            throw new InvalidOperationException($"Перемешаны слои внешности: head={CharacterAppearance.OriginalHeadStyleId}, hair={hair}, eyes={eyes}, clip={source.id}, frame={f}, order={parts[p].drawOrder}.");
                        var bounds=parts[0].mesh.bounds;foreach(var part in parts) bounds.Encapsulate(part.mesh.bounds);
                        clip.frames[f]=parts;clip.bounds[f]=bounds;Capacity=Math.Max(Capacity,parts.Length);
                    }
                    clips.Add(source.id,clip);
                }
                prepared.Add((hair,eyes,secondary==1,CharacterAppearance.OriginalHeadStyleId),clips);
            }
            foreach(var pair in prepared) selections.Add(pair.Key,pair.Value);
            baseline=asset;headResolver=resolver;Capacity=checked(Capacity+WearableRenderCatalog.MaximumAdditionalParts);
            timer.Stop();PrepareElapsedMilliseconds=timer.Elapsed.TotalMilliseconds;
            Debug.Log($"Графика пони подготовлена: исходных сочетаний={PreparedSelectionCount}, форм головы={PonyHeadCatalog.Styles.Count}, кадров={asset.Clips.Sum(clip=>clip.frames.Length)}, вместимость={Capacity}, время={PrepareElapsedMilliseconds:F1} мс. Дополнительные сочетания головы готовятся при выборе внешности.");
        }
        public static bool HasClip(string clip)=>selections.Count>0 && selections[(1,1,false,CharacterAppearance.OriginalHeadStyleId)].ContainsKey(clip);
        public static PonyRenderPart[][] Frames(PonyAppearanceKey key,string clip)=>Get(key,clip).frames;
        private static Clip Get(PonyAppearanceKey key,string clip)
        {
            if(!selections.TryGetValue((key.HairStyle,key.EyeStyle,key.SecondaryEnabled,key.HeadStyleId),out var clips) || !clips.TryGetValue(clip,out var result))
                throw new InvalidOperationException($"Нет подготовленной внешности: head={key.HeadStyleId}, hair={key.HairStyle}, eyes={key.EyeStyle}, secondary={key.SecondaryEnabled}, clip={clip}. Вызовите Bind/PrepareEquipment при событии смены формы.");
            return result;
        }
        // Explicit appearance events only. Keep Frames/Get read-only so rendering
        // cannot accidentally allocate a complete animation bank in LateUpdate.
        private static void PrepareShape(PonyAppearanceKey appearance)
        {
            var key=(appearance.HairStyle,appearance.EyeStyle,appearance.SecondaryEnabled,appearance.HeadStyleId);
            if(selections.ContainsKey(key)) return;
            if(!selections.TryGetValue((appearance.HairStyle,appearance.EyeStyle,appearance.SecondaryEnabled,CharacterAppearance.OriginalHeadStyleId),out var original))
                throw new InvalidOperationException($"Нет исходного сочетания внешности: head={appearance.HeadStyleId}, hair={appearance.HairStyle}, eyes={appearance.EyeStyle}, secondary={appearance.SecondaryEnabled}.");
            var timer=Stopwatch.StartNew();var prepared=new Dictionary<string,Clip>(StringComparer.Ordinal);
            foreach(var entry in original)
            {
                var source=entry.Value;var clip=new Clip {frames=new PonyRenderPart[source.frames.Length][],bounds=new Bounds[source.frames.Length]};
                for(int f=0;f<source.frames.Length;f++)
                {
                    var parts=(PonyRenderPart[])source.frames[f].Clone();int replaced=0;
                    for(int p=0;p<parts.Length;p++) if(parts[p].sourceShape==PonyHeadRenderResolver.SourceShape)
                    {parts[p]=headResolver.Resolve(appearance.HeadStyleId,entry.Key,f,parts[p]);replaced++;}
                    if(replaced!=1) throw new InvalidOperationException($"Нет единственной головы в исходном сочетании: head={appearance.HeadStyleId}, clip={entry.Key}, frame={f}, matches={replaced}.");
                    var bounds=parts[0].mesh.bounds;foreach(var part in parts) bounds.Encapsulate(part.mesh.bounds);
                    clip.frames[f]=parts;clip.bounds[f]=bounds;
                }
                prepared.Add(entry.Key,clip);
            }
            selections.Add(key,prepared);timer.Stop();LastShapePrepareMilliseconds=timer.Elapsed.TotalMilliseconds;
        }
        public static Bounds CameraBounds(ActorState actor)
        {
            if(selections.Count==0) throw new InvalidOperationException("Графика пони не подготовлена перед расчётом камеры.");
            var body=actor.BodyPosition;
            var bounds=Prepared(actor)?.CameraEnvelope??cameraEnvelope;bounds.center+=new Vector3(body.x,body.y,0);return bounds;
        }
        public static Bounds WorldBounds(ActorState actor)
        {
            string pose=ActorPose.Clip(actor);int frame=PonyMotionFrames.Sample(actor,actor.poseTime);
            var plan=Prepared(actor);
            var bounds=plan?.HasEquipment==true?plan.FrameBounds(pose,frame):Get(PonyAppearance.Snapshot(actor),pose).bounds[frame];
            var body=actor.BodyPosition;
            bounds.center=new Vector3(body.x+bounds.center.x*actor.facing,body.y+bounds.center.y,bounds.center.z);return bounds;
        }
        // Called by Bind / UI state refresh only. Identical snapshots share prepared
        // frames; the small weak cache is an optimisation, not a limit on mod slots.
        public static WearableDrawPlan PrepareEquipment(ActorState actor)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            if(baseline==null) throw new InvalidOperationException("Базовая графика пони не подготовлена перед снаряжением.");
            var appearance=PonyAppearance.Snapshot(actor);var equipment=actor.cosmeticEquipment;
            PrepareShape(appearance);
            var binding=bindings.GetValue(actor,_=>new Binding());
            if(binding.plan!=null && binding.appearance.Equals(appearance) && ReferenceEquals(binding.equipment,equipment) && binding.revision==(equipment?.revision??0)) return binding.plan;
            CosmeticEquipment.Validate(actor);
            var selected=new List<(string slot,WearableRenderAsset asset)>();
            if(equipment!=null && equipment.version!=1)
                Warn(actor,"Не поддержана версия визуальной экипировки "+equipment.version,null,null);
            if(equipment?.version==1) foreach(var entry in equipment.entries)
            {
                if(entry.item==null) continue;
                if(!CosmeticEquipment.TryResolve(actor,entry,out var definition,out var reason))
                {if(reason!=null) Warn(actor,reason,entry.definition,entry.item);continue;}
                if(!WearableRenderCatalog.TryGet(definition.Visual,out var asset))
                {Warn(actor,"Нет рисунка выбранного снаряжения",definition.Visual,entry.item);continue;}
                if(asset.Anatomy!=actor.anatomy) throw new InvalidOperationException($"Рисунок не подходит анатомии: actor={actor.id}, anatomy={actor.anatomy}, visual={definition.Visual}, profileAnatomy={asset.Anatomy}.");
                selected.Add((entry.slot,asset));
            }
            selected.Sort((a,b)=>StringComparer.Ordinal.Compare(a.slot,b.slot));
            string gear=string.Concat(selected.Select(pair=>pair.slot.Length+":"+pair.slot+pair.asset.VisualId.Length+":"+pair.asset.VisualId));
            var cacheKey=(appearance.HairStyle,appearance.EyeStyle,appearance.SecondaryEnabled,appearance.HeadStyleId,gear);
            if(!plans.TryGetValue(cacheKey,out var reference) || !reference.TryGetTarget(out var plan))
            {
                plan=WearableDrawPlan.Create(baseline,appearance,selected.Select(pair=>pair.asset).ToArray(),cameraEnvelope,headResolver);
                if(!plans.ContainsKey(cacheKey)) planOrder.Enqueue(cacheKey);
                plans[cacheKey]=new WeakReference<WearableDrawPlan>(plan);
                while(plans.Count>128) plans.Remove(planOrder.Dequeue());
            }
            binding.appearance=appearance;binding.equipment=equipment;binding.revision=equipment?.revision??0;binding.plan=plan;return plan;
        }
        public static bool TryGetPrepared(ActorState actor,out WearableDrawPlan plan)
        {plan=null;if(actor==null || !bindings.TryGetValue(actor,out var binding)) return false;plan=binding.plan;return plan!=null;}
        private static WearableDrawPlan Prepared(ActorState actor)
        {
            if(TryGetPrepared(actor,out var plan)) return plan;
            if(actor.cosmeticEquipment is {version:1} equipment && equipment.entries.Exists(entry=>entry.item!=null))
                Warn(actor,"Границы снаряжения запрошены до Bind/подготовки",null,null);
            return null;
        }
        private static void Warn(ActorState actor,string reason,string visual,string item)
        {
            string key=actor.id+"/"+reason+"/"+visual+"/"+item;
            if(warnings.Add(key)) Debug.LogWarning($"{reason}: actor={actor.id}, anatomy={actor.anatomy}, visual={visual??"<не указан>"}, item={item??"<не указан>"}. Данные сохранены; неподключённый слой пропущен.");
        }
        public static void ResetPrepared()
        {selections.Clear();plans.Clear();planOrder.Clear();bindings=new();baseline=null;headResolver=null;Capacity=0;PrepareElapsedMilliseconds=0;LastShapePrepareMilliseconds=0;}
    }
}
