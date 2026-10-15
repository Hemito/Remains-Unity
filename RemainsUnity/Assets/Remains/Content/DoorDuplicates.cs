using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // The original room data sometimes holds the same door twice in one cell (plant/ofisy2: two door1 at 28,19).
    // Both copies then stand in one place: opening one leaves the other closed on top of it, and only one can be
    // targeted. One door is kept: the one whose ID is first in ordinal order, the same rule for new layouts
    // (FactoryDoorAdapter) and saved worlds (Remove).
    public static class DoorDuplicates
    {
        public static bool SamePlace(DoorState a,DoorState b)=>a.level==b.level && a.definition==b.definition &&
            Mathf.Abs(a.x-b.x)<.001f && Mathf.Abs(a.y-b.y)<.001f && Mathf.Abs(a.width-b.width)<.001f && Mathf.Abs(a.height-b.height)<.001f;

        // Saved worlds that installed both copies. Returns the removed IDs; backup runs once before the first removal.
        public static IReadOnlyList<string> Remove(WorldState world,Action backup)
        {
            var removed=new List<string>();
            foreach(var group in world.doors.GroupBy(d=>(d.level,d.definition,Mathf.RoundToInt(d.x*1000),Mathf.RoundToInt(d.y*1000))))
            {
                var doors=group.Where(d=>group.Any(o=>o!=d && SamePlace(o,d))).OrderBy(d=>d.id,StringComparer.Ordinal).ToList();
                if(doors.Count<2) continue;
                foreach(var duplicate in doors.Skip(1))
                {
                    if(removed.Count==0) backup?.Invoke();
                    world.doors.Remove(duplicate);removed.Add(duplicate.id);
                }
            }
            if(removed.Count>0)
            {
                world.revision++;
                Debug.LogWarning($"Удалены двойные двери в одной клетке (дубль в исходных данных оригинала), оставлена первая по ID: {string.Join("; ",removed)}.");
            }
            return removed;
        }
    }
}
