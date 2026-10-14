using UnityEngine;

namespace Remains.Domain
{
    public static class SurfaceContactSound
    {
        // Prefer the support carrying the largest part of the movement hull.
        public static string Resolve(RoomLayout room,MovementHull hull,Vector2 position)
        {
            RoomSurface selected=null;float largest=0;
            foreach(var surface in RoomSurfaces.NearForActors(room,Rect.MinMaxRect(position.x-hull.width*.5f,position.y-RoomSupport.ContactTolerance,
                position.x+hull.width*.5f,position.y+RoomSupport.ContactTolerance)))
            {
                if(Mathf.Abs(position.y-RoomSupport.Top(surface,position.x,hull.width))>RoomSupport.ContactTolerance) continue;
                float overlap=Mathf.Min(position.x+hull.width*.5f,surface.x+surface.width)-Mathf.Max(position.x-hull.width*.5f,surface.x);
                if(overlap<=0) continue;
                if(selected==null || overlap>largest || overlap==largest && surface.x<selected.x)
                {selected=surface;largest=overlap;}
            }
            if(selected==null) return null;
            return string.IsNullOrEmpty(selected.movementSound)?room.movementSound:selected.movementSound;
        }
    }
}
