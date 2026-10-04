namespace Remains.Domain
{
    public static class DoorRequestRules
    {
        // Actor must be resolved by the transport from the authenticated connection.
        public static DoorChange Apply(WorldState world,ActorState actor,string worldId,string doorId,
            int generation,int lifecycle,long revision,bool open,bool paused,IDoorAccessRule access,out string reason)
        {
            reason=null;
            if(world==null || world.worldId!=worldId) reason="Мир изменился. Взаимодействие с дверью отменено.";
            else if(actor==null || actor.generation!=generation || actor.lifecycleSequence!=lifecycle)
                reason="Состояние персонажа изменилось. Взаимодействие с дверью отменено.";
            else if(paused) reason="Локация на паузе: взаимодействие с дверью недоступно.";
            if(reason!=null) return DoorChange.Rejected;
            var door=world.doors.Find(d=>d.id==doorId);
            bool occupied=door!=null && door.open && !open && DoorOccupancy.IsOccupied(world,door);
            return DoorOperations.SetOpen(door,actor,revision,open,access,occupied,out reason);
        }
    }
}
