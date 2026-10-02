using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class MotionFramePoint { public float velocity; public int frame; }
    [Serializable] public sealed class MotionFrameCurve { public string clip,axis; public bool limited; public float maxVerticalVelocity,maxHorizontalSpeed; public MotionFramePoint[] points; }
    [Serializable] public sealed class MotionFrameLibrary { public MotionFrameCurve[] curves; }

    // Authored pose curves consume replicated motion, never a local animation clock.
    public static class PonyMotionFrames
    {
        private static MotionFrameLibrary library;
        public static void Load(PonyLibrary animation)
        {
            if(library!=null) return;
            var asset=Resources.Load<TextAsset>("Original/Pony/motion-frames");
            if(asset==null) throw new InvalidOperationException("Нет кривых поз Original/Pony/motion-frames.");
            var parsed=JsonUtility.FromJson<MotionFrameLibrary>(asset.text);
            if(parsed?.curves==null) throw new InvalidOperationException("Нет списка кривых поз пони.");
            var ids=new System.Collections.Generic.HashSet<string>();
            foreach(var curve in parsed.curves)
            {
                var clip=Array.Find(animation.clips,c=>c.id==curve.clip);
                if(clip==null || !ids.Add(curve.clip+":"+curve.axis) || curve.points==null || curve.points.Length<2)
                    throw new InvalidOperationException("Некорректная кривая позы: "+curve.clip);
                if(curve.axis!=null && curve.axis!="" && curve.axis!="vertical" && curve.axis!="forward")
                    throw new InvalidOperationException("Неизвестная ось кривой позы: "+curve.clip+"/"+curve.axis);
                if(curve.limited && (!float.IsFinite(curve.maxVerticalVelocity) || !float.IsFinite(curve.maxHorizontalSpeed) || curve.maxHorizontalSpeed<0))
                    throw new InvalidOperationException("Неверные ограничения кривой позы: "+curve.clip);
                float previous=float.NegativeInfinity;
                foreach(var point in curve.points)
                {
                    if(!float.IsFinite(point.velocity) || point.velocity<=previous || point.frame<0 || point.frame>=clip.frames.Length)
                        throw new InvalidOperationException("Некорректная точка кривой позы: "+curve.clip);
                    previous=point.velocity;
                }
            }
            library=parsed;
        }
        public static int Sample(ActorState actor,float elapsed)
        {
            string clip=ActorPose.Clip(actor);
            foreach(var curve in library.curves)
            {
                if(curve.clip!=clip || actor.down) continue;
                if(curve.limited && (actor.vy>curve.maxVerticalVelocity || Mathf.Abs(actor.vx)>curve.maxHorizontalSpeed)) continue;
                float velocity=curve.axis=="forward"?actor.vx*actor.facing:actor.vy;
                var points=curve.points;
                if(velocity<=points[0].velocity) return points[0].frame;
                for(int i=1;i<points.Length;i++)
                    if(velocity<=points[i].velocity)
                    {
                        float fraction=Mathf.InverseLerp(points[i-1].velocity,points[i].velocity,velocity);
                        return Mathf.RoundToInt(Mathf.Lerp(points[i-1].frame,points[i].frame,fraction));
                    }
                return points[^1].frame;
            }
            return PonyAnimationData.FrameIndex(clip,elapsed,actor.down,ActorPose.StationaryClimb(actor));
        }
    }
}
