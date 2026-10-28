using System;

namespace Remains.Content
{
    // Cosmetic-only interpretation of UnitPlayer.klip (1.04). Does not consume gameplay RNG or poseTime.
    public sealed class PonyNestedBlinkClock
    {
        public const int FramesPerSecond=30,InitialTicks=300,MinimumIntervalTicks=50,IntervalChoices=200,ClosedFromTick=5,OpenFromTick=1;
        private string actorId;
        private uint random;
        private int remaining=InitialTicks;
        private double fraction;
        public bool Closed {get;private set;}
        public int RemainingTicks=>remaining;
        public void Bind(string id)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Моргание пони: нужен стабильный ID персонажа.",nameof(id));
            if(actorId==id)return;
            actorId=id;random=2166136261;
            foreach(char value in id){random^=value;random=unchecked(random*16777619);}
            if(random==0)random=0x9e3779b9;
            remaining=InitialTicks;fraction=0;Closed=false;
        }
        // An inactive/dead/down/covered expression is paused, preserving its existing authored eye frame.
        // Call only from the live visual clock, not from raw pose/portrait sampling.
        public bool Advance(float seconds,bool allowed)
        {
            if(actorId==null)throw new InvalidOperationException("Моргание пони: часы не привязаны к персонажу.");
            if(!float.IsFinite(seconds) || seconds<0 || seconds>60)throw new ArgumentOutOfRangeException(nameof(seconds),"Моргание пони: некорректный шаг визуального времени.");
            if(!allowed){bool changed=Closed;Closed=false;return changed;}
            fraction+=seconds*FramesPerSecond;int ticks=(int)fraction;fraction-=ticks;
            // At most 1800 ticks for a validated step; normal frames cost zero or one tick.
            while(ticks-->0)
            {
                if(remaining>0)remaining--;
                else
                {
                    // Stable per-actor PRNG replaces Flash Math.random only for cosmetic intervals.
                    random^=random<<13;random^=random>>17;random^=random<<5;
                    remaining=MinimumIntervalTicks+(int)(random%IntervalChoices);
                }
            }
            bool next=remaining<=ClosedFromTick && remaining>OpenFromTick;
            bool result=Closed!=next;Closed=next;return result;
        }
    }
}
