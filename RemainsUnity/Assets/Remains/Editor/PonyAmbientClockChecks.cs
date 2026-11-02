using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyAmbientClockChecks
    {
        public static void Run()
        {
            var bank=GameAssets.Load<PonyAmbientMotionAsset>(PonyAmbientMotionAsset.Resource)??
                throw new InvalidOperationException("Проверка ожидания пони: нет подготовленного банка движений.");
            bank.Validate();
            var clock=new PonyAmbientClock();clock.Bind("ambient-clock-check",bank);
            Require(!clock.Active && clock.ClipIndex==-1 && clock.Frame==0,"новые часы должны начинаться со стойки");
            bool found=false;
            for(int i=0;i<3000;i++){clock.Advance(1f/30,true);if(clock.Active){found=true;break;}}
            Require(found,"за контрольный интервал не выбран ни один клип");
            int clipIndex=clock.ClipIndex;Require(clock.Frame==0,"новый клип начинается не с первого кадра");
            var clip=bank.Clips[clipIndex];
            for(int i=1;i<clip.frames.Length;i++)
            {
                clock.Advance(1f/clip.fps,true);
                Require(clock.ClipIndex==clipIndex && clock.Frame==i,"одноразовый клип пропустил или повторил кадр "+i);
            }
            clock.Advance(1f/clip.fps,true);Require(!clock.Active && clock.Frame==0,"после последнего кадра не вернулась стойка");

            // Equal stable IDs, same real visual time, different render rates; rebinding a snapshot keeps the phase.
            var at30=new PonyAmbientClock();var at60=new PonyAmbientClock();
            at30.Bind("ambient-partition-check",bank);at60.Bind("ambient-partition-check",bank);
            var seen=new bool[bank.Clips.Length];
            for(int i=0;i<12000;i++)
            {
                at30.Advance(1f/30,true);at60.Advance(1f/60,true);at60.Advance(1f/60,true);
                if(i%7==0)at60.Bind("ambient-partition-check",bank);
                Require(at30.ClipIndex==at60.ClipIndex && at30.Frame==at60.Frame,"частота кадров или rebind изменили фазу на шаге "+i);
                if(at30.Active)seen[at30.ClipIndex]=true;
            }
            for(int i=0;i<seen.Length;i++)Require(seen[i],"не выбирается клип "+bank.Clips[i].id);

            // Cancellation is immediate even for dt=0; movement time must not be accumulated for a delayed restart.
            for(int i=0;i<3000 && !at30.Active;i++)at30.Advance(1f/30,true);
            Require(at30.Active,"нет активной анимации для проверки отмены");
            at30.Advance(0,false);Require(!at30.Active && at30.Frame==0,"запрет не отменил клип сразу");
            at30.Advance(60,false);at30.Advance(0,true);
            Require(!at30.Active && at30.Frame==0,"после движения возобновился старый клип");
            bool rejected=false;try{at30.Advance(float.NaN,true);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"неконечное визуальное время принято");
            Debug.Log("Ожидание пони, часы: PASS — одноразовые кадры, все три клипа, одинаковая фаза 30/60 FPS, сохранение при rebind и немедленная отмена без накопления времени.");
        }

        private static void Require(bool condition,string reason)
        {if(!condition)throw new InvalidOperationException("Проверка ожидания пони: "+reason+".");}
    }
}
