using System;
using Remains.Domain;
namespace Remains.Editor
{
    public static class ActorMotionReplicationChecks
    {
        public static void Run()
        {
            CheckRemoteRun();
            var current=new ActorState {id="pose-check",owner="a",level="hub",generation=2,lifecycleSequence=3,
                motionSequence=12,motionSentAt=604800.25,motionClockGeneration=2,pose="run",poseTime=1.1f,x=17,vx=-8,facing=-1};
            foreach(uint sequence in new uint[]{10,12})
            {
                var snapshot=current.Copy();snapshot.motionSequence=sequence;snapshot.poseTime=1;snapshot.x=18;
                snapshot.health=23;
                snapshot.motionSentAt=0;snapshot.motionClockGeneration=-1;
                ActorMotionReplication.PreserveNewerMotion(current,snapshot);
                Require(snapshot.poseTime==1.1f && snapshot.x==17 && snapshot.motionSequence==12 && snapshot.health==23 &&
                    snapshot.motionSentAt==current.motionSentAt && snapshot.motionClockGeneration==current.motionClockGeneration,
                    "Старый снимок откатил движение или потерял новое здоровье");
            }
            var restart=current.Copy();restart.motionSequence=13;restart.poseTime=0;
            ActorMotionReplication.PreserveNewerMotion(current,restart);
            Require(restart.poseTime==0 && restart.motionSequence==13,"Новый запуск run ошибочно запрещён");
            var ladder=current.Copy();ladder.motionSequence=14;ladder.pose="climb";ladder.poseTime=-2;ladder.climbing=true;ladder.vy=-1;
            ActorMotionReplication.PreserveNewerMotion(current,ladder);
            Require(ladder.poseTime==-2 && ladder.climbing,"Обратная лестничная фаза потеряна");
            foreach(string boundary in new[]{"owner","generation","lifecycle","level","id"})
            {
                var next=current.Copy();next.motionSequence=1;next.poseTime=0;next.x=40;
                switch(boundary)
                {
                    case "owner":next.owner="b";break;
                    case "generation":next.generation++;break;
                    case "lifecycle":next.lifecycleSequence++;break;
                    case "level":next.level="factory:0";break;
                    case "id":next.id="other";break;
                }
                ActorMotionReplication.PreserveNewerMotion(current,next);
                Require(next.x==40 && next.poseTime==0,"Предыдущее движение перенесено через границу "+boundary);
            }
            Require(ActorMotionReplication.IsNewer(0,uint.MaxValue) && !ActorMotionReplication.IsNewer(uint.MaxValue,0),
                "Неверный порядок при переполнении счётчика");
            Require(current.Copy().motionSequence==12,"Снимок не сохраняет номер движения");
            Require(current.Copy().motionSentAt==604800.25 && current.Copy().motionClockGeneration==2,"Снимок потерял время или поколение часов движения");
        }
        private static void CheckRemoteRun()
        {
            var actor=new ActorState {id="phase",owner="remote",pose="run",poseSequence=7,poseTime=.5338194f};
            RemotePosePhase.Apply(actor,"run",7,.5004858f);
            Require(Math.Abs(actor.poseTime-.5338194f)<.000001f,"Свежий пакет отмотал показанный run");
            ActorPose.Advance(actor,1f/60);
            Require(actor.poseTime>.5338194f && actor.poseTime<.5338194f+1f/60 && actor.poseCorrectionRemaining>0,"Коррекция остановила цикл или не замедлила опережение");
            ActorPose.Advance(actor,.1f);
            Require(Math.Abs(actor.poseTime-(.5004858f+1f/60+.1f))<.00001f && actor.poseCorrectionRemaining==0,"Коррекция накапливает опережение");
            RemotePosePhase.Apply(actor,"run",8,0);
            Require(actor.poseTime==0 && actor.poseCorrectionRemaining==0,"Настоящий перезапуск не сбросил run");
            ActorPose.Select(actor,"idle");ActorPose.Select(actor,"run");
            Require(actor.poseSequence==10,"Пропущенная наблюдателем остановка не меняет номер запуска");
            actor.poseTime=1;actor.motionSequence=1;
            var snapshot=actor.Copy();snapshot.motionSequence=2;snapshot.poseTime=.98f;
            RemotePosePhase.Reconcile(actor,snapshot);
            Require(snapshot.poseTime==1 && snapshot.poseCorrectionRemaining>0,"Снимок мира отматывает текущий run");
            Require(snapshot.Copy().poseSequence==10 && snapshot.Copy().poseCorrectionRemaining==0,"Транзитная поправка попала в сохранение");
            snapshot=actor.Copy();snapshot.owner="new-owner";snapshot.motionSequence=2;snapshot.poseTime=0;
            RemotePosePhase.Reconcile(actor,snapshot);Require(snapshot.poseTime==0,"Поправка пересекла границу владения");
            actor.climbing=true;actor.vy=-1;
            RemotePosePhase.Apply(actor,"climb",11,-.5f);ActorPose.Advance(actor,.1f);
            Require(Math.Abs(actor.poseTime+.6f)<.00001f,"Коррекция run сломала обратное проигрывание лестницы");
            foreach(int fps in new[]{30,60,144})
            {
                var runner=new ActorState {pose="run",poseSequence=1,poseTime=1};
                RemotePosePhase.Apply(runner,"run",1,.9f);
                ActorPose.Advance(runner,0);
                Require(runner.poseTime==1,"Нулевой шаг продвинул коррекцию");
                float elapsed=0;
                for(int i=0;i<fps/2;i++)
                {
                    float before=runner.poseTime;ActorPose.Advance(runner,1f/fps);elapsed+=1f/fps;
                    Require(runner.poseTime>before,"Поздняя поза остановила run при FPS="+fps);
                }
                Require(runner.poseCorrectionRemaining==0 && Math.Abs(runner.poseTime-(.9f+elapsed))<.00001f,
                    "Фаза не сошлась с исходным временем при FPS="+fps);
            }
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException(message);}
    }
}
