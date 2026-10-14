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
        // By stable body ID, not object: the session replaces the World graph on every received snapshot, and a
        // reference key made every body "new" again (republished with a new sequence each snapshot).
        private readonly Dictionary<string,Entry> sent=new(StringComparer.Ordinal);
        public void Reset()=>sent.Clear();
        public bool ShouldPublish(PropState prop,double now)=>ShouldPublish(prop,now,out _);
        // changed: the pose differs from the last one sent (a new pose version); a resting heartbeat repeats the
        // last pose and keeps its sequence, so receivers that already have it ignore it and the world state
        // (sequence included) does not change once a body is at rest.
        public bool ShouldPublish(PropState prop,double now,out bool changed)
        {
            bool found=sent.TryGetValue(prop.id,out var previous);
            changed=!found || previous.owner!=prop.owner || previous.level!=prop.level || previous.generation!=prop.generation ||
                previous.x!=prop.x || previous.y!=prop.y || previous.vx!=prop.vx || previous.vy!=prop.vy;
            if(!changed && now<previous.next) return false;
            // Different IDs stagger the first resting retransmission; subsequent
            // heartbeats retain that phase. No new random state belongs to saves.
            double interval=changed?Phase(prop.id):1;
            sent[prop.id]=new Entry {owner=prop.owner,level=prop.level,generation=prop.generation,
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
