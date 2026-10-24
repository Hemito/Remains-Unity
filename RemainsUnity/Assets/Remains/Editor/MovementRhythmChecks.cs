using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    public static class MovementRhythmChecks
    {
        public static void Run()
        {
            var settings=JsonUtility.FromJson<MovementAudio.Settings>(GameAssets.Load<TextAsset>("Presentation/movement-audio").text);
            foreach(var pattern in settings.rhythms)
            {
                pattern.Validate();
                foreach(int rate in new[]{30,60,144})
                foreach(int direction in pattern.pose=="climb"?new[]{1,-1}:new[]{1})
                {
                    var actor=new ActorState {id="rhythm",level="hub",pose=pattern.pose,climbing=pattern.pose=="climb",vx=4,vy=direction*3};
                    var track=new MovementRhythm();track.Sample(actor,pattern,true,false,3);
                    int count=0;float end=pattern.period*4;
                    for(int frame=1;frame<=Math.Ceiling(end*rate);frame++)
                    {
                        float time=Mathf.Min(frame/(float)rate,end);actor.poseTime=time*direction;
                        actor.x=time*4;actor.y=actor.climbing?time*direction*3:0;
                        count+=track.Sample(actor,pattern,true,false,3);
                    }
                    Require(count==pattern.beats.Length*4,$"Ритм зависит от FPS: {pattern.pose}, {rate}, {direction}, count={count}.");
                    Require(track.Sample(actor,pattern,true,false,3)==0,"Повторный снимок воспроизводит шаг.");
                    actor.poseTime+=direction*.1f;Require(track.Sample(actor,pattern,true,true,3)==0,"Пауза воспроизводит шаг.");
                    Require(track.Sample(actor,pattern,true,false,3)==0,"Возобновление повторяет пропущенные шаги.");
                    actor.poseTime+=direction*.6f;Require(track.Sample(actor,pattern,true,false,3)==0,"Долгий разрыв воспроизводит историю шагов.");
                    actor.generation++;actor.poseTime=0;Require(track.Sample(actor,pattern,true,false,3)==0,"Смена владельца воспроизводит историю.");
                    actor.poseTime=.2f;actor.vx=actor.vy=0;Require(track.Sample(actor,pattern,true,false,3)==0,"Неподвижный актёр создаёт шаги.");
                }
            }
            var single=new MovementBeatPattern {pose="run",period=1,beats=new[]{.1f}};
            var runner=new ActorState {id="rewind",level="hub",pose="run",vx=5};var corrected=new MovementRhythm();
            corrected.Sample(runner,single,true,false,3);runner.poseTime=.11f;runner.x=.1f;
            Require(corrected.Sample(runner,single,true,false,3)==1,"Не замечена отметка шага.");
            runner.poseTime=.09f;runner.x=.2f;Require(corrected.Sample(runner,single,true,false,3)==0,"Коррекция назад создала шаг.");
            runner.poseTime=.12f;runner.x=.3f;Require(corrected.Sample(runner,single,true,false,3)==0,"Повторно пройденная отметка создала шаг.");
            runner.poseSequence++;runner.poseTime=0;corrected.Sample(runner,single,true,false,3);runner.poseTime=.11f;runner.x=.4f;
            Require(corrected.Sample(runner,single,true,false,3)==1,"Новое движение не сбросило отметку старого.");
            runner.poseSequence++;runner.poseTime=0;corrected.Sample(runner,single,true,false,3);runner.poseTime=.11f;
            Require(corrected.Sample(runner,single,true,false,3)==0,"Застывшая удалённая позиция звучит из-за старой скорости.");
            Debug.Log("Ритм движения: PASS — четыре цикла при 30/60/144 FPS, спуск, покой, пауза, разрыв, коррекция и передача.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
