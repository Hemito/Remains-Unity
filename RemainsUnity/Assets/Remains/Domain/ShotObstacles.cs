using UnityEngine;

namespace Remains.Domain
{
    public static class ShotObstacles
    {
        public static float Limit(RoomLayout room,Vector2 start,Vector2 end)
            =>FirstContact(room,start,end)??1;
        public static float? FirstContact(RoomLayout room,Vector2 start,Vector2 end)
            =>FirstContact(room,start,end,out _);
        public static float? FirstContact(RoomLayout room,Vector2 start,Vector2 end,out RoomSurface contacted)
        {
            contacted=null;
            float? nearest=null;
            foreach(var surface in room.platforms)
            {
                if(surface.shotsPassThrough) continue;
                var enter=SurfaceContact.Entry(surface,start,end);
                if(enter.HasValue && (!nearest.HasValue || enter.Value<nearest.Value)) {nearest=enter;contacted=surface;}
            }
            return nearest;
        }
    }
}
