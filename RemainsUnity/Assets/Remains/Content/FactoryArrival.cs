using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // The hub enters the first starting room of the top band (0,0).
    // A source player marker is an arrival instruction, not a spawned extra player.
    public static class FactoryArrival
    {
        public static FactoryPlacedObject Require(PreparedFactory factory,IReadOnlyList<FactoryPlacedObject> objects)
        {
            FactoryPlacedObject result=null;
            foreach(var placement in objects)
            {
                var cell=placement.Room.Cell;
                if(cell.X!=0 || cell.Y!=0 || !cell.Start || placement.Source.Type!="remains:obj:player") continue;
                if(result!=null) throw new InvalidOperationException($"Завод {factory.Level}: несколько точек игрока в стартовой комнате 0,0: {result.Id}, {placement.Id}.");
                result=placement;
            }
            return result??throw new InvalidOperationException($"Завод {factory.Level}: нет исходной точки игрока в стартовой комнате 0,0.");
        }
        public static string Rejection(RoomLayout room,MovementHull hull,Vector2 position)
        {
            if(!ActorTerrainMotion.IsClear(room,hull,position))
                return $"Прибытие на {room.id}: точка ({position.x}, {position.y}) перекрыта или вне границ уровня.";
            if(!ActorTerrainMotion.Supported(room,hull,position))
                return $"Прибытие на {room.id}: точка ({position.x}, {position.y}) не имеет физической опоры.";
            return null;
        }
    }
}
