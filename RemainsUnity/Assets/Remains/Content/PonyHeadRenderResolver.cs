using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // One prepared replacement rule shared by bare actors and equipped portraits.
    // Decorative geometry never changes anatomy, movement or foot-support data.
    public sealed class PonyHeadRenderResolver
    {
        public const string SourceShape="196";
        private readonly Dictionary<string,Dictionary<string,PonyRenderPart[]>> styles=new(StringComparer.Ordinal);
        private readonly Dictionary<string,PonyRenderPart[]> originals=new(StringComparer.Ordinal);
        public PonyHeadRenderResolver(PonyRenderAsset asset)
        {
            if(asset?.Clips==null || asset.HeadStyles==null)
                throw new InvalidOperationException("Нет подготовленного банка форм головы.");
            foreach(var clip in asset.Clips)
            {
                var parts=new PonyRenderPart[clip.frames.Length];
                for(int f=0;f<parts.Length;f++)parts[f]=Array.Find(clip.frames[f].parts,p=>p.sourceShape==SourceShape)??throw new InvalidOperationException("Нет базовой головы: "+clip.id);
                originals.Add(clip.id,parts);
            }
            foreach(var style in asset.HeadStyles)
            {
                if(style==null || string.IsNullOrWhiteSpace(style.id) || style.id==CharacterAppearance.OriginalHeadStyleId || style.clips==null || styles.ContainsKey(style.id))
                    throw new InvalidOperationException("Неверная форма подготовленной головы: "+style?.id+".");
                var clips=new Dictionary<string,PonyRenderPart[]>(StringComparer.Ordinal);
                foreach(var clip in style.clips)
                {
                    if(clip==null || string.IsNullOrWhiteSpace(clip.id) || clip.frames==null || clips.ContainsKey(clip.id))
                        throw new InvalidOperationException($"Неверная поза подготовленной головы: head={style.id}, clip={clip?.id}.");
                    var parts=new PonyRenderPart[clip.frames.Length];
                    for(int f=0;f<parts.Length;f++)
                    {
                        if(clip.frames[f]?.parts?.Length!=1)
                            throw new InvalidOperationException($"Нет единственной замены головы: head={style.id}, clip={clip.id}, frame={f}.");
                        parts[f]=clip.frames[f].parts[0];
                    }
                    clips.Add(clip.id,parts);
                }
                styles.Add(style.id,clips);
            }
        }
        public PonyRenderPart Resolve(string headStyleId,string clip,int frame,PonyRenderPart baselinePart)
        {
            if(baselinePart==null) throw new ArgumentNullException(nameof(baselinePart));
            if(baselinePart.sourceShape!=SourceShape || headStyleId==CharacterAppearance.OriginalHeadStyleId) return baselinePart;
            if(headStyleId==null || !styles.TryGetValue(headStyleId,out var clips) || !clips.TryGetValue(clip,out var frames) || frame<0 || frame>=frames.Length)
                throw new InvalidOperationException($"Нет подготовленной замены головы: head={headStyleId??"<не указана>"}, clip={clip}, frame={frame}, sourceIndex={baselinePart.sourceIndex}.");
            var replacement=frames[frame];
            if(replacement==null || replacement.sourceShape!=SourceShape || replacement.sourceIndex!=baselinePart.sourceIndex)
                throw new InvalidOperationException($"Замена головы не соответствует исходной части: head={headStyleId}, clip={clip}, frame={frame}, sourceIndex={baselinePart.sourceIndex}.");
            return replacement;
        }
        public PonyRenderPart ResolveWearable(string headStyleId,string clip,int frame,PonyRenderPart part)
        {
            if(part.sourceShape!=SourceShape || headStyleId==CharacterAppearance.OriginalHeadStyleId)return part;
            var source=originals[clip][frame];var replacement=Resolve(headStyleId,clip,frame,source);
            var originalPoints=source.mesh.vertices;var wearablePoints=part.mesh.vertices;
            for(int p=0;p<4;p++)if(Vector3.Distance(originalPoints[p],wearablePoints[p])>.00001f)
                throw new InvalidOperationException($"Голова комплекта требует явной регистрации формы: head={headStyleId}, clip={clip}, path={part.sourcePath}, point={p}.");
            if(part.baseColor!=replacement.baseColor)throw new InvalidOperationException("Голова комплекта требует адаптера цвета: "+part.sourcePath);
            var vertices=replacement.mesh.vertices;var points=new Vector3[4];
            for(int p=0;p<4;p++)points[p]=part.symbolPose.Matrix.MultiplyPoint3x4(vertices[p]);
            return new PonyRenderPart {mesh=replacement.mesh,material=replacement.material,slot=part.slot,visible=part.visible,
                hairStyle=part.hairStyle,eyeStyle=part.eyeStyle,drawOrder=part.drawOrder,sourceIndex=part.sourceIndex,
                sourcePath=part.sourcePath,sourceShape=part.sourceShape,points=points,baseColor=part.baseColor,orderPath=part.orderPath,
                symbolId=part.symbolId,symbolPose=part.symbolPose};
        }
    }
}
