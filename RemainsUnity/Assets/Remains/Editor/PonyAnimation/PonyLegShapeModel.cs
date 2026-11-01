using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Local example-based affine shape, independent of Unity assets and of the whole-body pose.
    public sealed class PonyLegShapeModel
    {
        public sealed class Sample
        {
            public string Id,Variant;
            public float[] Features,Shape;
            public readonly List<(string Id,string Family)> Sources=new();
            public bool Mirrored;
        }
        public readonly struct Target
        {
            public readonly Vector2 Hip,Knee,Foot;
            public Target(Vector2 hip,Vector2 knee,Vector2 foot){Hip=hip;Knee=knee;Foot=foot;}
        }
        public sealed class Prediction
        {
            public string Variant;
            public Vector2[][] Points;
            public int[] Indices;
            public float[] Weights;
            public float Distance;
        }
        public readonly List<Sample> Samples=new();
        public const int Neighbours=12;
        public static readonly float[] FeatureWeights={2,2,1,1,1,1,.35f,.35f};
        private static Vector2 Hip=>PonyPointControls.Parts[0].Rest[0];
        private static float Scale=>Vector2.Distance(Hip,PonyPointControls.Parts[0].Rest[1])+Vector2.Distance(PonyPointControls.Parts[1].Rest[0],PonyPointControls.Parts[1].Rest[1]);
        public static Target Driver(Vector2[][] points)=>new(Hip,(points[0][1]+points[1][0])*.5f,points[1][1]);
        public static float[] Features(Target target)
        {
            var u=target.Knee-target.Hip;var v=target.Foot-target.Knee;float a=u.magnitude,b=v.magnitude;
            if(a<.025f || b<.025f)throw new InvalidOperationException("Примеры формы: вырожденная конфигурация ноги.");
            var foot=(target.Foot-target.Hip)/Scale;u/=a;v/=b;
            return new[]{foot.x,foot.y,u.x,u.y,Vector2.Dot(u,v),Cross(u,v),Mathf.Log(a/Vector2.Distance(Hip,PonyPointControls.Parts[0].Rest[1])),Mathf.Log(b/Vector2.Distance(PonyPointControls.Parts[1].Rest[0],PonyPointControls.Parts[1].Rest[1]))};
        }
        public static float[] Encode(Vector2[][] points,Target target)
        {
            var u=target.Knee-target.Hip;var v=target.Foot-target.Knee;
            var end=InBasis(points[0][1]-target.Knee,u);var start=InBasis(points[1][0]-target.Knee,v);
            var upper=Side(points[0]);var lower=Side(points[1]);var upRest=Side(PonyPointControls.Parts[0].Rest);var lowRest=Side(PonyPointControls.Parts[1].Rest);
            var slide=points[0][0]-target.Hip;
            return new[]{slide.x,slide.y,end.x,end.y,start.x,start.y,upper.x,upper.y/upRest.y,lower.x,lower.y/lowRest.y};
        }
        public static Vector2[][] Decode(float[] shape,Target target)
        {
            var u=target.Knee-target.Hip;var v=target.Foot-target.Knee;
            Vector2 a=target.Hip+new Vector2(shape[0],shape[1]),b=target.Knee+FromBasis(new Vector2(shape[2],shape[3]),u);
            Vector2 c=target.Knee+FromBasis(new Vector2(shape[4],shape[5]),v),d=target.Foot;
            return new[]{new[]{a,b,Third(a,b,shape[6],shape[7]*Side(PonyPointControls.Parts[0].Rest).y)},
                new[]{c,d,Third(c,d,shape[8],shape[9]*Side(PonyPointControls.Parts[1].Rest).y)}};
        }
        public Prediction Predict(Target target,string filter="all",string previousVariant=null,Func<Sample,bool> include=null)
        {
            var features=Features(target);
            var nearest=Samples.Select((s,i)=>(Sample:s,Index:i,Distance:Distance(features,s.Features)))
                .Where(s=>!s.Sample.Mirrored && (filter=="all" || s.Sample.Sources.Any(p=>p.Family==filter)) && (include==null || include(s.Sample)))
                .OrderBy(s=>s.Distance).ThenBy(s=>s.Index).Take(Neighbours).ToArray();
            if(nearest.Length==0)throw new InvalidOperationException("Примеры формы: нет совместимых образцов для фильтра "+filter);
            double Kernel(double distance)=>1/Math.Pow(distance+.0004,2);
            var scores=nearest.GroupBy(n=>n.Sample.Variant).Select(g=>(Variant:g.Key,Weight:g.Sum(n=>Kernel(n.Distance)))).OrderByDescending(g=>g.Weight).ThenBy(g=>g.Variant,StringComparer.Ordinal).ToArray();
            string variant=scores[0].Variant;
            if(previousVariant!=null && scores.Any(s=>s.Variant==previousVariant && s.Weight>=scores[0].Weight*.8))variant=previousVariant;
            var selected=nearest.Where(n=>n.Sample.Variant==variant).ToArray();
            if(selected[0].Distance<1e-10)selected=selected.Take(1).ToArray();
            double sum=selected.Sum(n=>Kernel(n.Distance));var weights=selected.Select(n=>(float)(Kernel(n.Distance)/sum)).ToArray();var shape=new float[10];
            for(int i=0;i<selected.Length;i++)for(int p=0;p<shape.Length;p++)shape[p]+=selected[i].Sample.Shape[p]*weights[i];
            return new Prediction {Variant=variant,Points=Decode(shape,target),Indices=selected.Select(n=>n.Index).ToArray(),Weights=weights,Distance=(float)Math.Sqrt(nearest[0].Distance)};
        }
        public static double Distance(float[] a,float[] b)
        {double sum=0;for(int i=0;i<a.Length;i++){double d=(a[i]-b[i])*FeatureWeights[i];sum+=d*d;}return sum;}
        private static Vector2 Side(Vector2[] p)
        {var axis=p[1]-p[0];float length=axis.magnitude;if(length<1e-5)throw new InvalidOperationException("Примеры формы: схлопнувшаяся деталь.");var offset=p[2]-p[0];return new Vector2(Vector2.Dot(offset,axis)/axis.sqrMagnitude,Cross(axis,offset)/length);}
        private static Vector2 Third(Vector2 a,Vector2 b,float along,float across)
        {var axis=b-a;if(axis.sqrMagnitude<1e-10)throw new InvalidOperationException("Примеры формы: результат схлопнулся.");return a+axis*along+new Vector2(-axis.y,axis.x).normalized*across;}
        private static Vector2 InBasis(Vector2 offset,Vector2 axis)=>new(Vector2.Dot(offset,axis)/axis.sqrMagnitude,Cross(axis,offset)/axis.sqrMagnitude);
        private static Vector2 FromBasis(Vector2 value,Vector2 axis)=>axis*value.x+new Vector2(-axis.y,axis.x)*value.y;
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    }
}
