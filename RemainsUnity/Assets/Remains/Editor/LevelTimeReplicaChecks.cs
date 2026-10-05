using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class LevelTimeReplicaChecks
    {
        public static void Run()
        {
            var replica=new LevelTimeReplica();
            var clocks=new[]{new LevelTimeStamp {level="factory:0",elapsedSeconds=12,running=true},
                new LevelTimeStamp {level="hub",elapsedSeconds=7,running=false}};
            Require(replica.Receive("world",1,100,clocks),"initial epoch");
            Require(Math.Abs(replica.Read("world","factory:0",100.3)-12.3)<1e-9 && replica.Read("world","hub",101)==7,"latency extrapolation/frozen independent level");
            clocks[0].elapsedSeconds=999;Require(replica.Read("world","factory:0",100)==12,"no mutable message reference");
            Require(!replica.Receive("world",1,103,clocks) && replica.Read("world","factory:0",100)==12,"duplicate ignored");
            Require(!replica.Receive("world",0,103,clocks),"out of order ignored");
            replica.Receive("world",2,100.5,new[]{new LevelTimeStamp {level="factory:0",elapsedSeconds=12.5,running=false}});
            Require(replica.Read("world","factory:0",110)==12.5,"pause received late freezes at canonical time");
            replica.Receive("world",3,110,new[]{new LevelTimeStamp {level="factory:0",elapsedSeconds=12.5,running=true}});
            Require(replica.Read("world","factory:0",111)==13.5 && replica.Read("world","factory:0",109)==12.5,"resume and negative network offset clamp");
            Reject(()=>replica.Receive("world",4,112,new[]{new LevelTimeStamp {level="factory:0",elapsedSeconds=-1}}));
            Reject(()=>replica.Receive("world",4,112,new[]{new LevelTimeStamp {level="x"},new LevelTimeStamp {level="x"}}));
            Require(replica.Sequence==3 && replica.Read("world","factory:0",111)==13.5,"invalid packet leaves state intact");
            Reject(()=>replica.Read("world","absent",111));
            replica.Receive("other",0,200,new[]{new LevelTimeStamp {level="hub",elapsedSeconds=0}});
            Require(!replica.Has("world","factory:0") && replica.Read("other","hub",201)==0,"world switch");
            replica.Reset();Require(replica.Sequence==-1 && !replica.Has("other","hub"),"reset");
            foreach(int fps in new[]{30,60,144}) CheckPlayback(fps);
            Debug.Log("Проверки сетевых часов уровней пройдены: задержка, пауза, повторные пакеты и смена мира.");
        }
        private static void CheckPlayback(int fps)
        {
            double dt=1d/fps;
            var playback=new LevelTimePlayback();playback.Advance(12,true,dt);
            for(int frame=1;frame<=fps*4;frame++)
            {
                double previous=playback.ElapsedSeconds;
                double correction=frame<fps?.2:-.15;
                playback.Advance(12+frame*dt+correction,true,dt);
                Require(LevelTimePlayback.IsContinuous(previous,playback.ElapsedSeconds,dt) && playback.ElapsedSeconds>previous,"bounded monotonic epoch correction "+fps);
            }
            double frozen=playback.ElapsedSeconds;
            playback.Advance(frozen-.3,false,10);
            Require(playback.ElapsedSeconds==frozen,"late pause freezes current local sample "+fps);
            playback.Advance(frozen+.5,true,dt);
            Require(LevelTimePlayback.IsContinuous(frozen,playback.ElapsedSeconds,dt),"resume does not replay transport delay "+fps);
            Reject(()=>playback.Advance(-1,true,dt));
            Require(!LevelTimePlayback.IsContinuous(frozen,frozen+1,dt),"historical interval remains rejected "+fps);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("LevelTimeReplica: "+reason);}
        private static void Reject(Action action)
        {try {action();} catch(InvalidOperationException) {return;}throw new InvalidOperationException("LevelTimeReplica: ожидалось отклонение.");}
    }
}
