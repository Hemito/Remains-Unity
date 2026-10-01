using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class CrouchChecks
    {
        public static void Run()
        {
            foreach(int rate in new[]{30,60,144})
            {
                float dt=1f/rate;
                var room=new RoomLayout {id="crouch-check",left=-20,right=20,bottom=0,top=8,
                    platforms=new[]{new RoomSurface {x=-20,y=-1,width=40,height=1},new RoomSurface {x=1,y=.9f,width=3,height=.3f}},ladders=Array.Empty<RoomLadder>()};
                var actor=new ActorState {id="crouch-check"};var motion=new MovementController(room);
                for(int i=0;i<rate/4;i++) motion.Step(actor,Vector2.down,false,dt);
                Require(actor.crouched && actor.pose=="crouch" && actor.y==0,"S не включает приседание на месте.");
                motion.Step(actor,Vector2.zero,false,dt);Require(actor.crouched,"Отпускание S отменило приседание.");
                for(int i=0;i<rate;i++) motion.Step(actor,Vector2.right,false,dt,true);
                Require(actor.crouched && actor.pose=="crawl" && Mathf.Abs(actor.vx-2.1f)<.001f && actor.x>1.9f,"Ползание не проходит под потолком или ускоряется от Shift.");
                motion.Step(actor,Vector2.up,true,dt,false,true);
                Require(actor.crouched && actor.y==0,"Под низким потолком разрешено встать/прыгнуть.");
                var restored=actor.Copy();var destination=new ActorState();ActorPose.CopyMotion(actor,destination);
                Require(restored.crouched && destination.crouched && Anatomy.Movement(restored).height==.7f,"Стойка потеряна при копировании/сохранении.");
                Require(!SavedActorPlacement.Repair(restored,room,"проверка стойки"),"Загрузка присевшего актёра переносит его из свободного прохода.");
                var standing=new ActorState {x=actor.x,y=actor.y};
                Require(WeaponPose.RangedAnchor(actor).y<WeaponPose.RangedAnchor(standing).y && WeaponPose.Origin(actor).y<WeaponPose.Origin(standing).y,"Оружие не следует за стойкой.");
                for(int i=0;i<rate*2;i++) motion.Step(actor,Vector2.right,false,dt);
                motion.Step(actor,Vector2.up,false,dt);Require(!actor.crouched,"W не поднимает на свободном месте.");
                for(int i=0;i<rate/4;i++) motion.Step(actor,Vector2.down,false,dt);
                motion.Step(actor,Vector2.zero,true,dt,false,true);Require(!actor.crouched && actor.vy>0,"Прыжок не поднимает из приседания.");
            }
            Debug.Log("Приседание: PASS — S/W/прыжок, скорость, габариты, потолок, сохранение, оружие, 30/60/144 Гц.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
