using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class AtlasActorClip
    {
        public string pose;public int row,count;public float framesPerSecond;public bool loop;
        public int FrameIndex(float time)
        {
            int frame=Mathf.FloorToInt(Mathf.Max(0,time)*framesPerSecond);
            return loop?frame%count:Mathf.Min(frame,count-1);
        }
    }
    [Serializable] public sealed class AtlasActorArtwork
    {
        public string resource;public int width,height,cellWidth,cellHeight;
        public float pixelsPerUnit,pivotX,pivotY;public bool facesLeft;
        public AtlasActorClip[] clips;
        public AtlasActorClip Clip(string pose)
        {
            foreach(var clip in clips) if(clip.pose==pose) return clip;
            throw new InvalidOperationException($"Нет позы атласа: resource={resource}, pose={pose}.");
        }
    }
    public static class AtlasAnimationData
    {
        private static readonly Dictionary<string,AtlasActorArtwork> cache=new();
        public static AtlasActorArtwork Get(string key)
        {
            if(cache.TryGetValue(key,out var cached)) return cached;
            var asset=Resources.Load<TextAsset>(key);
            if(asset==null) throw new InvalidOperationException("Нет описания атласа: "+key);
            var data=JsonUtility.FromJson<AtlasActorArtwork>(asset.text);
            if(data?.clips==null || data.clips.Length==0) throw new InvalidOperationException("Нет клипов атласа: "+key);
            var poses=new HashSet<string>();
            foreach(var clip in data.clips)
                if(clip==null || string.IsNullOrEmpty(clip.pose) || !poses.Add(clip.pose) || clip.count<1 ||
                    !float.IsFinite(clip.framesPerSecond) || clip.framesPerSecond<=0)
                    throw new InvalidOperationException("Некорректная поза атласа: "+key);
            cache.Add(key,data);return data;
        }
    }
}
