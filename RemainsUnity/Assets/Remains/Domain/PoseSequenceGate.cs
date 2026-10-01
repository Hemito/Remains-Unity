using System.Collections.Generic;

namespace Remains.Domain
{
    // Each receiver owns its gate. Server and host-client must not share cursors.
    public sealed class PoseSequenceGate
    {
        private readonly Dictionary<string, uint> sequences = new();
        public bool Accept(string actor, int generation, int lifecycle, uint sequence)
        {
            string key = actor + ":" + generation + ":" + lifecycle;
            if (sequences.TryGetValue(key, out uint previous) && unchecked((int)(sequence - previous)) <= 0)
                return false;
            sequences[key] = sequence;
            return true;
        }
        public void Clear() => sequences.Clear();
    }
}
