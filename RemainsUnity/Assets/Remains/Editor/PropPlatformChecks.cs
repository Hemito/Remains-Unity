using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropPlatformChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {left=-10,right=10,bottom=0,top=10,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                new RoomSurface {x=-10,y=-1,width=20,height=1},
                new RoomSurface {x=-5,y=2.9f,width=10,height=.1f,oneWay=true},
                new RoomSurface {x=2,y=0,width=.5f,height=9}}};
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            foreach(bool container in new[]{false,true})
            {
                PropState prop=container?new ContainerState():new PropState();
                prop.movable=true;prop.width=.8f;prop.height=.8f;prop.anchorHeight=container?0:.4f;
                prop.Position=new Vector2(0,4+prop.anchorHeight);
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) PropMotion.Step(prop,room,dt,null);
                Require(Mathf.Abs(prop.y-prop.anchorHeight-3)<.001f,"Свободный предмет не остался на платформе");
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) PropMotion.Step(prop,room,dt,new Vector2(0,1+prop.anchorHeight));
                Require(prop.y-prop.anchorHeight<1.01f,"Удерживаемый предмет не спустился сквозь платформу");
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) PropMotion.Step(prop,room,dt,new Vector2(0,4+prop.anchorHeight));
                Require(prop.y-prop.anchorHeight>3.99f,"Повторный подъём заблокирован платформой");
                prop.vx=prop.vy=0;
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) PropMotion.Step(prop,room,dt,null);
                Require(Mathf.Abs(prop.y-prop.anchorHeight-3)<.001f,"После отпускания потеряна опора платформы");
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) PropMotion.Step(prop,room,dt,new Vector2(6,-2));
                Require(prop.x<=1.6001f && prop.y-prop.anchorHeight>=-.0001f,"Спуск прошёл сквозь сплошные стену или пол");
            }
            Debug.Log("Предметы на платформах: PASS — свободная опора, спуск/подъём телекинезом, возврат опоры, стены и пол, ящик/контейнер при 30/60/144 FPS.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка платформ предметов: "+reason);}
    }
}
