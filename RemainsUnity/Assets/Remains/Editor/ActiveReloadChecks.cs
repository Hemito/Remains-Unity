using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActiveReloadChecks
    {
        public static void Run()
        {
            var clock=new ActiveReloadClock();
            Require(clock.Press()==ActiveReloadAttempt.Ignored,"Нажатие без перезарядки принято.");
            foreach(float time in new[]{4f,5f})
            {
                clock.Start(8,.5f,.125f);clock.Advance(time);
                Require(clock.Press()==ActiveReloadAttempt.Succeeded && !clock.Running && clock.Remaining==0,"Граница окна не завершила перезарядку.");
                Require(clock.Press()==ActiveReloadAttempt.Ignored,"Одна попытка завершила перезарядку дважды.");
            }
            foreach(float time in new[]{0f,3.99f,5.01f,7.99f})
            {
                clock.Start(8,.5f,.125f);clock.Advance(time);
                Require(clock.Press()==ActiveReloadAttempt.Missed && clock.Remaining==8 && !clock.Opportunity && clock.Running,"Промах не запустил полный интервал без окна.");
                clock.Advance(4);
                Require(clock.Press()==ActiveReloadAttempt.Ignored && clock.Remaining==4,"Повтор после промаха изменил таймер или дал второе окно.");
                clock.Advance(4);Require(!clock.Running && clock.Progress==1,"После промаха обычная перезарядка не закончилась.");
            }
            foreach(int fps in new[]{30,60,144})
            {
                clock.Start(3,.55f,.15f);
                for(int frame=0;frame<(int)(fps*1.8f);frame++) clock.Advance(1f/fps);
                float remaining=clock.Remaining;clock.Advance(0);
                Require(clock.Remaining==remaining,"Нулевая dt продвинула паузу.");
                Require(clock.Press()==ActiveReloadAttempt.Succeeded,"Окно зависит от частоты кадров.");
                clock.Start(3,.55f,.15f);
                for(int frame=0;frame<fps*4;frame++) clock.Advance(1f/fps);
                Require(!clock.Running && clock.Remaining==0 && clock.Press()==ActiveReloadAttempt.Ignored,"Без нажатия обычный таймер не завершился.");
            }
            clock.Start(8,.5f,.125f);clock.Advance(4);clock.Cancel();
            Require(!clock.Running && !clock.Opportunity && clock.Press()==ActiveReloadAttempt.Ignored,"Отмена оставила доступное окно.");
            clock.Start(0,.5f,.125f);Require(!clock.Running && clock.Remaining==0,"Нулевой интервал не завершён.");
            Reject(()=>clock.Start(-1,.5f,.1f));Reject(()=>clock.Start(float.NaN,.5f,.1f));
            Reject(()=>clock.Start(1,.32f,.1f));Reject(()=>clock.Start(1,.5f,0));Reject(()=>clock.Start(1,.9f,.2f));
            Reject(()=>clock.Start(1,float.NaN,.1f));Reject(()=>clock.Start(1,.5f,float.PositiveInfinity));
            clock.Start(8,.5f,.125f);clock.Advance(1);
            Reject(()=>clock.Advance(-1));Reject(()=>clock.Advance(float.NaN));
            Require(clock.Remaining==7,"Некорректное время повредило таймер.");
            Debug.Log("Активная перезарядка: PASS — границы, успех, промах, повтор, естественное завершение, пауза, отмена, FPS и неверные параметры.");
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Активная перезарядка: "+message);}
        private static void Reject(Action action)
        {bool rejected=false;try{action();}catch(ArgumentOutOfRangeException){rejected=true;}Require(rejected,"Приняты неверные параметры.");}
    }
}
