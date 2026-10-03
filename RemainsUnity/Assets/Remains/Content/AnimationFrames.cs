using UnityEngine;

namespace Remains.Content
{
    // A signed phase allows reversal without restarting an animation.
    public static class AnimationFrames
    {
        public static int Sample(float time,float fps,int count,bool loop,int loopStart=0,bool stationary=false,bool playIntro=false)
        {
            if(stationary) return 0;
            float position=time*fps;
            // Float accumulation can turn an exact boundary into -3.0000002.
            // Snap only within 1/10000 of a source frame (3.3 microseconds at 30 Hz).
            float nearest=Mathf.Round(position);
            if(Mathf.Abs(position-nearest)<=.0001f) position=nearest;
            int frame=Mathf.FloorToInt(position);
            if(!loop) return Mathf.Clamp(frame,0,count-1);
            if(playIntro && frame>=0 && frame<loopStart) return frame;
            if(playIntro) frame-=loopStart;
            int length=count-loopStart;
            return loopStart+((frame%length)+length)%length;
        }
    }
}
