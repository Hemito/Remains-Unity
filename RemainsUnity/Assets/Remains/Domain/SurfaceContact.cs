using UnityEngine;

namespace Remains.Domain
{
    // Shared segment/rectangle intersection; callers choose which surfaces block their query.
    public static class SurfaceContact
    {
        public static float? Entry(RoomSurface surface,Vector2 start,Vector2 end,float radius=0)
            =>Entry(new Rect(surface.x,surface.y,surface.width,surface.height),start,end,radius);
        public static float? Entry(Rect bounds,Vector2 start,Vector2 end,float radius=0)
        {
            float left=bounds.xMin,right=bounds.xMax,bottom=bounds.yMin,top=bounds.yMax;
            if(radius<=0) return RectangleEntry(start,end,left,right,bottom,top);
            // Swept circle against a rectangle: two edge strips and four rounded corners.
            // Expanding only the bounding box would report false hits outside the corners.
            float? nearest=RectangleEntry(start,end,left-radius,right+radius,bottom,top);
            Include(RectangleEntry(start,end,left,right,bottom-radius,top+radius));
            Include(HitGeometry.CircleEntry(new Vector2(left,bottom),radius,start,end));
            Include(HitGeometry.CircleEntry(new Vector2(left,top),radius,start,end));
            Include(HitGeometry.CircleEntry(new Vector2(right,bottom),radius,start,end));
            Include(HitGeometry.CircleEntry(new Vector2(right,top),radius,start,end));
            return nearest;
            void Include(float? value) {if(value.HasValue && (!nearest.HasValue || value.Value<nearest.Value)) nearest=value;}
        }
        private static float? RectangleEntry(Vector2 start,Vector2 end,float left,float right,float bottom,float top)
        {
            float enter=0,exit=1;var delta=end-start;
            return Clip(start.x,delta.x,left,right,ref enter,ref exit) &&
                Clip(start.y,delta.y,bottom,top,ref enter,ref exit)?enter:null;
        }
        private static bool Clip(float start,float delta,float min,float max,ref float enter,ref float exit)
        {
            if(delta==0) return start>=min && start<=max;
            float a=(min-start)/delta,b=(max-start)/delta;
            enter=Mathf.Max(enter,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));
            return enter<=exit;
        }
    }
    public static class RoomVisibility
    {
        public static bool Clear(RoomLayout room,Vector2 start,Vector2 end)
        {
            foreach(var surface in room.platforms)
                if(!surface.oneWay && SurfaceContact.Entry(surface,start,end).HasValue) return false;
            return true;
        }
    }
}
