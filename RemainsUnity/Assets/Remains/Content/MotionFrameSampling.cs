using System;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class MotionFramePoint
    {
        [InspectorName("Скорость, м/с")] public float velocity;
        [InspectorName("Кадр (с нуля)")] public int frame;
    }

    public static class MotionFrameSampling
    {
        public static void Validate(MotionFramePoint[] points,int count,string context)
        {
            if(points==null || points.Length<2) throw new InvalidOperationException("Недостаточно точек кривой позы: "+context);
            float previous=float.NegativeInfinity;
            foreach(var point in points)
            {
                if(point==null || !float.IsFinite(point.velocity) || point.velocity<=previous || point.frame<0 || point.frame>=count)
                    throw new InvalidOperationException("Некорректная точка кривой позы: "+context);
                previous=point.velocity;
            }
        }

        // Shared interpolation for independently authored motion-to-pose data.
        public static int Sample(MotionFramePoint[] points,float velocity)
        {
            if(!float.IsFinite(velocity)) throw new InvalidOperationException("Нечисловая скорость при выборе позы.");
            if(velocity<=points[0].velocity) return points[0].frame;
            for(int i=1;i<points.Length;i++)
                if(velocity<=points[i].velocity)
                {
                    float fraction=Mathf.InverseLerp(points[i-1].velocity,points[i].velocity,velocity);
                    return Mathf.RoundToInt(Mathf.Lerp(points[i-1].frame,points[i].frame,fraction));
                }
            return points[^1].frame;
        }
    }
}
