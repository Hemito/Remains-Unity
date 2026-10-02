using System.Collections.Generic;

namespace Remains.Networking
{
    // Ephemeral, reliable-channel presentation events; never a gameplay transaction journal.
    public sealed class ReloadSoundGate
    {
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        private readonly Dictionary<string,string> active=new();
        public bool Accept(string actor,string token,bool started)
        {
            if(string.IsNullOrEmpty(actor) || string.IsNullOrEmpty(token)) return false;
            if(started)
            {
                if(!seen.Add(token)) return false;
                recent.Enqueue(token);if(recent.Count>512) seen.Remove(recent.Dequeue());
                active[actor]=token;return true;
            }
            if(!active.TryGetValue(actor,out var current) || current!=token) return false;
            active.Remove(actor);return true;
        }
        public void Clear() {seen.Clear();recent.Clear();active.Clear();}
    }
}
