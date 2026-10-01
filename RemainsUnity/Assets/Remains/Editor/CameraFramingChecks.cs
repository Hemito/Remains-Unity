using System;
using Remains.Presentation;

namespace Remains.Editor
{
    public static class CameraFramingChecks
    {
        public static void Run()
        {
            foreach(var sample in new[] {
                new[]{8f,-.7f,2.5f,8f},new[]{-8f,-2.5f,.7f,8f},
                new[]{5f,-.4f,1.8f,5f},new[]{-5f,-1.8f,.4f,5f}})
            {
                float center=CameraFollower.FrameAxis(sample[0],sample[1],sample[2],sample[3]);
                if(center-sample[3]>sample[1]-.149f || center+sample[3]<sample[2]+.149f)
                    throw new InvalidOperationException("Камера обрезает границы позы у края.");
            }
            if(CameraFollower.FrameAxis(2,0,4,5)!=2)
                throw new InvalidOperationException("Камера сдвигает уже видимую позу.");
        }
    }
}
