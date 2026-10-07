using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class WearableRenderOrder {public int[] path;}
    [Serializable] public sealed class WearableRenderFrame
    {public PonyRenderPart[] parts;public int[] hiddenBasePartIndices;public WearableRenderOrder[] baseOrder;public Bounds bounds;}
    [Serializable] public sealed class WearableRenderClip
    {public string id;public WearableRenderFrame[] frames;}
    public sealed class WearableRenderAsset : ScriptableObject
    {
        public const int Schema=1;
        [SerializeField,HideInInspector] private int schema;
        [SerializeField,HideInInspector] private string visualId,anatomy,sourceHash,baseSourceHash,contentFingerprint;
        [SerializeField,HideInInspector] private WearableRenderClip[] clips;
        public string VisualId=>visualId;public string Anatomy=>anatomy;public string SourceHash=>sourceHash;
        public string BaseSourceHash=>baseSourceHash;public string ContentFingerprint=>contentFingerprint;
        public WearableRenderClip[] Clips=>clips;
        public void Configure(string id,string actorAnatomy,string source,string baseSource,string fingerprint,WearableRenderClip[] values)
        {schema=Schema;visualId=id;anatomy=actorAnatomy;sourceHash=source;baseSourceHash=baseSource;contentFingerprint=fingerprint;clips=values;}
        public void Validate(PonyLibrary library,string expectedBaseHash,string context)
        {
            if(schema!=Schema || string.IsNullOrWhiteSpace(visualId) || string.IsNullOrWhiteSpace(anatomy) || string.IsNullOrWhiteSpace(sourceHash) ||
                string.IsNullOrWhiteSpace(contentFingerprint) || baseSourceHash!=expectedBaseHash || library?.clips==null || clips==null || clips.Length!=library.clips.Length)
                throw new InvalidOperationException($"Графика снаряжения не подготовлена: visual={visualId}, source={context}, schema={schema}, baseHash={baseSourceHash}, expected={expectedBaseHash}.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var clip in clips)
            {
                var original=Array.Find(library.clips,value=>value.id==clip?.id);
                if(clip==null || original==null || !seen.Add(clip.id) || clip.frames==null || clip.frames.Length!=original.frames.Length)
                    throw new InvalidOperationException($"Неполные кадры снаряжения: visual={visualId}, clip={clip?.id}, source={context}.");
                for(int f=0;f<clip.frames.Length;f++)
                {
                    var frame=clip.frames[f];var source=original.frames[f];
                    if(frame?.parts==null || frame.hiddenBasePartIndices==null || frame.baseOrder==null || frame.baseOrder.Length!=source.parts.Length)
                        throw new InvalidOperationException($"Нет привязок снаряжения: visual={visualId}, clip={clip.id}, frame={f}, source={context}.");
                    var hidden=new HashSet<int>();
                    foreach(int index in frame.hiddenBasePartIndices)
                        if(index<0 || index>=source.parts.Length || !hidden.Add(index))
                            throw new InvalidOperationException($"Неизвестная/повторная скрываемая часть: visual={visualId}, clip={clip.id}, frame={f}, part={index}.");
                    foreach(var order in frame.baseOrder) ValidatePath(order?.path,visualId+"/"+clip.id+"/"+f);
                    bool hasBounds=false;Bounds expectedBounds=default;
                    foreach(var part in frame.parts)
                    {
                        if(part==null || part.mesh==null || part.material==null || part.material.mainTexture==null || part.points==null || part.points.Length!=4 ||
                            string.IsNullOrWhiteSpace(part.sourcePath) || part.hairStyle<0 || part.hairStyle>library.hairStyles || part.eyeStyle<0 || part.eyeStyle>library.eyeStyles ||
                            part.slot is not ("none" or "fur" or "hair" or "hairSecondary" or "eye" or "magic"))
                            throw new InvalidOperationException($"Повреждена часть снаряжения: visual={visualId}, clip={clip.id}, frame={f}, path={part?.sourcePath}.");
                        ValidatePath(part.orderPath,visualId+"/"+clip.id+"/"+f+"/"+part.sourcePath);
                        PonyRenderAsset.ValidatePart(part,visualId+"/"+clip.id+"/"+f);
                        if(!hasBounds) {expectedBounds=part.mesh.bounds;hasBounds=true;}else expectedBounds.Encapsulate(part.mesh.bounds);
                    }
                    if(!hasBounds) throw new InvalidOperationException($"Пустой кадр снаряжения: visual={visualId}, clip={clip.id}, frame={f}.");
                    PonyRenderAsset.ValidateBounds(frame.bounds,expectedBounds,visualId+"/"+clip.id+"/"+f);
                }
            }
        }
        public static void ValidatePath(int[] path,string context)
        {
            if(path==null || path.Length==0 || Array.Exists(path,value=>value<0))
                throw new InvalidOperationException("Не объявлен порядок графической части: "+context+".");
        }
    }
}
