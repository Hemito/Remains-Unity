using System;

namespace Remains.Content
{
    // UnitPlayer.freeAnim's cosmetic decision: 1% on each eligible 30 Hz tick, then one of free1/free2/free3 once.
    // Independent of gameplay RNG, motion/pose clocks and snapshot object identity.
    public sealed class PonyAmbientClock
    {
        public const int FramesPerSecond=30;
        private string actorId;
        private PonyAmbientMotionAsset bank;
        private uint random;
        private int clipTicks;
        private double fraction;
        public int ClipIndex {get;private set;}=-1;
        public int Frame {get;private set;}
        public bool Active=>ClipIndex>=0;

        public void Bind(string id,PonyAmbientMotionAsset motions)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Ожидание пони: нужен стабильный ID персонажа.",nameof(id));
            if(motions==null)throw new ArgumentNullException(nameof(motions),"Ожидание пони: нет банка движений.");
            if(actorId==id && ReferenceEquals(bank,motions))return;
            // The loader validates source hashes and node geometry once. A clock binding only needs timing data.
            if(motions.Clips?.Length!=3)throw new ArgumentException("Ожидание пони: требуется три подготовленных клипа.",nameof(motions));
            foreach(var clip in motions.Clips)
                if(clip?.frames==null || clip.frames.Length<2 || !float.IsFinite(clip.fps) || clip.fps<=0)
                    throw new ArgumentException("Ожидание пони: нет корректного времени клипа "+clip?.id+".",nameof(motions));
            actorId=id;bank=motions;random=2166136261;
            // Separate the stream from the blink clock while retaining stable per-actor intervals.
            foreach(char value in "ambient:"+id){random^=value;random=unchecked(random*16777619);}
            if(random==0)random=0x9e3779b9;
            ClipIndex=-1;Frame=clipTicks=0;fraction=0;
        }

        public bool Advance(float seconds,bool allowed)
        {
            if(bank==null)throw new InvalidOperationException("Ожидание пони: часы не привязаны к персонажу и банку.");
            if(!float.IsFinite(seconds) || seconds<0 || seconds>60)
                throw new ArgumentOutOfRangeException(nameof(seconds),"Ожидание пони: некорректный шаг визуального времени.");
            int previousClip=ClipIndex,previousFrame=Frame;
            if(!allowed)
            {
                ClipIndex=-1;Frame=clipTicks=0;fraction=0;
                return previousClip!=ClipIndex || previousFrame!=Frame;
            }
            fraction+=seconds*FramesPerSecond;int ticks=(int)fraction;fraction-=ticks;
            // The validated step bounds this to 1800 ticks; a normal render costs zero or one.
            while(ticks-->0)
            {
                if(Active)
                {
                    var clip=bank.Clips[ClipIndex];clipTicks++;
                    double sample=clipTicks*(double)clip.fps/FramesPerSecond;
                    if(sample>=clip.frames.Length){ClipIndex=-1;Frame=clipTicks=0;}
                    else Frame=(int)sample;
                }
                else if(Next(100)==0)
                {
                    ClipIndex=Next(bank.Clips.Length);Frame=clipTicks=0;
                }
            }
            return previousClip!=ClipIndex || previousFrame!=Frame;
        }

        private int Next(int bound)
        {
            // Rejection removes modulo bias. The private xorshift stream never advances gameplay randomness.
            uint ceiling=uint.MaxValue-uint.MaxValue%(uint)bound,value;
            do{random^=random<<13;random^=random>>17;random^=random<<5;value=random-1;}
            while(value>=ceiling);
            return (int)(value%(uint)bound);
        }
    }
}
