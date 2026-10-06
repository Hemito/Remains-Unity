using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Keeps the RoomLayout identity stable for controllers that retain it.
    // Only the derived obstacle array changes; authored surfaces are immutable input.
    internal sealed class RoomDoorGeometry
    {
        public RoomLayout Room {get;}
        public long OcclusionRevision {get;private set;}
        private readonly RoomSurface[] authored;
        private (float x,float y,float width,float height)[] publishedSolids;
        private (string id,bool blocks,float x,float y,float width,float height,string texture)[] previous;
        public RoomDoorGeometry(RoomLayout room)
        {
            Room=room??throw new ArgumentNullException(nameof(room));
            authored=room.platforms??throw new ArgumentException("У комнаты нет поверхностей.");
            publishedSolids=SolidShape(authored);
        }
        public bool Reconcile(IReadOnlyList<DoorState> doors,string level)
        {
            if(doors==null) throw new InvalidOperationException("В состоянии мира отсутствует список дверей.");
            var selected=new List<DoorState>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var door in doors)
            {
                if(door==null) throw new InvalidOperationException("В состоянии мира сохранена пустая дверь.");
                door.Validate();
                if(!ids.Add(door.id)) throw new InvalidOperationException("Повторный ID двери: "+door.id);
                if(door.level==level) selected.Add(door);
            }
            var shape=selected.Select(d=>(d.id,d.Blocks,d.x,d.y,d.width,d.height,d.surfaceTexture)).ToArray();
            if(previous!=null && previous.SequenceEqual(shape)) return false;
            Room.platforms=selected.Count==0?authored:DoorGeometry.Compose(authored,selected,level);
            previous=shape;
            var solids=SolidShape(Room.platforms);
            if(publishedSolids.SequenceEqual(solids)) return false;
            publishedSolids=solids;OcclusionRevision++;return true;
        }
        // Compare actual composed obstacles, independent of door IDs, texture, HP or list order.
        private static (float x,float y,float width,float height)[] SolidShape(RoomSurface[] surfaces)
            =>surfaces.Where(surface=>!surface.oneWay).Select(surface=>(surface.x,surface.y,surface.width,surface.height))
                .OrderBy(shape=>shape.x).ThenBy(shape=>shape.y).ThenBy(shape=>shape.width).ThenBy(shape=>shape.height).ToArray();
    }
}
