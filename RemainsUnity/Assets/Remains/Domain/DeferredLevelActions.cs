using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Transient received events, not a durable transaction journal. Caller retains identity checks.
    public sealed class DeferredLevelActions
    {
        private readonly List<(string level,Action apply)> pending=new();
        public int Count=>pending.Count;
        public void Add(string level,Action apply)=>pending.Add((level,apply??throw new ArgumentNullException(nameof(apply))));
        public void Clear()=>pending.Clear();
        public void Drain(Func<string,bool> paused)
        {
            if(pending.Count==0) return;
            var ready=pending.FindAll(entry=>!paused(entry.level));
            foreach(var entry in ready) {pending.Remove(entry);entry.apply();}
        }
    }
}
