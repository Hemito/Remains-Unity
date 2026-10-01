using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class VariableJumpChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {left=-10,right=10,bottom=-1,top=10,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
            foreach(int rate in new[]{30,60,144})
            {
                float dt=1f/rate;float[] peaks=new float[2];
                for(int mode=0;mode<2;mode++)
                {
                    var actor=new ActorState();var movement=new MovementController(room);
                    for(int i=0;i<rate*3;i++)
                    {
                        movement.Step(actor,Vector2.zero,i==0,dt,false,mode==1);
                        peaks[mode]=Mathf.Max(peaks[mode],actor.y);
                        if(i>rate*2) Require(Mathf.Abs(actor.y)<.001f && actor.jumpSupportRemaining==0,"Удержание повторило прыжок после приземления");
                    }
                }
                Require(Mathf.Abs(peaks[0]-1.44f)<.02f && Mathf.Abs(peaks[1]-3.36f)<.02f,
                    $"Высота прыжка зависит от частоты: rate={rate}, short={peaks[0]}, long={peaks[1]}");
            }
            var source=new ActorState();var control=new MovementController(room);
            control.Step(source,Vector2.zero,true,.05f,false,true);
            var saved=source.Copy();var motionCopy=new ActorState();ActorPose.CopyMotion(source,motionCopy);
            Require(saved.jumpSupportRemaining==source.jumpSupportRemaining && motionCopy.jumpSupportRemaining==source.jumpSupportRemaining,"Копия потеряла остаток поддержки прыжка");
            control.Step(source,Vector2.zero,false,.05f,false,true);
            new MovementController(room).Step(saved,Vector2.zero,false,.05f,false,true);
            Require(source.Position==saved.Position && source.vy==saved.vy,"Продолжение сохранённого прыжка изменило траекторию");
            control.Step(source,Vector2.zero,false,.01f,false,false);float releasedVelocity=source.vy;
            control.Step(source,Vector2.zero,true,.01f,false,true);
            Require(source.jumpSupportRemaining==0 && source.vy<releasedVelocity,"Повторное нажатие в воздухе возобновило поддержку");
            room.platforms=new[]{room.platforms[0],new RoomSurface {x=-2,y=2,width=4,height=.2f}};
            source=new ActorState();control=new MovementController(room);
            for(int i=0;i<15;i++) control.Step(source,Vector2.zero,i==0,1f/60,false,true);
            Require(source.jumpSupportRemaining==0 && source.vy<=0,"Потолок не прекратил поддержку прыжка");
            Debug.Log("Прыжок: PASS — короткий/длинный, 30/60/144 Гц, отпускание, потолок, сохранение и отсутствие автоповтора.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
