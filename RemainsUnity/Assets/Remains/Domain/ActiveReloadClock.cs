using System;

namespace Remains.Domain
{
    public enum ActiveReloadAttempt {Ignored,Missed,Succeeded}

    // Local input timing only. Ammo, ownership, saving and cancellation belong to the reload controller.
    public sealed class ActiveReloadClock
    {
        private double duration,elapsed;
        public bool Running {get;private set;}
        public bool Opportunity {get;private set;}
        public float WindowStart {get;private set;}
        public float WindowWidth {get;private set;}
        public float Progress=>duration>0?(float)(elapsed/duration):1;
        public float Remaining=>(float)Math.Max(0,duration-elapsed);

        public static void ValidateWindow(float start,float width)
        {
            if(!float.IsFinite(start) || !float.IsFinite(width) || start<1f/3 || width<=0 || start+width>1)
                throw new ArgumentOutOfRangeException(nameof(start),"Окно активной перезарядки должно быть после первой трети и целиком внутри полосы.");
        }
        public void Start(float seconds,float start,float width)
        {
            ValidateWindow(start,width);
            if(!float.IsFinite(seconds) || seconds<0) throw new ArgumentOutOfRangeException(nameof(seconds));
            duration=seconds;elapsed=0;WindowStart=start;WindowWidth=width;
            Running=seconds>0;Opportunity=Running;
        }
        public void Advance(float seconds)
        {
            if(!float.IsFinite(seconds) || seconds<0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if(!Running) return;
            elapsed=Math.Min(duration,elapsed+seconds);
            if(elapsed>=duration) {Running=false;Opportunity=false;}
        }
        public ActiveReloadAttempt Press()
        {
            if(!Running || !Opportunity) return ActiveReloadAttempt.Ignored;
            // Compare elapsed time, not a frame index; coarse/fine simulation obeys the same interval.
            double progress=elapsed/duration;
            Opportunity=false;
            if(progress>=WindowStart && progress<=WindowStart+WindowWidth)
            {elapsed=duration;Running=false;return ActiveReloadAttempt.Succeeded;}
            elapsed=0;
            return ActiveReloadAttempt.Missed;
        }
        public void Cancel() {elapsed=duration;Running=false;Opportunity=false;}
    }
}
