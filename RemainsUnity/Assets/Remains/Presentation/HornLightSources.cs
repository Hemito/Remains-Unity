using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    // Explicit initial visual tuning: small magic-coloured direct light, independent of the AI's carried-light rule.
    // The completed PonyVisual supplies both exact horn pose and its confirmed activity; no world/item scans here.
    public sealed class HornLightSources
    {
        public const float Reach=1.5f,EmitterWidth=.1f,RadianceScale=2;
        private sealed class Entry {public string Id;public readonly Vector4[] Points=new Vector4[1];}
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly HashSet<string> retained=new(StringComparer.Ordinal);
        private readonly List<string> gone=new();
        public void Append(IReadOnlyDictionary<string,ActorVisual> visuals,List<WorldLightEmission> output,Rect? visibleArea=null)
        {
            if(visuals==null || output==null)throw new ArgumentNullException(visuals==null?nameof(visuals):nameof(output));
            retained.Clear();
            foreach(var pair in visuals)
            {
                if(pair.Value is not PonyVisual pony || !pony.TryGetHornLightPose(out var position,out var color,out float opacity))continue;
                var affected=new Rect(position-Vector2.one*Reach,Vector2.one*(Reach*2));
                if(visibleArea.HasValue && !visibleArea.Value.Overlaps(affected))continue;
                if(string.IsNullOrEmpty(pony.ActorId) || pair.Key!=pony.ActorId)
                    throw new InvalidOperationException($"Свет рога: ID представления не совпал с ключом: actor={pony.ActorId}, key={pair.Key}.");
                retained.Add(pair.Key);
                if(!entries.TryGetValue(pair.Key,out var entry))
                {entry=new Entry{Id="horn:"+pair.Key};entries.Add(pair.Key,entry);}
                entry.Points[0]=new Vector4(position.x,position.y,1,0);
                var bounds=new Rect(position-Vector2.one*.02f,Vector2.one*.04f);
                output.Add(new WorldLightEmission(entry.Id,entry.Points,bounds,color*RadianceScale,EmitterWidth,Reach,opacity));
            }
            gone.Clear();foreach(var id in entries.Keys)if(!retained.Contains(id))gone.Add(id);
            foreach(var id in gone)entries.Remove(id);
        }
        public void Clear(){entries.Clear();retained.Clear();gone.Clear();}
    }
}
