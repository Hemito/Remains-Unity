using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class CameraFollowMotionChecks
    {
        public static void Run()
        {
            LinearTravel();RecordedLongFrame();Subdivision();BootstrapAndReset();InvalidInputs();
        }
        private static void LinearTravel()
        {
            foreach(int fps in new[]{30,60,144}) foreach(float speed in new[]{3.15f,-4.725f})
            {
                var motion=new CameraFollowMotion();
                var target=new Vector3(42,10,-10);
                var center=target-Vector3.up*(speed/CameraFollowMotion.DefaultResponseRate);
                motion.Step(center,target,0);
                double elapsed=0;
                for(int sample=0;sample<fps*3;sample++)
                {
                    float dt=1f/fps;
                    if(sample%19==7) dt*=1.6f;
                    if(sample==fps) dt=1f/3; // A long captured frame, still a confirmed linear path.
                    elapsed+=dt;target.y=10+(float)(speed*elapsed);
                    center=motion.Step(center,target,dt);
                    Near(center.y,target.y-speed/8,.00008f,$"Линейное движение: fps={fps}, speed={speed}, sample={sample}.");
                    Check(center.x==42 && center.z==-10,"Следование двигает неподвижную ось.");
                }
            }
        }
        private static void RecordedLongFrame()
        {
            // Real 30 FPS host frames 320,321,322. All remain climb/up at 3.15 m/s.
            // The first interval is .0532103 s, not a screenshot stall.
            float[] body={12.822032f,12.989646f,13.0946474f};
            float[] actualCamera={13.3734512f,13.528965f,13.6367989f};
            float firstScreenDelta=(body[1]-actualCamera[1]-body[0]+actualCamera[0])*108;
            float secondScreenDelta=(body[2]-actualCamera[2]-body[1]+actualCamera[1])*108;
            Check(firstScreenDelta>1 && secondScreenDelta<-.2f,"Записанный screen-space контрпример утрачен.");
            var motion=new CameraFollowMotion();var target=new Vector3(42,body[0]+1,-10);
            var center=target-Vector3.up*(3.15f/8);motion.Step(center,target,0);
            float screen=(body[0]-center.y)*108;
            for(int index=1;index<body.Length;index++)
            {
                target.y=body[index]+1;
                center=motion.Step(center,target,index==1?.0532103f:.0333337f);
                Near((body[index]-center.y)*108,screen,.002f,"Время кадра двигает тело обратно на экране.");
            }
        }
        private static void Subdivision()
        {
            Vector3 initial=new(2,8,-10),from=new(4,10,-10),to=new(4.8f,12,-10);
            var whole=new CameraFollowMotion();whole.Step(initial,from,0);
            var expected=whole.Step(initial,to,.25f);
            foreach(int steps in new[]{30,60,144})
            {
                var split=new CameraFollowMotion();split.Step(initial,from,0);var center=initial;
                for(int index=1;index<=steps;index++) center=split.Step(center,Vector3.Lerp(from,to,(float)index/steps),.25f/steps);
                Check(Vector3.Distance(center,expected)<.00003f,$"Разбиение линейного пути меняет результат: steps={steps}, result={center}, expected={expected}.");
            }
        }
        private static void BootstrapAndReset()
        {
            var motion=new CameraFollowMotion();Vector3 center=new(1,8,-10),target=new(4,10,-10);
            var expected=target+(center-target)*Mathf.Exp(-8*.1f);
            NearVector(motion.Step(center,target,.1f),expected,"Первый sample придумал скорость цели.");
            Check(motion.Step(center,new Vector3(70,20,-10),0)==center,"Нулевой шаг сдвигает камеру.");
            motion.Reset();
            NearVector(motion.Step(center,target,.1f),expected,"Reset переносит скорость телепорта в следующий шаг.");
            NearVector(motion.Step(target,target,1f/60),target,"Неподвижная камера дрейфует.");
            // Feed the actual externally clamped center into the next step.
            motion.Reset();motion.Step(target,target,0);
            var clamped=new Vector3(4,12,-10);
            NearVector(motion.Step(clamped,target,.1f),target+(clamped-target)*Mathf.Exp(-.8f),"После FrameAxis используется скрытый unclamped center.");
        }
        private static void InvalidInputs()
        {
            foreach(float dt in new[]{-1,float.NaN,float.PositiveInfinity})
                Reject(()=>new CameraFollowMotion().Step(Vector3.zero,Vector3.one,dt),"Принят неверный dt камеры.");
            Reject(()=>new CameraFollowMotion(0),"Принята нулевая скорость камеры.");
            Reject(()=>new CameraFollowMotion(float.NaN),"Принята неверная скорость камеры.");
            Reject(()=>new CameraFollowMotion().Step(new Vector3(float.NaN,0,0),Vector3.zero,.01f),"Принят неверный центр камеры.");
            Reject(()=>new CameraFollowMotion().Step(Vector3.zero,new Vector3(0,float.PositiveInfinity,0),.01f),"Принята неверная цель камеры.");
        }
        private static void Reject(Action action,string reason)
        {
            bool rejected=false;try {action();} catch(ArgumentException) {rejected=true;}Check(rejected,reason);
        }
        private static void Near(float actual,float expected,float tolerance,string reason)
            =>Check(Mathf.Abs(actual-expected)<=tolerance,$"{reason} actual={actual:R}, expected={expected:R}.");
        private static void NearVector(Vector3 actual,Vector3 expected,string reason)
            =>Check(Vector3.Distance(actual,expected)<.00001f,$"{reason} actual={actual}, expected={expected}.");
        private static void Check(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
