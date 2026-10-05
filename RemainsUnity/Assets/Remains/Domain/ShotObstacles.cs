using UnityEngine;

namespace Remains.Domain
{
    public static class ShotObstacles
    {
        public static float Limit(RoomLayout room,Vector2 start,Vector2 end)
            =>FirstContact(room,start,end)??1;
        public static float? FirstContact(RoomLayout room,Vector2 start,Vector2 end)
            =>FirstContact(room,start,end,out _);
        public static float? FirstContact(RoomLayout room,Vector2 start,Vector2 end,out RoomSurface contacted,float radius=0)
        {
            contacted=null;
            float? nearest=null;
            foreach(var surface in RoomSurfaces.All(room))
            {
                if(surface.shotsPassThrough) continue;
                var enter=SurfaceContact.Entry(surface,start,end,radius);
                if(enter.HasValue && (!nearest.HasValue || enter.Value<nearest.Value)) {nearest=enter;contacted=surface;}
            }
            return nearest;
        }
    }
}
