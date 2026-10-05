using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

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
        private static readonly Dictionary<(int,int,bool),Dictionary<string,Clip>> selections=new();
        private static Bounds cameraEnvelope;
        public static int Capacity {get;private set;}
        public static void Prepare()
        {
            PonyAppearance.Prepare();
            if(selections.Count!=0) return;
            var asset=Resources.Load<PonyRenderAsset>("Original/Pony/render");
            if(asset==null) throw new InvalidOperationException("Нет сохранённой графики пони Original/Pony/render.");
            asset.Validate();PonyAnimationData.Load();
            cameraEnvelope=asset.Clips[0].frames[0].bounds;Capacity=0;
            foreach(var clip in asset.Clips) foreach(var frame in clip.frames) cameraEnvelope.Encapsulate(frame.bounds);
            float extent=Mathf.Max(Mathf.Abs(cameraEnvelope.min.x),Mathf.Abs(cameraEnvelope.max.x));
            cameraEnvelope.SetMinMax(new Vector3(-extent,cameraEnvelope.min.y,cameraEnvelope.min.z),new Vector3(extent,cameraEnvelope.max.y,cameraEnvelope.max.z));
            // Publish only after the complete cache passes validation.
            var prepared=new Dictionary<(int,int,bool),Dictionary<string,Clip>>();
            for(int hair=1;hair<=PonyAnimationData.Library.hairStyles;hair++) for(int eyes=1;eyes<=PonyAnimationData.Library.eyeStyles;eyes++) for(int secondary=0;secondary<2;secondary++)
            {
                var clips=new Dictionary<string,Clip>();
                foreach(var source in asset.Clips)
                {
                    var clip=new Clip {frames=new PonyRenderPart[source.frames.Length][],bounds=new Bounds[source.frames.Length]};
                    for(int f=0;f<source.frames.Length;f++)
                    {
                        var parts=source.frames[f].parts.Where(part=>part.visible && PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,hair,eyes,secondary==1)).OrderBy(part=>part.drawOrder).ToArray();
                        if(parts.Length==0) throw new InvalidOperationException($"Пустой кадр внешности: hair={hair}, eyes={eyes}, secondary={secondary}, clip={source.id}, frame={f}.");
                        for(int p=1;p<parts.Length;p++) if(parts[p-1].drawOrder>=parts[p].drawOrder)
                            throw new InvalidOperationException($"Перемешаны слои внешности: hair={hair}, eyes={eyes}, clip={source.id}, frame={f}, order={parts[p].drawOrder}.");
                        var bounds=parts[0].mesh.bounds;foreach(var part in parts) bounds.Encapsulate(part.mesh.bounds);
                        clip.frames[f]=parts;clip.bounds[f]=bounds;Capacity=Math.Max(Capacity,parts.Length);
                    }
                    clips.Add(source.id,clip);
                }
                prepared.Add((hair,eyes,secondary==1),clips);
            }
            foreach(var pair in prepared) selections.Add(pair.Key,pair.Value);
        }
        public static bool HasClip(string clip)=>selections.Count>0 && selections[(1,1,false)].ContainsKey(clip);
        public static PonyRenderPart[][] Frames(PonyAppearanceKey key,string clip)=>Get(key,clip).frames;
        private static Clip Get(PonyAppearanceKey key,string clip)
        {
            if(!selections.TryGetValue((key.HairStyle,key.EyeStyle,key.SecondaryEnabled),out var clips) || !clips.TryGetValue(clip,out var result))
                throw new InvalidOperationException($"Нет подготовленной внешности: hair={key.HairStyle}, eyes={key.EyeStyle}, secondary={key.SecondaryEnabled}, clip={clip}.");
            return result;
        }
        public static Bounds CameraBounds(ActorState actor)
        {
            if(selections.Count==0) throw new InvalidOperationException("Графика пони не подготовлена перед расчётом камеры.");
            var bounds=cameraEnvelope;bounds.center+=new Vector3(actor.x,actor.y,0);return bounds;
        }
        public static Bounds WorldBounds(ActorState actor)
        {
            var clip=Get(PonyAppearance.Snapshot(actor),ActorPose.Clip(actor));
            var bounds=clip.bounds[PonyMotionFrames.Sample(actor,actor.poseTime)];
            bounds.center=new Vector3(actor.x+bounds.center.x*actor.facing,actor.y+bounds.center.y,bounds.center.z);return bounds;
        }
    }
}
