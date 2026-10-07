using System;
using UnityEngine;

namespace Remains.Domain
{
    public interface IRangedShotDirectionRule
    {
        Vector2 Select(Vector2 unitAim,float halfAngleDegrees,float sample);
    }
    public sealed class UniformAngularShotDirectionRule : IRangedShotDirectionRule
    {
        public Vector2 Select(Vector2 aim,float halfAngleDegrees,float sample)
        {
            if(halfAngleDegrees==0) return aim;
            float radians=Mathf.Lerp(-halfAngleDegrees,halfAngleDegrees,sample)*Mathf.Deg2Rad;
            float sine=Mathf.Sin(radians),cosine=Mathf.Cos(radians);
            return new Vector2(aim.x*cosine-aim.y*sine,aim.x*sine+aim.y*cosine);
        }
    }
    // Independent Unity policy: a uniform angular cone about the requested aim.
    // Sampling is an owner launch operation, never an observer or flight-step operation.
    public static class RangedShotDirection
    {
        private static readonly IRangedShotDirectionRule standard=new UniformAngularShotDirectionRule();
        public static Vector2 Select(Vector2 aimDirection,float halfAngleDegrees,float sample)
            =>Select(aimDirection,halfAngleDegrees,sample,standard);
        public static Vector2 Select(Vector2 aimDirection,float halfAngleDegrees,float sample,IRangedShotDirectionRule rule)
        {
            if(rule==null) throw new ArgumentNullException(nameof(rule),"Не задано правило направления выстрела.");
            if(!float.IsFinite(aimDirection.x) || !float.IsFinite(aimDirection.y) ||
                !float.IsFinite(aimDirection.sqrMagnitude) || aimDirection.sqrMagnitude<.00000001f)
                throw new ArgumentException($"Разброс выстрела: отсутствует конечное направление прицеливания {aimDirection}.");
            if(!float.IsFinite(halfAngleDegrees) || halfAngleDegrees<0 || halfAngleDegrees>90)
                throw new ArgumentException($"Разброс выстрела: полуширина {halfAngleDegrees} должна быть в диапазоне 0–90°.");
            if(!float.IsFinite(sample) || sample<0 || sample>1)
                throw new ArgumentException($"Разброс выстрела: случайная доля {sample} должна быть в диапазоне 0–1.");
            var direction=rule.Select(aimDirection.normalized,halfAngleDegrees,sample);
            if(!float.IsFinite(direction.x) || !float.IsFinite(direction.y) ||
                !float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude<.00000001f)
                throw new InvalidOperationException($"Правило направления выстрела {rule.GetType().FullName} вернуло некорректное направление {direction}.");
            return direction.normalized;
        }
    }
}
