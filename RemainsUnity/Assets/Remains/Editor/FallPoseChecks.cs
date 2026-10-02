using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;
namespace Remains.Editor
{
    public static class FallPoseChecks
    {
        public static void Run()
        {
            Anatomy.Load();
            var room=new RoomLayout {left=-10,right=10,bottom=0,top=20,ladders=Array.Empty<RoomLadder>(),platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var movement=new MovementController(room);
                var actor=new ActorState {id="fall-check",y=5,pose="idle"};
                movement.Step(actor,Vector2.zero,false,dt);
                Require(actor.pose=="fall","Потеря опоры не выбрала падение");
                var copy=JsonUtility.FromJson<ActorState>(JsonUtility.ToJson(actor));
                new MovementController(room).Step(copy,Vector2.zero,false,dt);
                Require(copy.pose=="fall","Восстановление состояния потеряло падение");
                actor=new ActorState {id="jump-check"};
                movement.Step(actor,Vector2.zero,true,dt);
                Require(actor.pose=="jump","Запуск прыжка выбрал падение");
                for(int i=0;i<Mathf.CeilToInt(.5f/dt);i++) movement.Step(actor,Vector2.zero,false,dt);
                Require(actor.y>0 && actor.vy<0 && actor.pose=="jump","Нисходящая часть прыжка стала сходом с уступа");
            }
            var fall=new ActorState {pose="fall",vx=0,vy=-7.2f};
            Require(PonyMotionFrames.Sample(fall,0)==41 && PonyMotionFrames.Sample(fall,100)==41,"Вертикальная поза зависит от времени");
            fall.vx=8.4f;fall.facing=1;
            int forward=PonyMotionFrames.Sample(fall,0);fall.facing=-1;
            Require(forward==31 && PonyMotionFrames.Sample(fall,0)==0,"Поза не учитывает скорость относительно взгляда");
            fall.vx=0;fall.vy=0;StanceAnimation.Select(fall,"idle",false);
            Require(fall.pose=="landing","Нет приземления после падения");
            var raider=new ActorState {anatomy="raider"};
            Require(AirbornePose.Select(raider,false,true)=="jump","Рейдеру назначен клип пони");
        }
        private static void Require(bool value,string message)
        {if(!value) throw new InvalidOperationException("Позы падения: "+message);}
    }
}
