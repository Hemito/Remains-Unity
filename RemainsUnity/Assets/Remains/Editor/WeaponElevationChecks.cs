using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponElevationChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {id="raise-check",platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}},ladders=Array.Empty<RoomLadder>()};
            foreach(string weapon in new[]{"pistol","energy"})
            foreach(float dt in new[]{1f/30,1f/60,1f/120})
            {
                var actor=new ActorState {id="raising-player",level=room.id,weapon=weapon};
                var gesture=new WeaponElevation();
                var target=new Vector2(6,2);
                gesture.Step(actor,room,true,WeaponRigs.RaiseDelay*.5f);
                Require(actor.weaponLift==0,"Короткое нажатие поднимает оружие");
                for(float elapsed=0;elapsed<WeaponRigs.RaiseDelay;elapsed+=dt) gesture.Step(actor,room,true,dt);
                Require(actor.weaponLift==WeaponRigs.RaiseHeight,"Длительное нажатие не поднимает оружие");
                WeaponPose.Aim(actor,target,room);
                Require(Mathf.Abs(WeaponPose.Mount(actor).y-WeaponRigs.AnchorHeight-WeaponRigs.RaiseHeight)<.0001f,"Неверная высота поднятого оружия");
                Require(Mathf.Abs(Vector3.Cross(target-WeaponPose.Muzzle(actor),WeaponPose.Direction(actor)).z)<.0001f,"Поднятый ствол не направлен в цель");
                var copy=new ActorState();ActorPose.CopyMotion(actor,copy);
                Require(copy.weaponLift==actor.weaponLift && Vector2.Distance(WeaponPose.Muzzle(copy),WeaponPose.Muzzle(actor))<.0001f,"Копирование движения потеряло поднятое дуло");
                Require(actor.Copy().weaponLift==actor.weaponLift,"Копия состояния потеряла подъём");
                gesture.Step(actor,room,false,dt);
                Require(actor.weaponLift==0,"Отпускание вверх не опустило оружие");
                actor.climbing=true;gesture.Step(actor,room,true,1);
                Require(actor.weaponLift==0,"Лестница разрешила подъём");
                actor.climbing=false;actor.y=2;gesture.Step(actor,room,true,1);
                Require(actor.weaponLift==0,"В воздухе разрешён подъём");
                actor.y=0;actor.weapon="crowbar";gesture.Step(actor,room,true,1);
                Require(actor.weaponLift==0,"Жест огнестрела изменяет ближнее оружие");
                actor.weapon=weapon;gesture.Step(actor,room,true,1);actor.down=true;gesture.Step(actor,room,true,1);
                Require(actor.weaponLift==0,"Нокаут сохранил подъём");
            }
            var blocked=new ActorState {id="blocked-player",level=room.id,weapon="pistol"};
            var elevation=new WeaponElevation();elevation.Step(blocked,room,true,1);
            room.platforms=new[]{room.platforms[0],new RoomSurface {x=-5,y=1.5f,width=10,height=.1f}};
            WeaponPose.Aim(blocked,new Vector2(6,1),room);
            Require(blocked.weaponLift==0,"Оружие поднялось сквозь потолок");
            room.platforms[1].oneWay=true;elevation.Step(blocked,room,true,1);WeaponPose.Aim(blocked,new Vector2(6,1),room);
            Require(blocked.weaponLift>0,"Открытая балка запрещает подъём");
            room.platforms=new[]{room.platforms[0],new RoomSurface {x=-.1f,y=1.5f,width=.2f,height=.1f},
                new RoomSurface {x=.6f,y=1.7f,width=.1f,height=1}};
            elevation.Step(blocked,room,true,1);WeaponPose.Aim(blocked,new Vector2(6,2),room);
            Require(blocked.weaponReach==0 && blocked.weaponLift==0,"Возврат от стены перенёс поднятое оружие сквозь край потолка");
            Debug.Log("Подъём огнестрела: PASS — задержка, отпускание, лестница, воздух, потолок, балка и копирование позы.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
