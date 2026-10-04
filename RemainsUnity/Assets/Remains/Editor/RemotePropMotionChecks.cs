using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class RemotePropMotionChecks
    {
        public static void Run()
        {
            foreach(int fps in new[]{30,60,144})
            {
                var prop=new ContainerState {id="remote-box",owner="remote",x=0,y=2,generation=1};
                prop.items.Add(SharedInventory.Create("heal",3));
                var motion=new RemotePropMotion();float dt=1f/fps;
                var initial=motion.Sample(prop,"local",false,dt);
                prop.x=1;
                string saved=Remains.Serialization.StateJson.ToJson(prop);
                var first=motion.Sample(prop,"local",false,dt);
                Require(first.x>initial.x && first.x<prop.Center.x,"Пакет позиции отображается скачком");
                var second=motion.Sample(prop,"local",false,dt);
                Require(second.x>first.x && second.x<prop.Center.x,"Нет движения между пакетами");
                Require(motion.Sample(prop,"local",true,dt)==second,"Пауза не остановила изображение");
                for(int i=0;i<fps;i++)
                {
                    var shown=motion.Sample(prop,"local",false,dt);
                    Require(shown.x<=prop.Center.x,"Изображение вышло за подтверждённую позицию");
                }
                Require(saved==Remains.Serialization.StateJson.ToJson(prop),"Сглаживание изменило физику или вещи");
                prop.y=0;
                for(int i=0;i<fps;i++) Require(motion.Sample(prop,"local",false,dt).y>=prop.Center.y,"Падение прошло ниже подтверждённого пола");
                prop.owner="local";prop.generation++;prop.x=2;
                Require(motion.Sample(prop,"local",false,dt)==prop.Center,"Передача локального управления сохранила задержку");
                prop.owner="remote";prop.generation++;motion.Sample(prop,"local",false,dt);prop.x=12;
                Require(motion.Sample(prop,"local",false,dt)==prop.Center,"Телепорт изображается обычным движением");
                prop.level="hub";prop.x=11;
                Require(motion.Sample(prop,"local",false,dt)==prop.Center,"Другой уровень унаследовал старую позицию");
            }
            Debug.Log("Изображение удалённых предметов: PASS — 30/60/144 FPS, движение между пакетами, пауза, пол, передача и сохранность состояния.");
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Сглаживание предметов: "+message);}
    }
}
