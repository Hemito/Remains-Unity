using UnityEngine;

namespace Remains.Content
{
    // A signed phase allows reversal without restarting an animation.
    public static class AnimationFrames
    {
        public static int Sample(float time,float fps,int count,bool loop,int loopStart=0,bool stationary=false,bool playIntro=false)
        {
            if(stationary) return 0;
            int frame=Mathf.FloorToInt(time*fps);
            if(!loop) return Mathf.Clamp(frame,0,count-1);
            if(playIntro && frame>=0 && frame<loopStart) return frame;
            if(playIntro) frame-=loopStart;
            int length=count-loopStart;
            return loopStart+((frame%length)+length)%length;
        }
    }
}
