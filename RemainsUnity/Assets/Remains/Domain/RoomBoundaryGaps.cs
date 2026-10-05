using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Domain
{
    // Explicit authoring calculation. One-way shelves are not an exterior wall.
    public static class RoomBoundaryGaps
    {
        private const float Epsilon=.0001f;
        public static RoomSurface[] Find(RoomLayout room,float thickness,string texture,string movementSound)
        {
            if(room?.platforms==null || !float.IsFinite(thickness) || thickness<=0 ||
                room.right-room.left<=2*thickness || room.top-room.bottom<=2*thickness || string.IsNullOrWhiteSpace(texture))
                throw new ArgumentException("Некорректный контур комнаты или толщина внешней стены.");
            var solid=room.platforms.Where(s=>!s.oneWay && s.slopeDirection==0).ToArray();
            var result=new List<RoomSurface>();
            Add(room.bottom+thickness,room.top-thickness,
                solid.Where(s=>s.x<=room.left+Epsilon && s.x+s.width>=room.left+thickness-Epsilon)
                    .Select(s=>new Vector2(s.y,s.y+s.height)),
                (a,b)=>new RoomSurface {x=room.left,y=a,width=thickness,height=b-a});
            Add(room.bottom+thickness,room.top-thickness,
                solid.Where(s=>s.x<=room.right-thickness+Epsilon && s.x+s.width>=room.right-Epsilon)
                    .Select(s=>new Vector2(s.y,s.y+s.height)),
                (a,b)=>new RoomSurface {x=room.right-thickness,y=a,width=thickness,height=b-a});
            Add(room.left,room.right,
                solid.Where(s=>s.y<=room.bottom+Epsilon && s.y+s.height>=room.bottom+thickness-Epsilon)
                    .Select(s=>new Vector2(s.x,s.x+s.width)),
                (a,b)=>new RoomSurface {x=a,y=room.bottom,width=b-a,height=thickness});
            Add(room.left,room.right,
                solid.Where(s=>s.y<=room.top-thickness+Epsilon && s.y+s.height>=room.top-Epsilon)
                    .Select(s=>new Vector2(s.x,s.x+s.width)),
                (a,b)=>new RoomSurface {x=a,y=room.top-thickness,width=b-a,height=thickness});
            return result.ToArray();

            void Add(float start,float end,IEnumerable<Vector2> coverage,Func<float,float,RoomSurface> create)
            {
                float cursor=start;
                foreach(var interval in coverage.OrderBy(value=>value.x).ThenBy(value=>value.y))
                {
                    float left=Mathf.Clamp(interval.x,start,end),right=Mathf.Clamp(interval.y,start,end);
                    if(right<=cursor+Epsilon) continue;
                    if(left>cursor+Epsilon) Append(cursor,left);
                    cursor=Mathf.Max(cursor,right);
                }
                if(cursor<end-Epsilon) Append(cursor,end);
                void Append(float a,float b)
                {var surface=create(a,b);surface.texture=texture;surface.movementSound=movementSound;result.Add(surface);}
            }
        }
    }
}
