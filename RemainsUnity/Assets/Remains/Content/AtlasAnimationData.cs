using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Domain;
namespace Remains.Content
{
    [Serializable] public sealed class AtlasWeaponSocketPoint
    {
        [InspectorName("X от левого края ячейки, пикс.")] public float x;
        [InspectorName("Y от верхнего края ячейки, пикс.")] public float y;
    }
    [Serializable] public sealed class AtlasActorClip
    {
        [InspectorName("ID позы")] public string pose;
        [InspectorName("Строка атласа")] public int row;
        // Original blit animations may start inside a row (BlitAnim ff): the first column of the clip.
        [Min(0),InspectorName("Первый столбец")] public int column;
        [InspectorName("Количество кадров")] public int count;
        [InspectorName("Кадров в секунду")] public float framesPerSecond;
        [InspectorName("Зациклить")] public bool loop;
        [Min(0),InspectorName("Первый кадр повторяемого участка")] public int loopStartFrame;
        [InspectorName("Кадры по вертикальной скорости (пусто — по времени)")] public MotionFramePoint[] verticalFrames;
        [InspectorName("Точки оружия по кадрам (для типа с привязкой к атласу)")] public AtlasWeaponSocketPoint[] weaponSockets;
        public bool UsesMotion=>verticalFrames!=null && verticalFrames.Length>0;
        public int Sample(ActorState actor)=>UsesMotion && !actor.down
            ?MotionFrameSampling.Sample(verticalFrames,actor.vy)
            :FrameIndex(actor.poseTime,ActorPose.StationaryClimb(actor));
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
        public static AtlasActorArtwork RequirePrepared(string key)=>cache.TryGetValue(key,out var value)?value:
            throw new InvalidOperationException($"Атлас не подготовлен при регистрации содержимого: {key}.");
        public static AtlasActorArtwork Get(string key)
        {
            if(cache.TryGetValue(key,out var cached)) return cached;
            var saved=GameAssets.Load<AtlasSpriteAsset>(key);
            var asset=saved==null?GameAssets.Load<TextAsset>(key):null;
            if(saved==null && asset==null) throw new InvalidOperationException("Нет описания атласа: "+key);
            var data=saved!=null?saved.CreateDefinition():JsonUtility.FromJson<AtlasActorArtwork>(asset.text);
            if(data?.clips==null || data.clips.Length==0) throw new InvalidOperationException("Нет клипов атласа: "+key);
            var poses=new HashSet<string>();
            foreach(var clip in data.clips)
            {
                if(clip==null || string.IsNullOrEmpty(clip.pose) || !poses.Add(clip.pose) || clip.count<1 || clip.column<0 ||
                    !float.IsFinite(clip.framesPerSecond) || clip.framesPerSecond<=0 ||
                    clip.loopStartFrame<0 || clip.loopStartFrame>=clip.count || (!clip.loop && clip.loopStartFrame!=0))
                    throw new InvalidOperationException("Некорректная поза атласа: "+key);
                if(clip.UsesMotion) MotionFrameSampling.Validate(clip.verticalFrames,clip.count,key+":"+clip.pose);
                AtlasWeaponSockets.ValidateClip(clip,key);
            }
            cache.Add(key,data);return data;
        }
    }
}
