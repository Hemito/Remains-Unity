using System;
using UnityEngine;

namespace Remains.Editor
{
    // Draft authoring limits for this two-part sample, not inferred equine anatomy.
    public static class PonyPuppetLimits
    {
        public const float MaximumDragAngleStep=20;
        public sealed class Settings
        {
            public float UpperMin=-45,UpperMax=75,KneeMin=2,KneeMax=135;
            public float FootCone=80,MinLength=.75f,MaxLength=1.25f;
            public void Validate()
            {
                if(!float.IsFinite(UpperMin) || !float.IsFinite(UpperMax) || !float.IsFinite(KneeMin) || !float.IsFinite(KneeMax) ||
                    UpperMin< -80 || UpperMax>100 || UpperMin>0 || UpperMax<0 || UpperMax-UpperMin>170 ||
                    KneeMin<0 || KneeMax>160 || KneeMin>=KneeMax || !float.IsFinite(FootCone) || FootCone<30 || FootCone>85 ||
                    !float.IsFinite(MinLength) || !float.IsFinite(MaxLength) || MinLength<.5f || MaxLength>1.5f || MinLength>1 || MaxLength<1)
                    throw new InvalidOperationException("Кукольное управление: некорректные пределы суставов.");
            }
        }
        public static Vector2 ProjectTarget(Vector2 offset,Settings limits)
        {
            var rest=PonyPointControls.Parts;var axis=(rest[1].Rest[1]-rest[0].Rest[0]).normalized;
            var normal=new Vector2(-axis.y,axis.x);
            float along=Mathf.Max(.03f,Vector2.Dot(offset,axis)),span=along*Mathf.Tan(limits.FootCone*Mathf.Deg2Rad);
            return axis*along+normal*Mathf.Clamp(Vector2.Dot(offset,normal),-span,span);
        }
        public static bool ProjectChain(Vector2 hip,ref Vector2 knee,ref Vector2 foot,Settings limits)
        {
            var originalKnee=knee;var originalFoot=foot;var rest=PonyPointControls.Parts;
            var first=rest[0].Rest[1]-rest[0].Rest[0];var second=rest[1].Rest[1]-rest[1].Rest[0];
            var upper=knee-hip;var lower=foot-knee;
            float upperAngle=upper.sqrMagnitude<1e-12?0:Vector2.SignedAngle(first,upper);
            float bend=upper.sqrMagnitude<1e-12 || lower.sqrMagnitude<1e-12?limits.KneeMin:-Side*Vector2.SignedAngle(upper,lower);
            float angle=Mathf.Clamp(upperAngle,limits.UpperMin,limits.UpperMax),usedBend=Mathf.Clamp(bend,limits.KneeMin,limits.KneeMax);
            float l1=Mathf.Clamp(upper.magnitude,first.magnitude*limits.MinLength,first.magnitude*limits.MaxLength);
            float l2=Mathf.Clamp(lower.magnitude,second.magnitude*limits.MinLength,second.magnitude*limits.MaxLength);
            if(angle==upperAngle && usedBend==bend && l1==upper.magnitude && l2==lower.magnitude)return false;
            Vector2 direction=Rotate(first.normalized,angle);
            knee=hip+direction*l1;foot=knee+Rotate(direction,-Side*usedBend)*l2;
            return Vector2.Distance(knee,originalKnee)>.00001f || Vector2.Distance(foot,originalFoot)>.00001f;
        }
        public static Vector2 ProjectKnee(Vector2 hip,Vector2 knee,Vector2 footAxis,Settings limits)
        {
            Vector2 offset=knee-hip,normal=new(-footAxis.y,footAxis.x);
            float span=Vector2.Distance(PonyPointControls.Parts[0].Rest[0],PonyPointControls.Parts[0].Rest[1])*limits.MaxLength;
            return hip+footAxis*Mathf.Max(.005f,Vector2.Dot(offset,footAxis))+normal*Mathf.Clamp(Vector2.Dot(offset,normal),-span,span);
        }
        public static bool Check(Vector2[][] points,Settings limits,out string reason)
        {
            var rest=PonyPointControls.Parts;var upper=points[0][1]-points[0][0];var lower=points[1][1]-points[1][0];
            var first=rest[0].Rest[1]-rest[0].Rest[0];var second=rest[1].Rest[1]-rest[1].Rest[0];
            float angle=Vector2.SignedAngle(first,upper),bend=-Side*Vector2.SignedAngle(upper,lower);const float epsilon=.002f;
            reason=upper.sqrMagnitude<1e-10 || lower.sqrMagnitude<1e-10?"Звено ноги схлопнулось.":
                angle<limits.UpperMin-epsilon || angle>limits.UpperMax+epsilon?"Поворот крупа вышел за заданный предел.":
                bend<limits.KneeMin-epsilon || bend>limits.KneeMax+epsilon?"Колено перегнуто или вышло за предел сгиба.":
                upper.magnitude<first.magnitude*limits.MinLength-epsilon || upper.magnitude>first.magnitude*limits.MaxLength+epsilon ||
                lower.magnitude<second.magnitude*limits.MinLength-epsilon || lower.magnitude>second.magnitude*limits.MaxLength+epsilon?"Длина звена вышла за заданный предел.":null;
            return reason==null;
        }
        private static float Side
        {
            get {var rest=PonyPointControls.Parts;var axis=rest[1].Rest[1]-rest[0].Rest[0];var knee=rest[0].Rest[1]-rest[0].Rest[0];return Mathf.Sign(axis.x*knee.y-axis.y*knee.x);}
        }
        private static Vector2 Rotate(Vector2 value,float degrees)
        {
            float angle=degrees*Mathf.Deg2Rad,c=Mathf.Cos(angle),s=Mathf.Sin(angle);return new Vector2(c*value.x-s*value.y,s*value.x+c*value.y);
        }
    }
}
