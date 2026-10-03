using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class MovementBeatPattern
    {
        public string pose;
        public float period;
        public float[] beats;
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(pose) || !float.IsFinite(period) || period<=0 || beats==null || beats.Length==0)
                throw new InvalidOperationException("Не заполнен ритм шагов: "+pose);
            float previous=-1;
            foreach(float beat in beats)
            {
                if(!float.IsFinite(beat) || beat<0 || beat>=period || beat<=previous)
                    throw new InvalidOperationException("Некорректные отметки ритма шагов: "+pose);
                previous=beat;
            }
        }
    }
    // Cosmetic interval crossing, independent of physics and rendered frame callbacks.
    public sealed class MovementRhythm
    {
        private string level,pose;
        private int generation;
        private uint sequence;
        private float phase;
        private Vector2 position;
        private bool initialized;
        public int Sample(ActorState actor,MovementBeatPattern pattern,bool grounded,bool suppressed,float teleportDistance)
        {
            float current=actor.poseTime;
            bool reset=!initialized || level!=actor.level || generation!=actor.generation || sequence!=actor.poseSequence ||
                pose!=actor.pose || Vector2.Distance(position,actor.Position)>teleportDistance || suppressed;
            int count=0;
            double delta=(double)current-phase;
            bool moving=actor.climbing?Mathf.Abs(actor.vy)>.001f && Mathf.Abs(actor.y-position.y)>.00001f:
                grounded && Mathf.Abs(actor.vx)>.1f && Mathf.Abs(actor.x-position.x)>.00001f;
            bool forward=actor.climbing?delta*actor.vy>0:delta>0;
            if(!reset && pattern!=null && moving && Math.Abs(delta)<=.25 && forward)
            {
                // Do not replay old network history after long stalls or phase corrections.
                // Round-off tolerance is a millionth of a second, not a gameplay delay.
                foreach(float beat in pattern.beats)
                {
                    double before=(phase-beat)/(double)pattern.period;
                    double after=(current-beat)/(double)pattern.period;
                    double epsilon=.000001/pattern.period;
                    count+=delta>0?(int)(Math.Floor(after+epsilon)-Math.Floor(before+epsilon)):
                        (int)(Math.Ceiling(before-epsilon)-Math.Ceiling(after-epsilon));
                }
            }
            initialized=true;level=actor.level;generation=actor.generation;sequence=actor.poseSequence;
            pose=actor.pose;phase=!reset && !actor.climbing?Math.Max(phase,current):current;position=actor.Position;
            return count;
        }
    }
}
