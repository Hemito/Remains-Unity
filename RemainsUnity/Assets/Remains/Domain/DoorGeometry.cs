using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    public static class DoorGeometry
    {
        // Explicit rebuild input for movement, shots and AI; no hidden collider
        // or renderer state. Call on topology changes, not in a physics loop.
        public static RoomSurface[] Compose(RoomSurface[] authored,IEnumerable<DoorState> doors,string level)
        {
            if(authored==null || doors==null || string.IsNullOrWhiteSpace(level)) throw new ArgumentException("Не заданы геометрия, двери или ID локации.");
            var result=new List<RoomSurface>(authored);
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var door in doors)
            {
                if(door==null) throw new InvalidOperationException("В списке дверей сохранена пустая запись.");
                door.Validate();
                if(!ids.Add(door.id)) throw new InvalidOperationException("Повторный ID двери: "+door.id);
                if(door.level!=level || door.open) continue;
                result.Add(new RoomSurface {x=door.x,y=door.y,width=door.width,height=door.height,texture=door.surfaceTexture});
            }
            return result.ToArray();
        }
    }
}
