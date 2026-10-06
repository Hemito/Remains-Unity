using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    // Canvas-local geometry only. The caller supplies real widget and world bounds.
    public static class ContainerWindowLayout
    {
        public static bool TryPlace(Rect canvas,Vector2 size,Rect source,IReadOnlyList<Rect> reserved,int count,
            float anchorGap,float screenMargin,float clearance,out Rect placement)
        {
            placement=default;
            ValidateRect(canvas,"Canvas",false);ValidateRect(source,"источник",true);
            if(!Finite(size.x) || !Finite(size.y) || size.x<=0 || size.y<=0 ||
                !Nonnegative(anchorGap) || !Nonnegative(screenMargin) || !Nonnegative(clearance) ||
                count<0 || count>(reserved?.Count??0))
                throw new ArgumentException("Окно контейнера: неверные размеры, отступы или число препятствий.");
            for(int i=0;i<count;i++) ValidateRect(reserved[i],"препятствие "+i,true);
            var area=new Rect(canvas.xMin+screenMargin,canvas.yMin+screenMargin,
                canvas.width-2*screenMargin,canvas.height-2*screenMargin);
            if(size.x>area.width || size.y>area.height) return false;
            var preferred=new Vector2(source.center.x-size.x*.5f,source.yMax+anchorGap);
            int candidates=3+2*(count+1);
            // Above the selected source is the first policy. Only when that area is
            // unavailable may the nearest free side/below placement be considered.
            for(int pass=0;pass<2;pass++)
            {
                bool found=false;float distance=float.PositiveInfinity;
                for(int ix=0;ix<candidates;ix++)
                {
                    float x=Coordinate(ix,true,preferred,area,size,source,reserved,clearance);
                    for(int iy=0;iy<candidates;iy++)
                    {
                        float y=Coordinate(iy,false,preferred,area,size,source,reserved,clearance);
                        if(pass==0 && y<preferred.y) continue;
                        var candidate=new Rect(x,y,size.x,size.y);
                        if(Blocked(candidate,source,clearance)) continue;
                        bool blocked=false;
                        for(int i=0;i<count;i++) if(Blocked(candidate,reserved[i],clearance)) {blocked=true;break;}
                        if(blocked) continue;
                        float next=(new Vector2(x,y)-preferred).sqrMagnitude;
                        if(found && next>=distance) continue;
                        found=true;distance=next;placement=candidate;
                    }
                }
                if(found) return true;
            }
            return false;
        }
        private static float Coordinate(int index,bool horizontal,Vector2 preferred,Rect area,Vector2 size,
            Rect source,IReadOnlyList<Rect> reserved,float clearance)
        {
            float minimum=horizontal?area.xMin:area.yMin;
            float maximum=(horizontal?area.xMax:area.yMax)-(horizontal?size.x:size.y);
            if(index==0) return Mathf.Clamp(horizontal?preferred.x:preferred.y,minimum,maximum);
            if(index==1) return minimum;
            if(index==2) return maximum;
            int obstacle=(index-3)/2;var rect=obstacle==0?source:reserved[obstacle-1];
            float value=(index-3)%2==0?
                (horizontal?rect.xMin-size.x:rect.yMin-size.y)-clearance:
                (horizontal?rect.xMax:rect.yMax)+clearance;
            return Mathf.Clamp(value,minimum,maximum);
        }
        public static bool Blocked(Rect window,Rect obstacle,float clearance)=>
            window.xMin<obstacle.xMax+clearance && window.xMax>obstacle.xMin-clearance &&
            window.yMin<obstacle.yMax+clearance && window.yMax>obstacle.yMin-clearance;
        private static bool Finite(float value)=>float.IsFinite(value);
        private static bool Nonnegative(float value)=>Finite(value) && value>=0;
        private static void ValidateRect(Rect rect,string context,bool emptyAllowed)
        {
            if(!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) ||
                rect.width<0 || rect.height<0 || !emptyAllowed && (rect.width==0 || rect.height==0) ||
                !Finite(rect.xMax) || !Finite(rect.yMax))
                throw new ArgumentException("Окно контейнера: некорректный прямоугольник "+context+"="+rect+".");
        }
    }
}
