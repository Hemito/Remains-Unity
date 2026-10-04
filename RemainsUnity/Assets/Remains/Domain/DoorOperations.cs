using System;
using UnityEngine;

namespace Remains.Domain
{
    public interface IDoorAccessRule
    {
        string Rejection(ActorState actor,DoorState door);
    }
    public sealed class NearbyDoorAccess : IDoorAccessRule
    {
        private readonly float distance;
        public NearbyDoorAccess(float distance=2)
        {
            if(!float.IsFinite(distance) || distance<=0) throw new ArgumentOutOfRangeException(nameof(distance));
            this.distance=distance;
        }
        public string Rejection(ActorState actor,DoorState door)
        {
            if(actor==null || !actor.connected || !actor.player) return "Для взаимодействия нужен подключённый игрок.";
            if(actor.down) return "Без сознания нельзя открыть дверь.";
            if(actor.level!=door.level) return "Дверь находится в другой локации.";
            if(!float.IsFinite(actor.x) || !float.IsFinite(actor.y) || door.DistanceTo(actor.Position)>distance)
                return "Подойдите ближе к двери.";
            return null;
        }
    }
    public enum DoorChange { Rejected, Unchanged, Changed }
    public static class DoorOperations
    {
        // The caller coordinates requests, pause and occupancy. This pure domain
        // operation never assumes that the host simulates player movement.
        public static DoorChange SetOpen(DoorState door,ActorState actor,long expectedRevision,bool desiredOpen,
            IDoorAccessRule access,bool closingVolumeOccupied,out string reason)
        {
            if(door==null) {reason="Дверь больше недоступна.";return DoorChange.Rejected;}
            door.Validate();
            if(access==null) throw new ArgumentNullException(nameof(access));
            reason=access.Rejection(actor,door);
            if(reason!=null) return DoorChange.Rejected;
            if(expectedRevision!=door.revision)
            {reason="Состояние двери уже изменилось. Повторите взаимодействие.";return DoorChange.Rejected;}
            if(door.open==desiredOpen) return DoorChange.Unchanged;
            if(!desiredOpen && closingVolumeOccupied)
            {reason="Проём занят: дверь нельзя закрыть.";return DoorChange.Rejected;}
            if(door.revision==long.MaxValue) throw new InvalidOperationException($"Исчерпан счётчик изменений двери {door.id}.");
            door.open=desiredOpen;door.revision++;return DoorChange.Changed;
        }
    }
}
