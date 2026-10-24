using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content.VectorArt
{
    // CPU geometry of RVG paths in output pixels (Y down): curve flattening, stencil fans for fills and triangles for
    // strokes. The GPU side (RvgRasterizer) resolves coverage with the stencil, so fans may overlap and self-intersect.
    public static class RvgGeometry
    {
        public const float Tolerance=.1f;   // output pixels: largest distance between a curve and its segments

        public sealed class Contour {public readonly List<Vector2> Points=new();public bool Closed;}
        // Contours of a path transformed by m (twips → output pixels); a closed contour repeats no end point.
        public static List<Contour> Contours(int[] commands,in Affine m)
        {
            var contours=new List<Contour>();Contour current=null;Vector2 last=default,start=default;
            void Finish(bool closed)
            {
                if(current==null) return;
                var points=current.Points;
                if(closed && points.Count>1 && (points[^1]-points[0]).sqrMagnitude<1e-10f) points.RemoveAt(points.Count-1);
                current.Closed=closed;
                if(points.Count>1) contours.Add(current);
                current=null;
            }
            int i=0;
            while(i<commands.Length)
            {
                switch(commands[i])
                {
                    case 0:
                        Finish(false);
                        start=last=m.Apply(commands[i+1],commands[i+2]);current=new Contour();current.Points.Add(last);i+=3;break;
                    case 1:
                        last=m.Apply(commands[i+1],commands[i+2]);current.Points.Add(last);i+=3;break;
                    case 2:
                    {
                        Vector2 c=m.Apply(commands[i+1],commands[i+2]),end=m.Apply(commands[i+3],commands[i+4]);
                        float deviation=(last-2*c+end).magnitude*.25f;
                        int segments=Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(deviation/Tolerance)),1,256);
                        for(int s=1;s<=segments;s++)
                        {
                            float t=s/(float)segments,u=1-t;
                            current.Points.Add(u*u*last+2*u*t*c+t*t*end);
                        }
                        last=end;i+=5;break;
                    }
                    case 3:
                        current?.Points.Add(start);
                        Finish(true);last=start;i+=1;break;
                    default: throw new InvalidOperationException("RVG: неизвестная команда пути "+commands[i]);
                }
            }
            Finish(false);
            return contours;
        }

        // Triangle fans of every contour (any fan point works for stencil parity) and the bounds to cover.
        // A fill closes every contour (an open fill contour is closed implicitly, as in SWF).
        public static void Fans(List<Contour> contours,List<Vector3> vertices,List<int> indices,ref Rect bounds)
        {
            foreach(var contour in contours)
            {
                var points=contour.Points;
                if(points.Count<3) continue;
                int first=vertices.Count;
                foreach(var p in points) {vertices.Add(p);Grow(ref bounds,p);}
                for(int k=1;k<points.Count-1;k++) {indices.Add(first);indices.Add(first+k);indices.Add(first+k+1);}
            }
        }

        // Stroke triangles of the contours at width (output pixels). Overlaps are fine: strokes are stencil-replaced.
        public static void Stroke(List<Contour> contours,float width,RvgLine style,
            List<Vector3> vertices,List<int> indices,ref Rect bounds)
        {
            float h=width*.5f;
            int roundSegments=Mathf.Clamp(Mathf.CeilToInt(Mathf.PI/Mathf.Acos(Mathf.Clamp(1-Tolerance/Mathf.Max(h,1e-3f),-1,1))),4,64);
            foreach(var item in contours)
            {
                var contour=item.Points;
                bool isClosed=item.Closed && !style.noClose;
                int count=contour.Count;
                int segmentCount=isClosed?count:count-1;
                for(int s=0;s<segmentCount;s++)
                {
                    Vector2 a=contour[s],b=contour[(s+1)%count];
                    Vector2 d=b-a;float length=d.magnitude;if(length<1e-6f) continue;
                    Vector2 dir=d/length,n=new(-dir.y,dir.x);
                    if(!isClosed && s==0 && style.startCap=="square") a-=dir*h;
                    if(!isClosed && s==segmentCount-1 && style.endCap=="square") b+=dir*h;
                    Quad(a+n*h,b+n*h,b-n*h,a-n*h,vertices,indices,ref bounds);
                }
                for(int v=0;v<count;v++)
                {
                    bool end=!isClosed && (v==0 || v==count-1);
                    if(end)
                    {
                        string cap=v==0?style.startCap:style.endCap;
                        if(cap=="round") Circle(contour[v],h,roundSegments,vertices,indices,ref bounds);
                        continue;
                    }
                    Vector2 prev=contour[(v-1+count)%count],p=contour[v],next=contour[(v+1)%count];
                    Join(prev,p,next,h,style,roundSegments,vertices,indices,ref bounds);
                }
            }
        }
        private static void Join(Vector2 prev,Vector2 p,Vector2 next,float h,RvgLine style,int roundSegments,List<Vector3> vertices,List<int> indices,ref Rect bounds)
        {
            if(style.join=="round") {Circle(p,h,roundSegments,vertices,indices,ref bounds);return;}
            Vector2 d1=(p-prev),d2=(next-p);
            if(d1.sqrMagnitude<1e-12f || d2.sqrMagnitude<1e-12f) return;
            d1.Normalize();d2.Normalize();
            Vector2 n1=new(-d1.y,d1.x),n2=new(-d2.y,d2.x);
            float turn=d1.x*d2.y-d1.y*d2.x;
            if(Mathf.Abs(turn)<1e-6f) return;
            float side=turn>0?-1:1;   // the outer side of the turn
            Vector2 o1=p+n1*h*side,o2=p+n2*h*side;
            Triangle(p,o1,o2,vertices,indices,ref bounds);
            if(style.join!="miter") return;
            Vector2 miter=(n1+n2);float ml=miter.magnitude;if(ml<1e-6f) return;
            miter/=ml;float cos=Vector2.Dot(miter,n1);if(cos<1e-6f) return;
            float miterLength=h/cos;
            if(miterLength/h>style.miterLimit) return;   // beyond the limit: the bevel above
            Triangle(o1,p+miter*miterLength*side,o2,vertices,indices,ref bounds);
        }
        private static void Circle(Vector2 c,float r,int segments,List<Vector3> vertices,List<int> indices,ref Rect bounds)
        {
            int first=vertices.Count;vertices.Add(c);Grow(ref bounds,c);
            for(int k=0;k<segments;k++)
            {
                float angle=k*2*Mathf.PI/segments;var p=c+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*r;
                vertices.Add(p);Grow(ref bounds,p);
            }
            for(int k=0;k<segments;k++) {indices.Add(first);indices.Add(first+1+k);indices.Add(first+1+(k+1)%segments);}
        }
        private static void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,List<Vector3> vertices,List<int> indices,ref Rect bounds)
        {
            int first=vertices.Count;
            vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
            Grow(ref bounds,a);Grow(ref bounds,b);Grow(ref bounds,c);Grow(ref bounds,d);
            indices.Add(first);indices.Add(first+1);indices.Add(first+2);indices.Add(first);indices.Add(first+2);indices.Add(first+3);
        }
        private static void Triangle(Vector2 a,Vector2 b,Vector2 c,List<Vector3> vertices,List<int> indices,ref Rect bounds)
        {
            int first=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
            Grow(ref bounds,a);Grow(ref bounds,b);Grow(ref bounds,c);
            indices.Add(first);indices.Add(first+1);indices.Add(first+2);
        }
        public static void Grow(ref Rect bounds,Vector2 p)
        {
            if(bounds.width<0) {bounds=new Rect(p.x,p.y,0,0);return;}
            bounds=Rect.MinMaxRect(Mathf.Min(bounds.xMin,p.x),Mathf.Min(bounds.yMin,p.y),Mathf.Max(bounds.xMax,p.x),Mathf.Max(bounds.yMax,p.y));
        }
        public static Rect EmptyBounds=>new(0,0,-1,-1);
    }

    // 2D affine transform in the SWF layout: x' = a·x + c·y + tx, y' = b·x + d·y + ty.
    public readonly struct Affine
    {
        public readonly float a,b,c,d,tx,ty;
        public Affine(float a,float b,float c,float d,float tx,float ty) {this.a=a;this.b=b;this.c=c;this.d=d;this.tx=tx;this.ty=ty;}
        public static Affine From(float[] m)=>new(m[0],m[1],m[2],m[3],m[4],m[5]);
        public static readonly Affine Identity=new(1,0,0,1,0,0);
        public static Affine Scale(float s)=>new(s,0,0,s,0,0);
        public static Affine Translate(float x,float y)=>new(1,0,0,1,x,y);
        public Vector2 Apply(float x,float y)=>new(a*x+c*y+tx,b*x+d*y+ty);
        // this ∘ inner: first inner, then this.
        public Affine Then(in Affine outer)=>new(outer.a*a+outer.c*b,outer.b*a+outer.d*b,outer.a*c+outer.c*d,outer.b*c+outer.d*d,
            outer.a*tx+outer.c*ty+outer.tx,outer.b*tx+outer.d*ty+outer.ty);
        public float Determinant=>a*d-b*c;
        public Affine Inverse()
        {
            float det=Determinant;
            if(Mathf.Abs(det)<1e-20f) return new Affine(0,0,0,0,0,0);
            float ia=d/det,ib=-b/det,ic=-c/det,id=a/det;
            return new Affine(ia,ib,ic,id,-(ia*tx+ic*ty),-(ib*tx+id*ty));
        }
    }
}
