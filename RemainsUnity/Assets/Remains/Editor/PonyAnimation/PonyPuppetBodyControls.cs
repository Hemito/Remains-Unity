using System;
using UnityEngine;

namespace Remains.Editor
{
    // New authoring controls. The old prototype remains available for comparison.
    public static class PonyPuppetBodyControls
    {
        public static readonly Vector2 Pivot=new(0,.85f);
        public static PonyPuppetControls.Input Prepare(PonyPuppetControls.Input source)=>new()
        {Body=source.Body,Foot=source.Foot,Width=source.Width,Attachment=source.Attachment,Limits=source.Limits,BodyRelative=true,BodyAngle=source.BodyAngle};
        public static Matrix4x4 Frame(PonyPuppetControls.Input input)
        {
            if(!float.IsFinite(input.BodyAngle) || Mathf.Abs(input.BodyAngle)>35 || !float.IsFinite(input.Body.x) || !float.IsFinite(input.Body.y))
                throw new InvalidOperationException("Кукла: недопустимый наклон или положение корпуса.");
            return Matrix4x4.Translate(input.Body)*Matrix4x4.Translate(Pivot)*Matrix4x4.Rotate(Quaternion.Euler(0,0,input.BodyAngle))*Matrix4x4.Translate(-Pivot);
        }
        public static Vector2[][] Map(Matrix4x4 frame,Vector2[][] points)=>Array.ConvertAll(points,p=>PonyAffinePoints.Map(frame,p));
        public static PonyPuppetControls.Result Solve(PonyPuppetControls.Input input)
        {
            if(!float.IsFinite(input.KneeOffset.x) || !float.IsFinite(input.KneeOffset.y) || input.KneeOffset.sqrMagnitude>1e-12)
                throw new InvalidOperationException("Кукла: свободное смещение колена доступно только в тонкой настройке формы.");
            var frame=Frame(input);var local=new PonyPuppetControls.Input {Foot=frame.inverse.MultiplyPoint3x4(input.Foot),Width=input.Width,Attachment=input.Attachment,Limits=input.Limits};
            var result=PonyPuppetControls.Solve(local);result.Points=Map(frame,result.Points);
            result.Foot=frame.MultiplyPoint3x4(result.Foot);result.Knee=frame.MultiplyPoint3x4(result.Knee);return result;
        }
        public static PonyPuppetControls.Input MoveKnee(PonyPuppetControls.Input input,Vector2 target)
        {
            if(!float.IsFinite(target.x) || !float.IsFinite(target.y))throw new InvalidOperationException("Кукла: цель колена не конечна.");
            var frame=Frame(input);var inverse=frame.inverse;var current=Solve(input);
            var rest=PonyPointControls.Parts;Vector2 hip=rest[0].Rest[0],first=rest[0].Rest[1]-hip,second=rest[1].Rest[1]-rest[1].Rest[0];
            Vector2 requested=(Vector2)inverse.MultiplyPoint3x4(target)-hip;
            float angle=requested.sqrMagnitude<1e-10?Vector2.SignedAngle(first,(Vector2)inverse.MultiplyPoint3x4(current.Knee)-hip):Vector2.SignedAngle(first,requested);
            Vector2 upper=Rotate(first,Mathf.Clamp(angle,input.Limits.UpperMin,input.Limits.UpperMax));
            Vector2 knee=hip+upper,lower=(Vector2)inverse.MultiplyPoint3x4(current.Foot)-knee;
            var axis=rest[1].Rest[1]-hip;float side=Mathf.Sign(axis.x*first.y-axis.y*first.x);
            float bend=lower.sqrMagnitude<1e-10?input.Limits.KneeMin:-side*Vector2.SignedAngle(upper,lower);
            Vector2 foot=knee+Rotate(upper.normalized,-side*Mathf.Clamp(bend,input.Limits.KneeMin,input.Limits.KneeMax))*second.magnitude;
            var next=Prepare(input);next.Foot=frame.MultiplyPoint3x4(foot);return next;
        }
        private static Vector2 Rotate(Vector2 value,float angle)
        {float r=angle*Mathf.Deg2Rad,c=Mathf.Cos(r),s=Mathf.Sin(r);return new Vector2(value.x*c-value.y*s,value.x*s+value.y*c);}
    }
}
