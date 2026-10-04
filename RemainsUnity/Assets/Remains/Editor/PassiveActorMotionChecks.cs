using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class PassiveActorMotionChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {id="passive-check",left=-10,right=10,top=20,
                platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}},
                ladders=new[]{new RoomLadder {x=0,bottom=0,top=10}}};
            var settings=MovementSettings.Load();
            foreach(int fps in new[]{30,60,144})
            {
                float dt=1f/fps;
                var body=new ActorState {id="body",y=4,climbing=true,down=true,health=0};
                var untouched=body.Copy();
                PassiveActorMotion.Step(body,room,0,settings);
                Require(body.y==untouched.y && body.vy==untouched.vy,"Пауза изменила физическое состояние тела");
                var controller=new MovementController(room,settings);
                controller.Step(body,Vector2.up,true,dt,true,true);
                Require(!body.climbing && body.y<4 && body.vy<0 && body.pose=="down","Нокдаун на лестнице не начал падение или принял ввод");
                for(int i=0;i<fps*2;i++) controller.Step(body,new Vector2(1,1),true,dt,true,true);
                Require(Mathf.Abs(body.y)<.0001f && body.vy==0 && body.x==0 && body.down,"Тело не остановилось на полу или управляется клавишами");
                body.down=false;body.health=35;controller.Step(body,Vector2.right,false,dt);
                Require(body.x>0,"После поднятия управление не восстановилось");
                var moving=new ActorState {id="momentum",y=5,vx=4,vy=3,down=true};
                PassiveActorMotion.Step(moving,room,dt,settings);
                Require(moving.x>0 && moving.y>5 && moving.vy<3,"Нокдаун уничтожил существующий импульс вместо физического движения");
                var offline=new ActorState {id="offline",player=true,connected=true,health=11,y=4,climbing=true};
                ActorSessionRules.Disconnect(offline);
                for(int i=0;i<fps*2;i++) controller.Step(offline,Vector2.up,true,dt,true,true);
                Require(offline.y==0 && offline.vy==0 && !offline.down && offline.health==11 && ActorPose.Clip(offline)=="down",
                    "Отсутствующее живое тело перестало падать, приняло ввод или потеряло HP");
                ActorSessionRules.Connect(offline,"returned");controller.Step(offline,Vector2.right,false,dt);
                Require(offline.x>0,"Возвращение живого игрока не вернуло управление физическому телу");
            }
            Debug.Log("Физика тела: PASS — падение с лестницы, пол, отсутствие управления, импульс и поднятие при 30/60/144 FPS.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException(reason);}
    }
}
