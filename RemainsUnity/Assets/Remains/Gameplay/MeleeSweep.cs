using System;
using UnityEngine;

namespace Remains.Gameplay
{
    public readonly struct MeleeSegment
    {
        public readonly Vector2 start,end;
        public readonly float speed;
        public MeleeSegment(Vector2 start,Vector2 end,float seconds)
        {this.start=start;this.end=end;speed=Vector2.Distance(start,end)/seconds;}
    }
    // Fixed pose samples preserve the outward and return paths even in a long frame.
    public sealed class MeleeSweep
    {
        public const float Duration=.3f;
        private const int Samples=36;
        private const float Interval=Duration/Samples;
        private readonly string weapon;
        private readonly Vector2 direction;
        private Vector2 previousOrigin,previousTip;
        private float elapsed;
        private int sample=1;
        public Vector2 Tip {get;private set;}
        public bool Finished=>sample>Samples;
        public float Elapsed=>elapsed;
        public MeleeSweep(string weapon,Vector2 origin,Vector2 direction)
        {
            this.weapon=weapon;this.direction=direction;previousOrigin=origin;
            Tip=previousTip=WeaponPose.Tip(weapon,origin,direction,0);
        }
        public void Advance(Vector2 origin,float dt,Action<MeleeSegment> emit)
        {
            if(Finished || dt<=0) return;
            float end=Mathf.Min(Duration,elapsed+dt);
            while(sample<=Samples && sample*Interval<=end+.000001f)
            {
                float time=sample*Interval;
                var sampledOrigin=Vector2.Lerp(previousOrigin,origin,Mathf.Clamp01((time-elapsed)/dt));
                var tip=WeaponPose.Tip(weapon,sampledOrigin,direction,time/Duration);
                emit(new MeleeSegment(previousTip,tip,Interval));previousTip=tip;sample++;
            }
            Tip=WeaponPose.Tip(weapon,Vector2.Lerp(previousOrigin,origin,(end-elapsed)/dt),direction,end/Duration);
            elapsed=end;previousOrigin=origin;
        }
    }
}
