using System;
using UnityEngine;

namespace Remains.Editor
{
    // Three independent correspondences represent a whole affine part, not a recovered skeleton.
    public static class PonyAffinePoints
    {
        public static Vector2[] Map(Matrix4x4 matrix,Vector2[] points)
        {
            Require(points);var output=new Vector2[3];
            for(int i=0;i<3;i++)output[i]=matrix.MultiplyPoint3x4(points[i]);return output;
        }
        public static bool TrySolve(Vector2[] source,Vector2[] target,out Matrix4x4 matrix,out string reason)
        {
            matrix=Matrix4x4.identity;reason=null;
            if(!Valid(source) || !Valid(target)){reason="Нужны три конечные точки источника и результата.";return false;}
            double ux=(double)source[1].x-source[0].x,uy=(double)source[1].y-source[0].y;
            double vx=(double)source[2].x-source[0].x,vy=(double)source[2].y-source[0].y,det=ux*vy-uy*vx;
            if(Math.Abs(det)<1e-10){reason="Исходные опоры лежат на одной прямой.";return false;}
            double ax=(double)target[1].x-target[0].x,ay=(double)target[1].y-target[0].y;
            double bx=(double)target[2].x-target[0].x,by=(double)target[2].y-target[0].y;
            matrix.m00=(float)((ax*vy-bx*uy)/det);matrix.m01=(float)((bx*ux-ax*vx)/det);
            matrix.m10=(float)((ay*vy-by*uy)/det);matrix.m11=(float)((by*ux-ay*vx)/det);
            matrix.m03=(float)(target[0].x-(double)matrix.m00*source[0].x-(double)matrix.m01*source[0].y);
            matrix.m13=(float)(target[0].y-(double)matrix.m10*source[0].x-(double)matrix.m11*source[0].y);
            for(int i=0;i<16;i++)if(!float.IsFinite(matrix[i])){reason="Преобразование опор вышло за конечный диапазон.";return false;}
            return true;
        }
        // Editing policy is separate from the exact representation: original reflected matrices remain legal.
        public static bool AcceptEdit(Matrix4x4 delta,out string reason)
        {
            double a=delta.m00,b=delta.m01,c=delta.m10,d=delta.m11,det=a*d-b*c;
            double sum=a*a+b*b+c*c+d*d,disc=Math.Sqrt(Math.Max(0,sum*sum-4*det*det));
            double large=Math.Sqrt(Math.Max(0,(sum+disc)/2)),small=Math.Sqrt(Math.Max(0,(sum-disc)/2));
            reason=det<=.2?"Деталь слишком сплющена или вывернута.":small<.4 || large>2.2?"Изменение размера превышает пределы этой пробы (0,4–2,2).":null;
            return reason==null;
        }
        public static Matrix4x4 Solve(Vector2[] source,Vector2[] target)
        {
            if(!TrySolve(source,target,out var result,out var reason))throw new InvalidOperationException("Три опоры: "+reason);return result;
        }
        private static bool Valid(Vector2[] points)=>points?.Length==3 && Array.TrueForAll(points,p=>float.IsFinite(p.x)&&float.IsFinite(p.y));
        private static void Require(Vector2[] points){if(!Valid(points))throw new InvalidOperationException("Три опоры: повреждён набор точек.");}
    }
}
