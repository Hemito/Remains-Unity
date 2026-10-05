using System;

namespace Remains.Domain
{
    internal static class MechanismStateValidation
    {
        public static void Require(bool valid,string field,string id)
        {
            if(!valid) throw new InvalidOperationException($"Некорректный механизм: id={id ?? "<нет>"}, поле={field}.");
        }
        public static void Placement(int schema,string id,string definition,string level,string roomId,string groupId,long revision)
        {
            Require(schema==1,"schema (поддерживается 1)",id);
            Require(!string.IsNullOrWhiteSpace(id),"id",id);
            Require(!string.IsNullOrWhiteSpace(definition),"definition",id);
            Require(!string.IsNullOrWhiteSpace(level),"level",id);
            Require(!string.IsNullOrWhiteSpace(roomId),"roomId",id);
            Require(!string.IsNullOrWhiteSpace(groupId),"groupId",id);
            Require(ScopedId(id,level),"id не принадлежит level",id);
            Require(revision>=0,"revision",id);
        }
        public static bool ScopedId(string id,string level)=>
            !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(level) && id.Length>level.Length+1 &&
            id[level.Length]==':' && string.CompareOrdinal(id,0,level,0,level.Length)==0;
        public static bool SameGroup(string level,string room,string group,string otherLevel,string otherRoom,string otherGroup)=>
            string.Equals(level,otherLevel,StringComparison.Ordinal) && string.Equals(room,otherRoom,StringComparison.Ordinal) &&
            string.Equals(group,otherGroup,StringComparison.Ordinal);
    }
}
