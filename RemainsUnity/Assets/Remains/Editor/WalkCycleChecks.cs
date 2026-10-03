using System;
using Remains.Content;
namespace Remains.Editor
{
    public static class WalkCycleChecks
    {
        public static void Run()
        {
            PonyAnimationData.Load();
            var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id=="walk");
            if(clip.frames.Length!=17 || clip.loopStartFrame!=1 || !clip.playIntro) throw new InvalidOperationException("Неверные границы цикла ходьбы.");
            for(int i=0;i<65;i++)
            {
                int expected=i==0?0:1+(i-1)%16;
                if(PonyAnimationData.FrameIndex("walk",(i+.1f)/30,false)!=expected) throw new InvalidOperationException("Неверный переход цикла ходьбы: "+i);
            }
            if(AnimationFrames.Sample(0,30,13,true,1)!=1 || AnimationFrames.Sample(0,30,13,true,1,true)!=0)
                throw new InvalidOperationException("Изменился вход в цикл лестницы.");
        }
    }
}
