using System;
using UnityEngine;

namespace Remains.Editor
{
    // Authoring aid over affine parts. Approximate limb geometry is explicit and editable;
    // the output is still the existing three-point/matrix representation.
    public static class PonyPuppetControls
    {
        public sealed class Input
        {
            public Vector2 Body,Foot=PonyPointControls.Parts[1].Rest[1],KneeOffset;
            public float Width=1,Attachment=.35f;
            public bool BodyRelative;
            public float BodyAngle;
            public PonyPuppetLimits.Settings Limits=new();
        }
        public sealed class Result
        {
            public Vector2[][] Points;
            public Vector2 Foot,Knee;
            public bool ReachLimited;
            public bool JointLimited;
        }
        public static Result Solve(Input input)
        {
            if(input==null || !Finite(input.Body) || !Finite(input.Foot) || !Finite(input.KneeOffset) ||
                !float.IsFinite(input.Width) || input.Width<.7f || input.Width>1.4f ||
                !float.IsFinite(input.Attachment) || input.Attachment<0 || input.Attachment>1)
                throw new InvalidOperationException("Кукольное управление: повреждены параметры позы.");
            if(input.Limits==null)throw new InvalidOperationException("Кукольное управление: отсутствуют пределы суставов.");
            input.Limits.Validate();
            var upper=PonyPointControls.Parts[0].Rest;var lower=PonyPointControls.Parts[1].Rest;
            Vector2 hip=upper[0]+input.Body,requested=input.Foot-hip,offset=PonyPuppetLimits.ProjectTarget(requested,input.Limits);
            double length1=Vector2.Distance(upper[0],upper[1]),length2=Vector2.Distance(lower[0],lower[1]);
            double distance=offset.magnitude;
            double min=Math.Max(Math.Abs(length1-length2)+.015,Math.Sqrt(length1*length1+length2*length2+2*length1*length2*Math.Cos(input.Limits.KneeMax*Math.PI/180)));
            double max=Math.Sqrt(length1*length1+length2*length2+2*length1*length2*Math.Cos(input.Limits.KneeMin*Math.PI/180));
            Vector2 axis=distance<1e-8?(lower[1]-upper[0]).normalized:offset/(float)distance;
            double used=Math.Max(min,Math.Min(max,distance));
            Vector2 foot=hip+axis*(float)used,normal=new(-axis.y,axis.x);
            double along=(length1*length1-length2*length2+used*used)/(2*used);
            double height=Math.Sqrt(Math.Max(0,length1*length1-along*along));
            float side=Mathf.Sign(Cross(lower[1]-upper[0],upper[1]-upper[0]));
            Vector2 knee=hip+axis*((float)along+input.KneeOffset.x)+normal*((float)height*side+input.KneeOffset.y);
            var intendedKnee=knee;knee=PonyPuppetLimits.ProjectKnee(hip,knee,axis,input.Limits);
            bool jointLimited=PonyPuppetLimits.ProjectChain(hip,ref knee,ref foot,input.Limits) || Vector2.Distance(requested,offset)>.00001f || Vector2.Distance(knee,intendedKnee)>.00001f;
            var first=Segment(upper,hip,knee,input.Width);var second=Segment(lower,knee,foot,input.Width);
            first[2]=Vector2.Lerp(first[2],upper[2]+input.Body,input.Attachment);
            return new Result {Points=new[]{first,second},Foot=foot,Knee=knee,ReachLimited=Vector2.Distance(foot,input.Foot)>.00001f,JointLimited=jointLimited};
        }
        public static Vector2[] Segment(Vector2[] source,Vector2 start,Vector2 end,float width)
        {
            Vector2 old=source[1]-source[0],axis=end-start,offset=source[2]-source[0];
            if(axis.sqrMagnitude<1e-10 || old.sqrMagnitude<1e-10)throw new InvalidOperationException("Кукольное управление: звено схлопнулось.");
            float along=Vector2.Dot(offset,old)/old.sqrMagnitude,across=Cross(old,offset)/old.magnitude;
            return new[]{start,end,start+axis*along+new Vector2(-axis.y,axis.x).normalized*(across*width)};
        }
        public static Vector2[][] ApplyFine(Result pose,Vector2[][] correction)
        {
            if(correction?.Length!=2 || Array.Exists(correction,p=>p?.Length!=3))
                throw new InvalidOperationException("Кукольное управление: повреждены поправки формы.");
            var output=new Vector2[2][];
            for(int p=0;p<2;p++)
            {
                var rest=PonyPointControls.Parts[p].Rest;var delta=PonyAffinePoints.Solve(rest,pose.Points[p]);
                var adjusted=new Vector2[3];for(int i=0;i<3;i++)adjusted[i]=rest[i]+correction[p][i];
                output[p]=PonyAffinePoints.Map(delta,adjusted);
            }
            return output;
        }
        public static Matrix4x4[] Matrices(PonyAnimationArtwork art,Input input,Vector2[][] correction=null)
        {
            var posed=Solve(input);var points=correction==null?posed.Points:ApplyFine(posed,correction);
            if(!PonyPuppetLimits.Check(points,input.Limits,out var reason))throw new InvalidOperationException("Кукольное управление: "+reason);
            var matrices=PonyPointControls.Matrices(art,points);
            for(int n=0;n<matrices.Length;n++)if(Array.FindIndex(PonyPointControls.Parts,p=>p.Socket==art.Frame.Nodes[n].socket)<0)
                matrices[n]=Matrix4x4.Translate(input.Body)*matrices[n];
            return matrices;
        }
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static bool Finite(Vector2 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y);
    }
}
