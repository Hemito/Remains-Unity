using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    public sealed class WearableDrawPlan
    {
        private readonly Dictionary<string,PonyRenderPart[][]> frames;
        private readonly Dictionary<string,Bounds[]> bounds;
        private readonly int hairStyle,eyeStyle;
        private readonly bool secondaryEnabled;
        public Bounds CameraEnvelope {get;}
        public int Capacity {get;}
        public bool HasEquipment {get;}
        public IReadOnlyList<string> VisualIds {get;}
        public PonyRenderPart[] PortraitFrame=>Frames("idle")[0];
        private WearableDrawPlan(Dictionary<string,PonyRenderPart[][]> values,Dictionary<string,Bounds[]> frameBounds,Bounds envelope,int capacity,string[] ids,PonyAppearanceKey appearance)
        {frames=values;bounds=frameBounds;CameraEnvelope=envelope;Capacity=capacity;HasEquipment=ids.Length!=0;VisualIds=Array.AsReadOnly(ids);
            hairStyle=appearance.HairStyle;eyeStyle=appearance.EyeStyle;secondaryEnabled=appearance.SecondaryEnabled;}
        public bool MatchesShape(PonyAppearanceKey appearance)=>hairStyle==appearance.HairStyle && eyeStyle==appearance.EyeStyle && secondaryEnabled==appearance.SecondaryEnabled;
        public PonyRenderPart[][] Frames(string clip)=>frames.TryGetValue(clip,out var value)?value:
            throw new InvalidOperationException("Нет подготовленного кадра визуального снаряжения: clip="+clip+".");
        public Bounds FrameBounds(string clip,int frame)=>bounds[clip][frame];
        public static WearableDrawPlan Create(PonyRenderAsset baseline,PonyAppearanceKey appearance,IReadOnlyList<WearableRenderAsset> equipment,Bounds bareCameraEnvelope)
        {
            if(baseline==null || equipment==null) throw new ArgumentNullException("Для плана нужны базовая графика и выбранные рисунки.");
            var values=new Dictionary<string,PonyRenderPart[][]>(StringComparer.Ordinal);var allBounds=new Dictionary<string,Bounds[]>(StringComparer.Ordinal);
            bool hasEnvelope=false;Bounds envelope=default;int capacity=0;
            foreach(var clip in baseline.Clips)
            {
                var result=new PonyRenderPart[clip.frames.Length][];var frameBounds=new Bounds[clip.frames.Length];
                var equippedClips=new WearableRenderClip[equipment.Count];
                for(int e=0;e<equipment.Count;e++) equippedClips[e]=Array.Find(equipment[e].Clips,item=>item.id==clip.id)??
                    throw new InvalidOperationException($"Нет позы снаряжения: visual={equipment[e].VisualId}, clip={clip.id}.");
                for(int f=0;f<result.Length;f++)
                {
                    var hidden=new HashSet<int>();WearableRenderOrder[] baseOrder=null;
                    foreach(var equipped in equippedClips)
                    {
                        var frame=equipped.frames[f];
                        foreach(int index in frame.hiddenBasePartIndices) hidden.Add(index);
                        if(baseOrder==null) baseOrder=frame.baseOrder;
                        else for(int p=0;p<baseOrder.Length;p++) if(ComparePath(baseOrder[p].path,frame.baseOrder[p].path)!=0)
                            throw new InvalidOperationException($"Несовместимый порядок базовых частей: clip={clip.id}, frame={f}, part={p}.");
                    }
                    var list=new List<(PonyRenderPart part,int[] order)>();
                    foreach(var part in clip.frames[f].parts)
                        if(part.visible && !hidden.Contains(part.sourceIndex) && PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,appearance.HairStyle,appearance.EyeStyle,appearance.SecondaryEnabled))
                            list.Add((part,baseOrder==null?new[]{part.drawOrder}:baseOrder[part.sourceIndex].path));
                    foreach(var equipped in equippedClips) foreach(var part in equipped.frames[f].parts)
                        if(part.visible && PonyAppearance.Matches(part.hairStyle,part.eyeStyle,part.slot,appearance.HairStyle,appearance.EyeStyle,appearance.SecondaryEnabled)) list.Add((part,part.orderPath));
                    list.Sort((a,b)=>ComparePath(a.order,b.order));
                    if(list.Count==0) throw new InvalidOperationException($"Пустой составной рисунок: clip={clip.id}, frame={f}.");
                    var parts=new PonyRenderPart[list.Count];var bounds=list[0].part.mesh.bounds;
                    for(int p=0;p<parts.Length;p++)
                    {
                        if(p>0 && ComparePath(list[p-1].order,list[p].order)==0)
                            throw new InvalidOperationException($"Неоднозначные слои снаряжения: clip={clip.id}, frame={f}, paths={list[p-1].part.sourcePath}/{list[p].part.sourcePath}.");
                        parts[p]=list[p].part;bounds.Encapsulate(parts[p].mesh.bounds);
                    }
                    result[f]=parts;frameBounds[f]=bounds;capacity=Math.Max(capacity,parts.Length);
                    if(!hasEnvelope) {envelope=bounds;hasEnvelope=true;}else envelope.Encapsulate(bounds);
                }
                values.Add(clip.id,result);allBounds.Add(clip.id,frameBounds);
            }
            if(equipment.Count==0) envelope=bareCameraEnvelope;
            else
            {
                float x=Mathf.Max(Mathf.Abs(envelope.min.x),Mathf.Abs(envelope.max.x));
                envelope.SetMinMax(new Vector3(-x,envelope.min.y,envelope.min.z),new Vector3(x,envelope.max.y,envelope.max.z));
            }
            var ids=new string[equipment.Count];for(int i=0;i<ids.Length;i++) ids[i]=equipment[i].VisualId;
            return new WearableDrawPlan(values,allBounds,envelope,capacity,ids,appearance);
        }
        public static int ComparePath(int[] a,int[] b)
        {
            int count=Math.Min(a.Length,b.Length);
            for(int i=0;i<count;i++) {int result=a[i].CompareTo(b[i]);if(result!=0) return result;}
            return a.Length.CompareTo(b.Length);
        }
    }
}
