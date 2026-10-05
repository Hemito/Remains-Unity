using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class PoseSupportFrame { public Vector2[] points; }
    [Serializable] public sealed class PoseSupportTrack { public string pose; public PoseSupportFrame[] frames; }
    [Serializable] public sealed class PoseSupportDefinition
    {
        public int schema,alphaMinimum;
        public string sourceHash;
        public string[] shapes;
        public PoseSupportTrack[] poses;
    }

    // Baked graphic contacts are gameplay data. No pixel/mesh analysis during motion.
    public sealed class PoseSupportContactData
    {
        private readonly Dictionary<string,IReadOnlyList<Vector2>[]> poses=new();
        public PoseSupportContactData(string anatomy,PoseSupportDefinition source)
        {
            if(source==null || source.schema!=1 || source.alphaMinimum<1 || source.alphaMinimum>255 ||
                source.sourceHash!=PonyRenderAsset.SourceHash() || source.shapes==null || source.shapes.Length==0 || source.poses==null)
                throw new InvalidOperationException($"Некорректные или устаревшие контакты ног: anatomy={anatomy}. Перезапеките контакты из текущей графики.");
            var clips=PonyAnimationData.Library.clips;
            if(source.poses.Length!=clips.Length)
                throw new InvalidOperationException($"Неполный набор поз контакта: anatomy={anatomy}, poses={source.poses.Length}, expected={clips.Length}.");
            foreach(var track in source.poses)
            {
                var clip=Array.Find(clips,value=>value.id==track?.pose);
                if(clip==null || track.frames==null || track.frames.Length!=clip.frames.Length || poses.ContainsKey(track.pose))
                    throw new InvalidOperationException($"Некорректная поза контакта: anatomy={anatomy}, pose={track?.pose}, frames={track?.frames?.Length}.");
                var frames=new IReadOnlyList<Vector2>[track.frames.Length];
                for(int frame=0;frame<frames.Length;frame++)
                {
                    var points=track.frames[frame]?.points;
                    int expected=Array.FindAll(clip.frames[frame].parts,part=>Array.IndexOf(source.shapes,part.shape)>=0).Length;
                    if(points==null || points.Length==0 || points.Length!=expected)
                        throw new InvalidOperationException($"Нет точек ног: anatomy={anatomy}, pose={track.pose}, frame={frame}.");
                    foreach(var point in points)
                        if(!float.IsFinite(point.x) || !float.IsFinite(point.y))
                            throw new InvalidOperationException($"Неконечная точка ног: anatomy={anatomy}, pose={track.pose}, frame={frame}, point={point}.");
                    frames[frame]=Array.AsReadOnly((Vector2[])points.Clone());
                }
                poses.Add(track.pose,frames);
            }
        }
        public IReadOnlyList<Vector2> Sample(string pose,int frame)
        {
            if(!poses.TryGetValue(pose,out var frames) || frame<0 || frame>=frames.Length)
                throw new InvalidOperationException($"Нет запечённого контакта ног: pose={pose}, frame={frame}.");
            return frames[frame];
        }
    }
}
