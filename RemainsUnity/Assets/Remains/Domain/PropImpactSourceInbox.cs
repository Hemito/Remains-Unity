using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Receipt of an accepted source schedules geometry exactly once. Retained samples/contacts
    // have separate lifetimes and cannot re-arm this decision when a target later walks into the old point.
    public sealed class PropImpactSourceInbox
    {
        private readonly Dictionary<string,(int generation,uint sequence)> seen=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Queue<PropImpactSample>> queues=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        public void Clear() {seen.Clear();queues.Clear();}
        public bool Accept(PropImpactSample sample,bool active)
        {
            if(seen.TryGetValue(sample.prop,out var old) && (sample.generation<old.generation ||
                sample.generation==old.generation && !PropReplication.IsNewer(sample.sequence,old.sequence))) return false;
            seen[sample.prop]=(sample.generation,sample.sequence);
            if(active)
            {
                if(!queues.TryGetValue(sample.level,out var queue)) queues.Add(sample.level,queue=new Queue<PropImpactSample>());
                queue.Enqueue(sample);
            }
            return true;
        }
        public void DropInactive(Func<string,bool> active)
        {
            removed.Clear();foreach(string level in queues.Keys) if(!active(level)) removed.Add(level);
            foreach(string level in removed) queues.Remove(level);
        }
        public void Drain(string level,List<PropImpactSample> output)
        {
            output.Clear();if(!queues.TryGetValue(level,out var queue)) return;
            while(queue.Count>0) output.Add(queue.Dequeue());
        }
    }
}
