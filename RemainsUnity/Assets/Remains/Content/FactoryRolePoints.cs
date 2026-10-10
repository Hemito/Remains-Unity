using System;
using System.Collections.Generic;
using System.Linq;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    public sealed class FactoryRolePoint
    {
        public string Id {get;}
        public FactoryPlacedObject Marker {get;}
        public FactoryCell Cell=>Marker.Room.Cell;
        public Vector2 Position=>Marker.Position;
        internal FactoryRolePoint(string id,FactoryPlacedObject marker) {Id=id;Marker=marker;}
    }
    public static class FactoryRolePoints
    {
        public static FactoryRolePoint[] Read(PreparedFactory factory,IReadOnlyList<FactoryPlacedObject> objects)
        {
            if(factory==null || objects==null) throw new ArgumentNullException(factory==null?nameof(factory):nameof(objects));
            var ids=new HashSet<string>(objects.Select(obj=>obj.Id),StringComparer.Ordinal);
            var roles=new List<FactoryRolePoint>();
            foreach(var cell in factory.Grid.Layout.Cells)
            {
                if(cell.Role==FactoryCellRole.None) continue;
                var markers=objects.Where(obj=>ReferenceEquals(obj.Room.Cell,cell) && obj.Source.Id==cell.RolePoint && obj.Source.Type=="remains:obj:player").ToArray();
                if(markers.Length!=1) throw new InvalidOperationException($"Завод {factory.Level}, клетка {cell.X},{cell.Y}: не разрешён маркер роли {cell.RolePoint}.");
                string id=$"{factory.Level}:{cell.X},{cell.Y}:role:{cell.RolePoint}";
                if(!ids.Add(id)) throw new InvalidOperationException("Повтор мирового ID роли завода: "+id);
                roles.Add(new FactoryRolePoint(id,markers[0]));
            }
            return roles.ToArray();
        }
    }
}
