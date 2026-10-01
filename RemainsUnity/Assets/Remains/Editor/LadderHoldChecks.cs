using System;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LadderHoldChecks
    {
        public static void Run()
        {
            var room=RoomLayout.Load();
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var actor=new ActorState {id="ladder-hold",x=4,y=1};
                var motion=new MovementController(room);
                motion.Step(actor,Vector2.up,false,dt);
                Require(actor.climbing && actor.vy>0,"Вход на лестницу");
                float height=actor.y;
                for(int i=0;i<300;i++) motion.Step(actor,Vector2.zero,false,dt);
                Require(actor.climbing && actor.pose=="climb" && actor.y==height && actor.vy==0,"Удержание без ввода");
                float time=actor.poseTime;ActorPose.Advance(actor,1);
                Require(actor.poseTime==time,"Неподвижная поза не перебирает кадры");
                var copy=StateJson.Copy(actor);
                new MovementController(room).Step(copy,Vector2.zero,false,dt);
                Require(copy.climbing && copy.y==height,"Загрузка сохраняет захват лестницы");
                motion.Step(actor,Vector2.zero,true,dt);
                Require(!actor.climbing && actor.vy>0 && actor.pose=="jump","Прыжок отпускает лестницу");
                actor=new ActorState {id="ladder-side",x=4,y=1,climbing=true};
                for(int i=0;i<60;i++) motion.Step(actor,Vector2.right,false,dt);
                Require(!actor.climbing && actor.x>4.65f,"Боковой выход отпускает лестницу");
                actor=new ActorState {id="ladder-down",x=4,y=1,climbing=true,down=true};
                motion.Step(actor,Vector2.zero,false,dt);
                Require(!actor.climbing,"Нокдаун снимает захват лестницы");
                Debug.Log($"Удержание лестницы: PASS, dt={dt}, остановка, загрузка, прыжок и выход.");
            }
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Проверка лестницы: "+reason);}
    }
}
