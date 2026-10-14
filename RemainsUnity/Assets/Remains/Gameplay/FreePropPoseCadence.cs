using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Gameplay
{
    // Moving bodies use the caller's normal pose cadence. Resting bodies resend
    // at most one second apart so loss of the final unreliable pose can recover.
    public sealed class FreePropPoseCadence
    {
        private struct Entry
        {
            public string owner,level;
            public int generation;
            public float x,y,vx,vy;
            public double next;
        }
        private readonly Dictionary<PropState,Entry> sent=new();
        public void Reset()=>sent.Clear();
        public bool ShouldPublish(PropState prop,double now)
        {
            bool found=sent.TryGetValue(prop,out var previous);
            bool changed=!found || previous.owner!=prop.owner || previous.level!=prop.level || previous.generation!=prop.generation ||
                previous.x!=prop.x || previous.y!=prop.y || previous.vx!=prop.vx || previous.vy!=prop.vy;
            if(!changed && now<previous.next) return false;
            // Different IDs stagger the first resting retransmission; subsequent
            // heartbeats retain that phase. No new random state belongs to saves.
            double interval=changed?Phase(prop.id):1;
            sent[prop]=new Entry {owner=prop.owner,level=prop.level,generation=prop.generation,
                x=prop.x,y=prop.y,vx=prop.vx,vy=prop.vy,next=now+interval};
            return true;
        }
        private static double Phase(string id)
        {
            uint hash=2166136261;
            foreach(char value in id) unchecked {hash=(hash^value)*16777619;}
            // 20 slots align with the ordinary 50 ms network cadence.
            return .05*(1+hash%20);
        }
    }
}
