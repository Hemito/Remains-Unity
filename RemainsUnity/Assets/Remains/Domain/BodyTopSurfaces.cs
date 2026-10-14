using System.Collections.Generic;

namespace Remains.Domain
{
    // Tops of physical bodies (boxes, containers, furniture) as one-way supports for actor movement only
    // (ActorTerrainMotion): actors land on them from above, walk off and drop through; bodies never block actors
    // from the side or below. Shots, melee, NPC door logic and the bodies' own physics do not see them.
    // Rebuilt once per frame before movement, reusing its surface objects (no allocation once warmed up).
    public static class BodyTopSurfaces
    {
        public const float Thickness=.1f;

        public static void Update(RoomLayout room,WorldState world,string level)
        {
            if(room==null) return;
            var surfaces=room.bodySurfaces??=new List<RoomSurface>();
            int count=0;
            if(world!=null)
            {
                count=Add(surfaces,count,world.props,level);
                count=Add(surfaces,count,world.containers,level);
            }
            if(surfaces.Count>count) surfaces.RemoveRange(count,surfaces.Count-count);
        }

        private static int Add<T>(List<RoomSurface> surfaces,int count,List<T> bodies,string level) where T:PropState
        {
            if(bodies==null) return count;
            foreach(var body in bodies)
            {
                // A held (telekinesis/carried) body is not a support.
                if(body==null || !body.hasPhysicalBody || body.held || body.level!=level || body.width<=0 || body.height<=0) continue;
                RoomSurface surface;
                if(count<surfaces.Count) surface=surfaces[count];
                else {surface=new RoomSurface();surfaces.Add(surface);}
                float top=body.y-body.anchorHeight+body.height;
                surface.x=body.x-body.width*.5f;surface.y=top-Thickness;surface.width=body.width;surface.height=Thickness;
                surface.oneWay=true;surface.slopeDirection=0;surface.texture=null;surface.movementSound=null;surface.sourceDoorId=null;surface.shotsPassThrough=true;
                count++;
            }
            return count;
        }
    }
}
