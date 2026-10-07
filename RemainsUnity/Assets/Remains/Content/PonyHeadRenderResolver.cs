using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Content
{
    // One prepared replacement rule shared by bare actors and equipped portraits.
    // Decorative geometry never changes anatomy, movement or foot-support data.
    public sealed class PonyHeadRenderResolver
    {
        public const string SourceShape="196";
        private readonly Dictionary<string,Dictionary<string,PonyRenderPart[]>> styles=new(StringComparer.Ordinal);
        public PonyHeadRenderResolver(PonyRenderAsset asset)
        {
            if(asset?.Clips==null || asset.HeadStyles==null)
                throw new InvalidOperationException("Нет подготовленного банка форм головы.");
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
    }
}
