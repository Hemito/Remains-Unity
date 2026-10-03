using System;
using Remains.Networking;
using UnityEngine;

namespace Remains.Editor
{
    public static class ReplicationCadenceChecks
    {
        public static void Run()
        {
            foreach(int fps in new[]{10,30,60,144})
            foreach(int rate in new[]{10,20})
            foreach(double origin in new[]{0d,604800d})
            {
                var cadence=new ReplicationCadence(1d/rate);
                int sent=0;double last=-1;
                for(int frame=0;frame<fps*10;frame++)
                {
                    double now=origin+(double)frame/fps;
                    if(cadence.TryAdvance(now))
                    {
                        sent++;
                        if(last>=0) Require(now-last<=1d/rate+1d/fps+1e-8,"Пропущено доступное окно отправки");
                        last=now;
                    }
                    Require(!cadence.TryAdvance(now),"Повторный вызов в одном кадре отправляет второе состояние");
                }
                Require(sent==Math.Min(fps,rate)*10,$"Частота зависит от кадров: fps={fps}, rate={rate}, sent={sent}, origin={origin}");
            }
            var stalled=new ReplicationCadence(.05);
            Require(stalled.TryAdvance(0) && stalled.TryAdvance(5.023),"Нет актуального состояния после остановки кадров");
            Require(!stalled.TryAdvance(5.023) && !stalled.TryAdvance(5.03) && stalled.TryAdvance(5.05),"После остановки догоняются пропущенные сообщения либо сбит график");
            var jittered=new ReplicationCadence(.05);int packets=0;double time=0;
            double[] frames={.021,.039,.027,.043};
            for(int i=0;i<400;i++)
            {if(jittered.TryAdvance(time)) packets++;time+=frames[i%frames.Length];}
            Require(packets==260,"Колебания времени кадра изменили среднюю частоту отправки");
            Debug.Log("Частота репликации: PASS — 10/30/60/144 FPS, 10/20 Гц, длительная сессия, неровные кадры, без догоняющих пачек.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Таймер репликации: "+reason);}
    }
}
