using System;
using Remains.Domain;
namespace Remains.Editor
{
    public static class ActorMotionReplicationChecks
    {
        public static void Run()
        {
            var current=new ActorState {id="pose-check",owner="a",level="hub",generation=2,lifecycleSequence=3,
                motionSequence=12,pose="run",poseTime=1.1f,x=17,vx=-8,facing=-1};
            foreach(uint sequence in new uint[]{10,12})
            {
                var snapshot=current.Copy();snapshot.motionSequence=sequence;snapshot.poseTime=1;snapshot.x=18;
                snapshot.health=23;
                ActorMotionReplication.PreserveNewerMotion(current,snapshot);
                Require(snapshot.poseTime==1.1f && snapshot.x==17 && snapshot.motionSequence==12 && snapshot.health==23,
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
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException(message);}
    }
}
