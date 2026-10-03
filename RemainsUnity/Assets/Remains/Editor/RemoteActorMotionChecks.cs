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
            var motion=new RemoteActorMotion();
            var world=new WorldState();
            var actor=new ActorState {id="remote",owner="remote",player=true,connected=true,vx=8.4f,pose="run"};
            var local=new ActorState {id="local",owner="local",player=true,connected=true,x=-2};
            world.actors.Add(actor);world.actors.Add(local);
            float previous=0;
            for(int frame=0;frame<60;frame++)
            {
                if(frame%3==0) {actor.x=frame/60f*8.4f;actor.motionSequence++;}
                motion.Step(world,"local",1f/60,_=>false);
                Require(Mathf.Abs(actor.x-previous-.14f)<.001f,"Равномерный поток 20 Гц не дал равномерного движения 60 Гц");
                Require(local.x==-2,"Изменено локально управляемое тело");previous=actor.x;
            }
            float lostAt=actor.x;
            for(int i=0;i<120;i++) motion.Step(world,"local",1f/60,_=>false);
            Require(actor.x-lostAt<=8.4f*RemoteActorMotion.PredictionSeconds+.001f,"Потеря пакетов вызывает неограниченный прогноз");
            float stopped=actor.x;motion.Step(world,"local",1,_=>false);
            Require(Mathf.Abs(actor.x-stopped)<.001f,"Прогноз не остановился после горизонта");
            actor.motionSequence++;actor.x=20;actor.vx=0;motion.Step(world,"local",1f/60,_=>false);
            Require(actor.x==20,"Телепорт воспроизведён проходом через комнату");
            actor.motionSequence++;actor.vx=4;motion.Step(world,"local",1,_=>true);
            Require(actor.x==20,"Пауза двигает чужое тело");
            actor.owner="local";actor.x=3;motion.Step(world,"local",.1f,_=>false);
            Require(actor.x==3,"После передачи владения осталась чужая проекция");
            actor.owner="remote";actor.level="hub";actor.x=4;actor.vx=0;actor.generation++;
            motion.Step(world,"local",.1f,_=>false);Require(actor.x==4,"Смена локации/поколения сохранила старую траекторию");
            var canonical=world.Copy();var replica=canonical.Copy();replica.actors[0].vx=5;
            motion.Clear();motion.Step(replica,"local",.05f,_=>false);
            Require(canonical.actors[0].x==4 && replica.actors[0].x>4,"Проекция изменила независимый канонический снимок");
            Debug.Log("Удалённое движение: PASS — 20/60 Гц, предел прогноза, пауза, телепорт, владение, независимый снимок.");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Удалённое движение: "+message);}
    }
}
