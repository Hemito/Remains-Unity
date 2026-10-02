using UnityEngine;

namespace Remains.Content
{
    // A signed phase allows reversal without restarting an animation.
    public static class AnimationFrames
    {
        public static int Sample(float time,float fps,int count,bool loop,int loopStart=0,bool stationary=false)
        {
            if(stationary) return 0;
            int frame=Mathf.FloorToInt(time*fps);
            if(!loop) return Mathf.Clamp(frame,0,count-1);
            int length=count-loopStart;
            return loopStart+((frame%length)+length)%length;
        }
    }
}
