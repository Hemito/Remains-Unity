using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    [Serializable] public sealed class WorldRoom
    {
        public string level;
        public string definition;
    }
    public static class WorldRooms
    {
        // An empty list is the explicit schema-1 contract of existing prototype saves.
        public static string Definition(WorldState world,string level)
        {
            string result=null;var levels=new HashSet<string>(StringComparer.Ordinal);
            if(world!=null)
            {
                if(world.rooms==null) throw new InvalidOperationException("Мир: отсутствует список определений комнат.");
                foreach(var room in world.rooms)
                {
                    if(room==null || string.IsNullOrWhiteSpace(room.level) || string.IsNullOrWhiteSpace(room.definition) || !levels.Add(room.level))
                        throw new InvalidOperationException($"Мир {world.worldId}: некорректная или повторная привязка комнаты {room?.level}.");
                    if(room.level==level) result=room.definition;
                }
            }
            if(result!=null) return result;
            return level switch {"hub"=>"base:hub","factory:0"=>"base:factory-prototype",
                _=>throw new InvalidOperationException("В мире не задано определение локации: "+level)};
        }
    }
}
