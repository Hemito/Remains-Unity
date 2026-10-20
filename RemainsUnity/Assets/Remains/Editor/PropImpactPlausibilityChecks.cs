using System;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropImpactPlausibilityChecks
    {
        public static void Run()
        {
            int loads=0;var room=new RoomLayout {left=-20,right=20,bottom=-5,top=100};
            var check=new PropImpactPlausibility(12,14,_=>{loads++;return room;});
            var prop=new PropState {id="admission-box",level="l",mass=50,width=1,height=1,x=0,y=4,vx=6};
            var sample=new PropImpactSample {id="sample",level="l",x=.2f,y=4,vx=6,vy=0};
            string clean=StateJson.ToJson(prop);
            Require(check.Sample(prop,sample,out _),"Допустимый движущийся источник отклонён");
            sample.vx=1e6f;Require(!check.Sample(prop,sample,out var reason)&&!string.IsNullOrEmpty(reason),"Миллион м/с принят");
            sample.vx=6;sample.x=15;Require(!check.Sample(prop,sample,out _),"Телепорт образца относительно канонической позы принят");
            sample.x=100;Require(!check.Sample(prop,sample,out _),"Образец за границами уровня принят");
            sample.x=0;sample.vx=80;Require(!check.Sample(prop,sample,out _),"Мгновенный разгон от последней позы принят");
            prop.vx=80;Require(check.Sample(prop,sample,out _),"Принятый быстрый выпуск телекинеза отклонён");
            sample.vx=0;sample.vy=-Mathf.Sqrt(2*WorldPhysics.Gravity*100);prop.vx=0;prop.vy=sample.vy;
            Require(check.Sample(prop,sample,out _),"Допустимое падение полного уровня отклонено");
            prop.vx=6;prop.vy=0;sample.vx=6;sample.vy=0;
            Require(StateJson.ToJson(prop)==clean,"Проверка admission изменила физическое тело");
            var target=new ActorState {id="victim",level="l",vx=2,vy=-1};
            Require(check.Target(target,new Vector2(2.2f,-1),1,out var canonical,out _)&&canonical==target.Velocity,
                "Импульс использует сообщение вместо канонической скорости жертвы");
            Require(!check.Target(target,new Vector2(1e6f,0),1,out _,out _),"Поддельная скорость жертвы принята");
            Require(!check.Target(target,new Vector2(100,0),1,out _,out _),"Скорость жертвы не сверена с принятой позой");
            Require(!check.Target(target,new Vector2(float.NaN,0),1,out _,out _),"NaN жертвы принят");
            Require(loads==1,"Admission повторно загружает геометрию на каждый образец");
            Debug.Log("Правдоподобие Box: PASS — предел из физики/TK, принятая поза, target velocity, NaN/teleport/миллион м/с без мутаций; кеш уровня.");
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
