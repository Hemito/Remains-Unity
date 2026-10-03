using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class RemoteActorMotionChecks
    {
        public static void Run()
        {
            foreach(int fps in new[]{30,60,144}) CheckJitter(fps);
            var motion=new RemoteActorMotion();
            var world=new WorldState();
            var actor=new ActorState {id="remote",owner="remote",player=true,connected=true,vx=8.4f,pose="run"};
            var local=new ActorState {id="local",owner="local",player=true,connected=true,x=-2};
            world.actors.Add(actor);world.actors.Add(local);
            double now=0;
            void Step(float dt,bool paused=false) {motion.Step(world,"local",dt,_=>paused,now);now+=dt;}
            float previous=0;
            for(int frame=0;frame<60;frame++)
            {
                if(frame%3==0) {actor.x=frame/60f*8.4f;Stamp(actor,now);}
                Step(1f/60);
                Require(Mathf.Abs(actor.x-previous-.14f)<.001f,"Равномерный поток 20 Гц не дал равномерного движения 60 Гц");
                Require(local.x==-2,"Изменено локально управляемое тело");previous=actor.x;
            }
            float lostAt=actor.x;
            for(int i=0;i<120;i++) Step(1f/60);
            Require(actor.x-lostAt<=8.4f*RemoteActorMotion.PredictionSeconds+.001f,"Потеря пакетов вызывает неограниченный прогноз");
            float stopped=actor.x;Step(1);
            Require(Mathf.Abs(actor.x-stopped)<.001f,"Прогноз не остановился после горизонта");
            Stamp(actor,now);actor.x=20;actor.vx=0;Step(1f/60);
            Require(actor.x==20,"Телепорт воспроизведён проходом через комнату");
            Stamp(actor,now);actor.vx=4;Step(1,true);
            Require(actor.x==20,"Пауза двигает чужое тело");
            actor.owner="local";actor.x=3;Step(.1f);
            Require(actor.x==3,"После передачи владения осталась чужая проекция");
            actor.owner="remote";actor.level="hub";actor.x=4;actor.vx=0;actor.generation++;
            Step(.1f);Require(actor.x==4,"Смена локации/поколения сохранила старую траекторию");
            var canonical=world.Copy();var replica=canonical.Copy();replica.actors[0].vx=5;
            Stamp(replica.actors[0],now);motion.Clear();motion.Step(replica,"local",.05f,_=>false,now);
            Require(canonical.actors[0].x==4 && replica.actors[0].x>4,"Проекция изменила независимый канонический снимок");
            Debug.Log("Удалённое движение: PASS — 20/60 Гц, предел прогноза, пауза, телепорт, владение, независимый снимок.");
        }
        private static void Stamp(ActorState actor,double now)
        {actor.motionSequence++;actor.motionSentAt=now;actor.motionClockGeneration=actor.generation;}
        private static void CheckJitter(int fps)
        {
            var world=new WorldState();
            var actor=new ActorState {id="jitter",owner="remote",player=true,connected=true,vx=8.4f,pose="run"};
            world.actors.Add(actor);
            var packets=new System.Collections.Generic.List<(int delivery,int source,uint sequence)>();
            var schedule=new Remains.Networking.ReplicationCadence(.05);
            uint sequence=0;
            for(int frame=0;frame<fps*4;frame++)
                if(schedule.TryAdvance((double)frame/fps))
                {sequence++;packets.Add((frame+(sequence%2==0?(int)Math.Ceiling(fps*.03):0),frame,sequence));}
            var motion=new RemoteActorMotion();
            float maximumError=0;
            for(int frame=0;frame<fps*4;frame++)
            {
                foreach(var packet in packets)
                {
                    if(packet.delivery!=frame || !ActorMotionReplication.IsNewer(packet.sequence,actor.motionSequence)) continue;
                    actor.x=8.4f*packet.source/fps;actor.motionSequence=packet.sequence;
                    // Deliberately unrelated sender/receiver clock origins.
                    actor.motionSentAt=604800d+(double)packet.source/fps;actor.motionClockGeneration=actor.generation;
                }
                motion.Step(world,"local",1f/fps,_=>false,1000d+(double)frame/fps);
                maximumError=Mathf.Max(maximumError,Mathf.Abs(actor.x-8.4f*(frame+1)/fps));
            }
            Require(maximumError<.002f,$"Запоздавший пакет тормозит ровный бег: fps={fps}, error={maximumError}");
            // Stop and reverse are genuine source changes, not delivery jitter.
            double now=1004;
            actor.x=33.6f;actor.vx=0;Stamp(actor,604804);
            for(int i=0;i<fps;i++) motion.Step(world,"local",1f/fps,_=>false,now+(double)i/fps);
            Require(Mathf.Abs(actor.x-33.6f)<.002f,"Остановка источника не остановила проекцию");
            actor.generation++;actor.motionSequence++;actor.x=2;actor.vx=-8.4f;
            motion.Step(world,"local",1f/fps,_=>false,1005);
            Require(actor.x==2,"До первой публикации новый владелец использовал чужие часы");
            Stamp(actor,5);motion.Step(world,"local",1f/fps,_=>false,1005+1d/fps);
            Require(actor.x<2 && actor.x>=2-8.4f/fps-.002f,"Новый владелец не начал движение от собственного времени");
            Debug.Log($"Временные отметки движения: PASS — fps={fps}, maxError={maximumError}, задержка чередующихся пакетов, остановка, новые часы владельца.");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Удалённое движение: "+message);}
    }
}
