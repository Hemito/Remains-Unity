using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Editor
{
    // Fits attachment points from authored affine motion. A low residual and a full numerical rank
    // are separate requirements: translation-only motion cannot uniquely locate a hinge.
    public static class PonyJointFit
    {
        public readonly struct Sample
        {
            public readonly Matrix4x4 Parent,Child;
            public readonly string Clip;
            public readonly int Frame;
            public Sample(Matrix4x4 parent,Matrix4x4 child,string clip,int frame)
            {Parent=parent;Child=child;Clip=clip;Frame=frame;}
        }
        public sealed class ClipError
        {
            public string Clip;
            public int Samples,WorstFrame;
            public double Rms,Max;
        }
        public sealed class ErrorSummary
        {
            public int Samples,WorstFrame;
            public string WorstClip;
            public double Rms,Max;
            public ClipError[] Clips;
        }
        public sealed class Result
        {
            public string Mode;
            public Vector2 ParentAnchor,ChildAnchor,SharedRestPoint;
            public int Rank,Unknowns,SampleCount;
            public double Condition,Rms,Max;
            public bool Identifiable,NumericallyConverged;
            public double[] SingularValues;
            public ClipError[] Clips;
        }
        private sealed class LeastSquares
        {
            public double[] Values,Singular;
            public int Rank;
            public double Condition;
            public bool Converged;
        }
        // Source matrices are floats. Directions below 1e-7 of the strongest direction cannot
        // support a trustworthy pivot; a full-rank fit with condition >1e5 is still flagged unstable.
        public const double DefaultRankTolerance=1e-7;
        public const double MaximumIdentifiableCondition=1e5;

        public static Result FitIndependent(IEnumerable<Sample> source,double relativeRankTolerance=DefaultRankTolerance)
        {
            var samples=RequireSamples(source);var a=new double[samples.Length*2,4];var b=new double[samples.Length*2];
            for(int i=0;i<samples.Length;i++)
            {
                var p=samples[i].Parent;var c=samples[i].Child;int row=i*2;
                a[row,0]=p.m00;a[row,1]=p.m01;a[row,2]=-c.m00;a[row,3]=-c.m01;b[row]=(double)c.m03-p.m03;
                a[row+1,0]=p.m10;a[row+1,1]=p.m11;a[row+1,2]=-c.m10;a[row+1,3]=-c.m11;b[row+1]=(double)c.m13-p.m13;
            }
            var fit=Solve(a,b,relativeRankTolerance);
            var result=new Result {Mode="independent-local-anchors",ParentAnchor=Point(fit.Values[0],fit.Values[1]),ChildAnchor=Point(fit.Values[2],fit.Values[3])};
            return Complete(result,fit,samples,4);
        }

        public static Result FitSharedRest(IEnumerable<Sample> source,Matrix4x4 parentRest,Matrix4x4 childRest,double relativeRankTolerance=DefaultRankTolerance)
        {
            var samples=RequireSamples(source);RequireMatrix(parentRest,"исходная родительская часть");RequireMatrix(childRest,"исходная дочерняя часть");
            var parentInverse=parentRest.inverse;var childInverse=childRest.inverse;
            var a=new double[samples.Length*2,2];var b=new double[samples.Length*2];
            for(int i=0;i<samples.Length;i++)
            {
                var p=samples[i].Parent*parentInverse;var c=samples[i].Child*childInverse;int row=i*2;
                a[row,0]=(double)p.m00-c.m00;a[row,1]=(double)p.m01-c.m01;b[row]=(double)c.m03-p.m03;
                a[row+1,0]=(double)p.m10-c.m10;a[row+1,1]=(double)p.m11-c.m11;b[row+1]=(double)c.m13-p.m13;
            }
            var fit=Solve(a,b,relativeRankTolerance);var common=Point(fit.Values[0],fit.Values[1]);
            var result=new Result {Mode="shared-rest-point",SharedRestPoint=common,
                ParentAnchor=parentInverse.MultiplyPoint3x4(common),ChildAnchor=childInverse.MultiplyPoint3x4(common)};
            return Complete(result,fit,samples,2);
        }

        public static ErrorSummary Evaluate(Result fit,IEnumerable<Sample> source)
        {
            if(fit==null)throw Error("нет результата для проверки");
            var samples=RequireSamples(source);var groups=new Dictionary<string,List<(double Error,int Frame)>>(StringComparer.Ordinal);
            double total=0,maximum=-1;string worstClip=null;int worstFrame=-1;
            foreach(var sample in samples)
            {
                double px=(double)sample.Parent.m00*fit.ParentAnchor.x+(double)sample.Parent.m01*fit.ParentAnchor.y+sample.Parent.m03;
                double py=(double)sample.Parent.m10*fit.ParentAnchor.x+(double)sample.Parent.m11*fit.ParentAnchor.y+sample.Parent.m13;
                double cx=(double)sample.Child.m00*fit.ChildAnchor.x+(double)sample.Child.m01*fit.ChildAnchor.y+sample.Child.m03;
                double cy=(double)sample.Child.m10*fit.ChildAnchor.x+(double)sample.Child.m11*fit.ChildAnchor.y+sample.Child.m13;
                double distance=Math.Sqrt((px-cx)*(px-cx)+(py-cy)*(py-cy));
                if(!Finite(distance))throw Error("неконечная ошибка кадра "+sample.Clip+"/"+sample.Frame);
                total+=distance*distance;if(distance>maximum){maximum=distance;worstClip=sample.Clip;worstFrame=sample.Frame;}
                if(!groups.TryGetValue(sample.Clip,out var errors)){errors=new List<(double,int)>();groups.Add(sample.Clip,errors);}errors.Add((distance,sample.Frame));
            }
            var clips=new List<ClipError>();
            foreach(var group in groups.OrderBy(g=>g.Key,StringComparer.Ordinal))
            {
                var worst=group.Value.OrderByDescending(value=>value.Error).First();
                clips.Add(new ClipError {Clip=group.Key,Samples=group.Value.Count,WorstFrame=worst.Frame,
                    Rms=Math.Sqrt(group.Value.Sum(value=>value.Error*value.Error)/group.Value.Count),Max=worst.Error});
            }
            return new ErrorSummary {Samples=samples.Length,Rms=Math.Sqrt(total/samples.Length),Max=maximum,WorstClip=worstClip,WorstFrame=worstFrame,Clips=clips.ToArray()};
        }

        private static Result Complete(Result result,LeastSquares fit,Sample[] samples,int unknowns)
        {
            var errors=Evaluate(result,samples);result.Rank=fit.Rank;result.Unknowns=unknowns;result.SampleCount=samples.Length;
            result.Condition=fit.Condition;result.SingularValues=fit.Singular;result.NumericallyConverged=fit.Converged;
            result.Identifiable=fit.Converged && fit.Rank==unknowns && fit.Condition<=MaximumIdentifiableCondition;
            result.Rms=errors.Rms;result.Max=errors.Max;result.Clips=errors.Clips;return result;
        }

        // One-sided Jacobi SVD operates on the original tall matrix, not A^T A. With at most
        // four unknowns it is inexpensive and retains near-null directions for rank diagnostics.
        private static LeastSquares Solve(double[,] input,double[] target,double relativeTolerance)
        {
            if(!Finite(relativeTolerance) || relativeTolerance<=0 || relativeTolerance>=1)throw Error("неверный допуск численного ранга");
            int rows=input.GetLength(0),columns=input.GetLength(1);var a=(double[,])input.Clone();var v=new double[columns,columns];
            for(int i=0;i<columns;i++)v[i,i]=1;
            bool converged=false;
            for(int sweep=0;sweep<100;sweep++)
            {
                bool changed=false;
                for(int p=0;p<columns-1;p++)for(int q=p+1;q<columns;q++)
                {
                    double alpha=0,beta=0,gamma=0;
                    for(int row=0;row<rows;row++){alpha+=a[row,p]*a[row,p];beta+=a[row,q]*a[row,q];gamma+=a[row,p]*a[row,q];}
                    if(alpha==0 || beta==0 || Math.Abs(gamma)<=1e-14*Math.Sqrt(alpha)*Math.Sqrt(beta))continue;
                    double zeta=(beta-alpha)/(2*gamma),absolute=Math.Abs(zeta);
                    double t=absolute>1e150?.5/zeta:(zeta>=0?1:-1)/(absolute+Math.Sqrt(1+zeta*zeta));
                    double cosine=1/Math.Sqrt(1+t*t),sine=cosine*t;
                    for(int row=0;row<rows;row++){double x=a[row,p],y=a[row,q];a[row,p]=cosine*x-sine*y;a[row,q]=sine*x+cosine*y;}
                    for(int row=0;row<columns;row++){double x=v[row,p],y=v[row,q];v[row,p]=cosine*x-sine*y;v[row,q]=sine*x+cosine*y;}
                    changed=true;
                }
                if(!changed){converged=true;break;}
            }
            var singular=new double[columns];
            for(int column=0;column<columns;column++)for(int row=0;row<rows;row++)singular[column]+=a[row,column]*a[row,column];
            for(int column=0;column<columns;column++)singular[column]=Math.Sqrt(singular[column]);
            double largest=singular.Max(),cutoff=Math.Max(1e-12,largest*relativeTolerance);var values=new double[columns];int rank=0;double smallest=double.PositiveInfinity;
            for(int column=0;column<columns;column++)if(singular[column]>cutoff)
            {
                rank++;smallest=Math.Min(smallest,singular[column]);double projection=0;
                for(int row=0;row<rows;row++)projection+=a[row,column]*target[row];
                projection/=singular[column]*singular[column];
                for(int i=0;i<columns;i++)values[i]+=v[i,column]*projection;
            }
            // Tiny rejected columns may continue exchanging numerical noise. Check orthogonality
            // only among retained directions before treating a fit as numerically unresolved.
            if(!converged)
            {
                converged=true;
                for(int p=0;p<columns-1;p++)for(int q=p+1;q<columns;q++)if(singular[p]>cutoff && singular[q]>cutoff)
                {
                    double dot=0;for(int row=0;row<rows;row++)dot+=a[row,p]*a[row,q];
                    if(Math.Abs(dot)>1e-9*singular[p]*singular[q])converged=false;
                }
            }
            foreach(double value in values)if(!Finite(value))throw Error("решение не является конечным");
            return new LeastSquares {Values=values,Singular=singular.OrderByDescending(s=>s).ToArray(),Rank=rank,
                Condition=rank==columns?largest/smallest:double.PositiveInfinity,Converged=converged};
        }

        private static Sample[] RequireSamples(IEnumerable<Sample> source)
        {
            if(source==null)throw Error("нет кадров для расчёта");var samples=source.ToArray();
            if(samples.Length==0)throw Error("набор кадров пуст");
            foreach(var sample in samples)
            {
                if(string.IsNullOrWhiteSpace(sample.Clip) || sample.Frame<0)throw Error("не указан клип или номер кадра");
                RequireMatrix(sample.Parent,sample.Clip+"/"+sample.Frame+"/parent");RequireMatrix(sample.Child,sample.Clip+"/"+sample.Frame+"/child");
            }
            return samples;
        }
        private static void RequireMatrix(Matrix4x4 matrix,string context)
        {
            for(int i=0;i<16;i++)if(!Finite(matrix[i]))throw Error("неконечная матрица "+context);
            double det=(double)matrix.m00*matrix.m11-(double)matrix.m01*matrix.m10;
            if(Math.Abs(det)<1e-12)throw Error("вырожденная 2D-матрица "+context);
            if(Math.Abs(matrix.m20)>1e-6 || Math.Abs(matrix.m21)>1e-6 || Math.Abs(matrix.m23)>1e-6 ||
                Math.Abs(matrix.m30)>1e-6 || Math.Abs(matrix.m31)>1e-6 || Math.Abs(matrix.m32)>1e-6 || Math.Abs(matrix.m33-1)>1e-6)
                throw Error("ожидалась плоская affine-матрица "+context);
        }
        private static Vector2 Point(double x,double y)
        {
            var result=new Vector2((float)x,(float)y);if(!Finite(result.x) || !Finite(result.y))throw Error("точка крепления выходит за конечный диапазон");return result;
        }
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
        private static InvalidOperationException Error(string reason)=>new("Поиск точки крепления: "+reason+".");
    }
}
