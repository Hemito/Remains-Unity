using System;
using UnityEngine;

namespace Remains.Domain
{
    public static class ActorPositionRecovery
    {
        public static bool Repair(ActorState actor,RoomLayout room,string context)
        {
            bool finite=float.IsFinite(actor.x) && float.IsFinite(actor.y) && float.IsFinite(actor.vx) && float.IsFinite(actor.vy);
            float x=finite?Mathf.Clamp(actor.x,room.left,room.right):room.spawnX;
            if(!RoomSupport.TryLowestFloor(room,x,out float floor))
                throw new InvalidOperationException($"Нет нижнего пола для восстановления: actor={actor.id}, room={room.id}, x={x}, context={context}.");
            bool outsideX=!finite || actor.x<room.left || actor.x>room.right;
            bool belowFloor=!finite || actor.y<floor-RoomSupport.ContactTolerance;
            if(!outsideX && !belowFloor) return false;
            var oldPosition=actor.Position;
            var oldVelocity=actor.Velocity;
            actor.x=x;actor.y=floor;actor.vx=actor.vy=0;
            if(room.id=="hub-room-0-0" && outsideX) {actor.x=room.spawnX;actor.y=room.spawnY;}
            ActorPose.Select(actor,"idle");
            actor.revision++;
            Debug.LogWarning($"Восстановлена недопустимая позиция: actor={actor.id}, level={actor.level}, room={room.id}, position={oldPosition}, velocity={oldVelocity}, restored={actor.Position}, bounds=[{room.left};{room.right}], floor={floor}, context={context}. Здоровье, ранения и инвентарь сохранены.");
            return true;
        }
    }
}
