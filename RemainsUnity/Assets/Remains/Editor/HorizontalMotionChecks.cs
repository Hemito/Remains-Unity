using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class HorizontalMotionChecks
    {
        public static void Run()
        {
            var settings=MovementSettings.Load();
            CheckSprintPreparation(settings);
            foreach(int rate in new[]{30,60,144})
            {
                float dt=1f/rate,velocity=0;
                for(int i=0;i<rate;i++) velocity=HorizontalMotion.Step(velocity,1,true,true,dt,settings);
                Require(Mathf.Abs(velocity-8.4f)<.001f,"Бег не достигает базовой скорости");
                float distance=0,elapsed=0;
                while(velocity>0 && elapsed<1)
                {
                    velocity=HorizontalMotion.Step(velocity,0,true,false,dt,settings);distance+=velocity*dt;elapsed+=dt;
                }
                Require(velocity==0 && elapsed<.28f && distance>.8f && distance<1.05f,"Неверная остановка после бега");
                velocity=8.4f;
                for(int i=0;i<rate/3;i++) velocity=HorizontalMotion.Step(velocity,0,false,false,dt,settings);
                Require(velocity>7 && velocity<8.4f,"В воздухе потеряна инерция или отсутствует торможение");
                velocity=0;
                for(int i=0;i<rate/3;i++) velocity=HorizontalMotion.Step(velocity,-1,false,true,dt,settings);
                Require(velocity<-1.8f && velocity>-2.2f,"Управление в воздухе не ослаблено");
            }
            var room=RoomLayout.Load();var controller=new MovementController(room);
            var actor=new ActorState {x=0};
            for(int i=0;i<20;i++) controller.Step(actor,Vector2.right,false,1f/60,true);
            controller.Step(actor,Vector2.right,true,1f/60,true);
            Require(actor.vx>8 && actor.vy>0,"Прыжок потерял горизонтальную скорость бега");
            var resumed=actor.Copy();var resumedController=new MovementController(room);
            controller.Step(actor,Vector2.zero,false,1f/60);resumedController.Step(resumed,Vector2.zero,false,1f/60);
            Require(Vector2.Distance(actor.Position,resumed.Position)<.0001f && actor.vx==resumed.vx,"Передача состояния сбросила инерцию");
            Debug.Log("Инерция: PASS — разгон, остановка, воздух при 30/60/144 Гц и продолжение после копирования.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
        private static void CheckSprintPreparation(Remains.Content.MovementSettings settings)
        {
            foreach(int rate in new[]{30,60,144})
            {
                var preparation=new SprintPreparation();var actor=new ActorState {id="sprint-timing"};
                float available=0;
                for(int i=0;i<rate;i++) available+=preparation.AccelerationTime(actor,1,true,1f/rate,settings.sprintPreparationSeconds);
                Require(Mathf.Abs(available-(1-settings.sprintPreparationSeconds))<.00001f,"Подготовка бега зависит от частоты симуляции");
                preparation.AccelerationTime(actor,0,false,.01f,settings.sprintPreparationSeconds);
                Require(preparation.AccelerationTime(actor,1,true,.05f,settings.sprintPreparationSeconds)==0,"Отпускание направления не сбросило подготовку");
                preparation.AccelerationTime(actor,1,false,.2f,settings.sprintPreparationSeconds);
                Require(Mathf.Abs(preparation.AccelerationTime(actor,-1,true,.05f,settings.sprintPreparationSeconds)-.05f)<.00001f,
                    "После ходьбы или смены направления появилась повторная задержка");
                actor.generation++;
                Require(preparation.AccelerationTime(actor,1,true,.05f,settings.sprintPreparationSeconds)==0,"Новый владелец унаследовал историю чужого ввода");
            }
            var controller=new MovementController(RoomLayout.Load());var runner=new ActorState {id="sprint-start"};
            controller.Step(runner,Vector2.right,false,.1f,true);
            Require(runner.vx==0 && runner.x==0,"Бег с места ускоряется до окончания подготовки");
            controller.Step(runner,Vector2.right,false,.1f,true);
            Require(runner.vx>0 && runner.x>0,"Бег не начинается после подготовки");
            float before=runner.vx;
            controller.Step(runner,Vector2.zero,false,.01f,true);
            Require(runner.vx<before,"Подготовка бега подавляет торможение");
        }
    }
}
