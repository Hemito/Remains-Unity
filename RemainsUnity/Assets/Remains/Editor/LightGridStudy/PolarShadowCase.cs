using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Editor
{
    // One geometry source for the CPU oracle, GPU contours, silhouettes and coarse fill blockers.
    internal sealed class PolarShadowCase
    {
        internal const float Width=32,Height=18,Radius=15,Cell=.2f;
        internal readonly string Id,Title,Description;
        internal readonly Vector2 Source;
        internal readonly List<Vector4> Segments=new();
        internal PolarShadowCase(string id,string title,string description,Vector2 source)
        {Id=id;Title=title;Description=description;Source=source;}
        private PolarShadowCase Block(float x,float y,float width,float height,float degrees=0)
        {
            float a=degrees*Mathf.Deg2Rad,c=Mathf.Cos(a),s=Mathf.Sin(a);
            var corners=new Vector2[4];
            for(int i=0;i<4;i++)
            {
                float px=(i==0 || i==3?-.5f:.5f)*width,py=(i<2?-.5f:.5f)*height;
                corners[i]=new Vector2(x+c*px-s*py,y+s*px+c*py);
            }
            for(int i=0;i<4;i++) {var p=corners[i];var q=corners[(i+1)%4];Segments.Add(new Vector4(p.x,p.y,q.x,q.y));}
            return this;
        }
        internal static PolarShadowCase[] Create()=>new[]
        {
            new PolarShadowCase("diagonal","Диагональные тени","Наклонная преграда и отдельные блоки; смотрим форму края в свободном пространстве.",new Vector2(6,8))
                .Block(11,10,.35f,3,25).Block(10,4,2,.4f).Block(16,14,1,1),
            new PolarShadowCase("thin-far","Тонкая преграда и дальняя тень","Пластина толщиной 1 см, обычная балка и дальний край радиуса 15 м. Ширина деталей не привязана к угловым отсчётам.",new Vector2(5,9))
                .Block(10,9,.01f,1.4f,28).Block(11,4,2,.2f,-15).Block(8,13,.3f,.3f),
            new PolarShadowCase("seam","Стык 0° / 360°","Луч вправо от лампы пересекает преграду ровно возле циклической границы угловой строки.",new Vector2(6,9))
                .Block(10,9,.25f,1.8f).Block(13,13,1,.5f,20),
            new PolarShadowCase("near-wall","Лампа в 2 см от стены","Лампа снаружи стены: отступ 2 см. Не проверяет источник, утопленный внутрь материала — это отдельный случай.",new Vector2(4.02f,9))
                .Block(3.75f,9,.5f,8).Block(10,11,.35f,2,-25).Block(9,5,1,.35f)
        };
        internal bool Contains(Vector2 p)
        {
            bool inside=false;
            foreach(var e in Segments)
                if((e.y>p.y)!=(e.w>p.y) && p.x<(double)(e.z-e.x)*(p.y-e.y)/(e.w-e.y)+e.x) inside=!inside;
            return inside;
        }
        internal double Nearest(double dx,double dy)=>Nearest(Source,dx,dy);
        internal double Nearest(Vector2 source,double dx,double dy)
        {
            double nearest=Radius;
            foreach(var e in Segments)
            {
                double ex=e.z-e.x,ey=e.w-e.y,px=e.x-source.x,py=e.y-source.y;
                double denominator=dx*ey-dy*ex;if(Math.Abs(denominator)<1e-10) continue;
                double t=(px*ey-py*ex)/denominator,u=(px*dy-py*dx)/denominator;
                if(t>1e-5 && u>=0 && u<=1) nearest=Math.Min(nearest,t);
            }
            return nearest;
        }
        internal GridLightField Fill()
        {
            const int width=160,height=90;var absorption=new float[width*height];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                // Conservative segment/box intersection, not sparse samples that can miss thin blockers.
                var r=new Rect(x*Cell+1e-5f,y*Cell+1e-5f,Cell-2e-5f,Cell-2e-5f);
                bool solid=Contains(r.center) || Contains(r.min) || Contains(r.max) ||
                    Contains(new Vector2(r.xMin,r.yMax)) || Contains(new Vector2(r.xMax,r.yMin));
                if(!solid) foreach(var e in Segments) if(Touches(e,r)) {solid=true;break;}
                absorption[y*width+x]=solid?1:0;
            }
            int seed=Mathf.FloorToInt(Source.y/Cell)*width+Mathf.FloorToInt(Source.x/Cell);
            if(absorption[seed]>=.99f) throw new InvalidOperationException($"Проба полярных теней: источник {Id} попал в закрытую клетку заполнения {seed}.");
            var field=new GridLightField(width,height,Cell,absorption);
            field.Solve(new[]{new GridLightSeed(seed,new Color(1,.87f,.69f))});return field;
        }
        private static bool Touches(Vector4 e,Rect r)
        {
            double first=0,last=1,dx=e.z-e.x,dy=e.w-e.y;
            return Clip(-dx,e.x-r.xMin,ref first,ref last) && Clip(dx,r.xMax-e.x,ref first,ref last) &&
                Clip(-dy,e.y-r.yMin,ref first,ref last) && Clip(dy,r.yMax-e.y,ref first,ref last);
        }
        private static bool Clip(double p,double q,ref double first,ref double last)
        {
            if(Math.Abs(p)<1e-12) return q>=0;
            double t=q/p;
            if(p<0) {if(t>last) return false;first=Math.Max(first,t);}
            else {if(t<first) return false;last=Math.Min(last,t);}return true;
        }
    }
}
