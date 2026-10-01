using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class PlatformDropChecks
    {
        public static void Run()
        {
            Anatomy.Load();
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var room=new RoomLayout {left=-10,right=10,bottom=-1,top=10,
                    ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                        new RoomSurface {x=-10,y=-1,width=20,height=1},
                        new RoomSurface {x=-3,y=1,width=6,height=.1f,oneWay=true},
                        new RoomSurface {x=-3,y=3,width=6,height=.1f,oneWay=true}}};
                var actor=new ActorState {id="drop",x=0,y=3.1f};
                var movement=new MovementController(room);
                for(int i=0;i<30;i++) movement.Step(actor,Vector2.down,false,dt);
                Require(Mathf.Abs(actor.y-3.1f)<.001f,"Удержание S не проваливает платформу");
                movement.Step(actor,Vector2.zero,false,dt);
                movement.Step(actor,Vector2.down,false,dt);
                Require(actor.y<3.1f && actor.vy<0,"Повторное нажатие начинает спуск");
                for(int i=0;i<120;i++) movement.Step(actor,Vector2.down,false,dt);
                Require(Mathf.Abs(actor.y-1.1f)<.001f,"Спуск останавливается на следующей платформе");
                movement.Step(actor,Vector2.zero,false,dt);
                movement.Step(actor,Vector2.down,false,dt);
                for(int i=0;i<120;i++) movement.Step(actor,Vector2.down,false,dt);
                Require(Mathf.Abs(actor.y)<.001f,"Повторный спуск до твёрдого пола");
                movement.Step(actor,Vector2.zero,false,dt);
                movement.Step(actor,Vector2.down,false,dt);
                Require(Mathf.Abs(actor.y)<.001f,"Двойное S не проходит твёрдый пол");
                var gesture=new ReleasedTapGesture(5f/30);
                Require(!gesture.Step(true,dt),"Первое нажатие не двойное");
                gesture.Step(false,dt);gesture.Step(false,.3f);
                Require(!gesture.Step(true,dt),"Медленные нажатия не считаются двойными");
                Debug.Log($"Спуск через платформу: PASS, dt={dt}, следующая опора и твёрдый пол сохранены.");
            }
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException("Проверка спуска: "+message);}
    }
}
