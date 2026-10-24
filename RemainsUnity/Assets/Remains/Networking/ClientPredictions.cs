using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Networking
{
    // Interacting client first (decisions.md, 2026-10-24): a client applies its own interaction with a shared part (a lamp
    // it broke, a door it opened, a body it grabbed) to its live world at once and tells the host. The host's canonical
    // copy confirms it with a tick; a prediction the canonical world does not confirm in time, or that the host rejects,
    // is undone from the canonical state. One prediction per part key: a newer one replaces the older.
    public sealed class ClientPredictions
    {
        public sealed class Entry
        {
            public string Key;
            public WorldChangeKind Kind;
            public Func<WorldState,bool> Confirmed;      // canonical world → the prediction holds there
            public Action<WorldState,WorldState> Revert; // (canonical, live): put the canonical part back into the live world
            public float Deadline;
        }
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        public int Count=>entries.Count;
        public void Add(Entry entry)=>entries[entry.Key]=entry;
        public void Clear()=>entries.Clear();

        // Drops confirmed predictions; returns those to undo (rejected keys, expired ones when now is given).
        public List<Entry> Settle(WorldState canonical,float? now,ICollection<string> rejected=null)
        {
            List<Entry> undo=null,done=null;
            foreach(var entry in entries.Values)
            {
                bool reject=rejected!=null && rejected.Contains(entry.Key);
                if(!reject && canonical!=null && entry.Confirmed(canonical)) {(done??=new List<Entry>()).Add(entry);continue;}
                if(reject || now.HasValue && now.Value>=entry.Deadline) (undo??=new List<Entry>()).Add(entry);
            }
            if(done!=null) foreach(var entry in done) entries.Remove(entry.Key);
            if(undo!=null) foreach(var entry in undo) entries.Remove(entry.Key);
            return undo;
        }
    }
}
