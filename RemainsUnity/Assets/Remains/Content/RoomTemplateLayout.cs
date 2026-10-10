using System;
using System.Linq;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // Adapter: room template grid -> current RoomLayout physics (spec §5). Geometry rules
    // live in RoomGeometryBuilder; this only maps its neutral output onto Domain types.
    public static class RoomTemplateLayout
    {
        public static RoomLayout Create(RoomTemplateAsset template,RoomDefinitionCatalog definitions)
        {
            if(template==null) throw new ArgumentNullException(nameof(template));
            return Create(template.Room,definitions);
        }
        public static RoomLayout Create(RoomFile room,RoomDefinitionCatalog definitions)
        {
            if(room==null) throw new ArgumentNullException(nameof(room));
            if(definitions==null) throw new ArgumentNullException(nameof(definitions));
            var geometry=RoomGeometryBuilder.Build(room,definitions);
            foreach(string issue in geometry.Issues) Debug.LogWarning($"Шаблон комнаты {room.Template}: {issue}");
            return new RoomLayout
            {
                id=room.Template,left=0,right=(float)geometry.Width,bottom=0,top=(float)geometry.Height,
                platforms=geometry.Surfaces.Select(s=>new RoomSurface
                {
                    x=(float)s.X,y=(float)s.Y,width=(float)s.Width,height=(float)s.Height,texture=s.Definition,
                    movementSound=s.MovementSound,oneWay=s.OneWay,shotsPassThrough=s.ShotsPassThrough,slopeDirection=s.Slope,
                }).ToArray(),
                ladders=geometry.Ladders.Select(l=>new RoomLadder
                {
                    x=(float)l.X,bottom=(float)l.Bottom,top=(float)l.Top,facing=l.Facing<0?LadderFacing.Left:LadderFacing.Right,
                    movementOffset=(float)l.MovementOffset,artworkOffset=(float)l.ArtworkOffset,texture=l.Definition,
                }).ToArray(),
            };
        }
    }
}
