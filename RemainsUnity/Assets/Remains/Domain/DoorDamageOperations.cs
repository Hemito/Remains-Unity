using System;

namespace Remains.Domain
{
    // The coordinator chooses the amount and commits the world. This operation has
    // no weapon formulas, ownership, transport or visual/audio side effects.
    public static class DoorDamageOperations
    {
        public static DoorChange Apply(DoorState door,string attackId,float amount,out string reason)
        {
            if(door==null) {reason="Проём больше недоступен.";return DoorChange.Rejected;}
            door.Validate();
            if(string.IsNullOrWhiteSpace(attackId) || !float.IsFinite(amount) || amount<=0)
            {reason="Повреждение проёма требует ID атаки и конечное положительное значение.";return DoorChange.Rejected;}
            if(door.integrity==null)
            {reason="Этот проём явно неразрушаемый.";return DoorChange.Rejected;}
            if(door.integrity.resolvedAttacks.Contains(attackId)) {reason=null;return DoorChange.Unchanged;}
            if(door.Destroyed) {reason="Проём уже разрушен.";return DoorChange.Rejected;}
            if(door.revision==long.MaxValue) throw new InvalidOperationException($"Исчерпан счётчик изменений проёма {door.id}.");
            door.integrity.health=Math.Max(0,door.integrity.health-amount);
            door.integrity.resolvedAttacks.Add(attackId);door.revision++;
            reason=null;return DoorChange.Changed;
        }
    }
}
