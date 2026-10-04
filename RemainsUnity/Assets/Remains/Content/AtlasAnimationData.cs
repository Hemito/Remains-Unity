using System;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class AtlasActorClip
    {
        [InspectorName("ID позы")] public string pose;
        [InspectorName("Строка атласа")] public int row;
        [InspectorName("Количество кадров")] public int count;
        [InspectorName("Кадров в секунду")] public float framesPerSecond;
        [InspectorName("Зациклить")] public bool loop;
        [Min(0),InspectorName("Первый кадр повторяемого участка")] public int loopStartFrame;
        public int FrameIndex(float time,bool stationary=false)
        {
            return AnimationFrames.Sample(time,framesPerSecond,count,loop,loopStartFrame,stationary,playIntro:true);
        }
    }
    [Serializable] public sealed class AtlasActorArtwork
    {
        [InspectorName("Стабильный ключ текстуры")] public string resource;
        [InspectorName("Ширина атласа, пикс.")] public int width;
        [InspectorName("Высота атласа, пикс.")] public int height;
        [InspectorName("Ширина ячейки, пикс.")] public int cellWidth;
        [InspectorName("Высота ячейки, пикс.")] public int cellHeight;
        [InspectorName("Пикселей на метр")] public float pixelsPerUnit;
        [InspectorName("Точка привязки X (0–1)")] public float pivotX;
        [InspectorName("Точка привязки Y (0–1)")] public float pivotY;
        [InspectorName("Исходная графика смотрит влево")] public bool facesLeft;
        [InspectorName("Позы")] public AtlasActorClip[] clips;
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
            var saved=Resources.Load<AtlasSpriteAsset>(key);
            var asset=saved==null?Resources.Load<TextAsset>(key):null;
            if(saved==null && asset==null) throw new InvalidOperationException("Нет описания атласа: "+key);
            var data=saved!=null?saved.CreateDefinition():JsonUtility.FromJson<AtlasActorArtwork>(asset.text);
            if(data?.clips==null || data.clips.Length==0) throw new InvalidOperationException("Нет клипов атласа: "+key);
            var poses=new HashSet<string>();
            foreach(var clip in data.clips)
                if(clip==null || string.IsNullOrEmpty(clip.pose) || !poses.Add(clip.pose) || clip.count<1 ||
                    !float.IsFinite(clip.framesPerSecond) || clip.framesPerSecond<=0 ||
                    clip.loopStartFrame<0 || clip.loopStartFrame>=clip.count || (!clip.loop && clip.loopStartFrame!=0))
                    throw new InvalidOperationException("Некорректная поза атласа: "+key);
            cache.Add(key,data);return data;
        }
    }
}
