using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropStackChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {left=-10,right=10,bottom=0,top=10,
                ladders=Array.Empty<RoomLadder>(),platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
            foreach(int fps in new[]{30,60,144})
            foreach(bool reverse in new[]{false,true})
            {
                var bottom=new PropState {id="bottom",owner="remote",y=.3f,width=1,height=.6f,anchorHeight=.3f};
                var middle=new ContainerState {id="middle",owner="local",x=0,y=2,width=.8f,height=.8f,anchorHeight=0};
                var top=new PropState {id="top",owner="local",y=4,width=.6f,height=.6f,anchorHeight=.3f};
                PropState[] objects=reverse?new PropState[]{top,middle,bottom}:new PropState[]{bottom,middle,top};
                for(int i=0;i<fps*4;i++)
                {
                    foreach(var item in objects)
                        if(item.owner=="local") PropMotion.Step(item,room,1f/fps,null,objects);
                }
                Require(Mathf.Abs(middle.y-.6f)<.001f && Mathf.Abs(top.y-1.7f)<.001f,$"Три объекта не образовали устойчивую стопку: fps={fps}, reverse={reverse}, middle={middle.y}, top={top.y}");
                Require(bottom.owner=="remote" && bottom.y==.3f,"Изменена чужая опора");
                bottom.x=4;
                for(int i=0;i<fps*4;i++)
                    foreach(var item in objects)
                        if(item.owner=="local") PropMotion.Step(item,room,1f/fps,null,objects);
                Require(Mathf.Abs(middle.y)<.001f && Mathf.Abs(top.y-1.1f)<.001f,"Стопка не опустилась после удаления основания");
                middle.level="hub";
                for(int i=0;i<fps*2;i++) PropMotion.Step(top,room,1f/fps,null,objects);
                Require(Mathf.Abs(top.y-.3f)<.001f,"Объект опирается на другую локацию");
            }
            Debug.Log("Штабелирование: PASS — ящик/контейнер/ящик, чужая опора, удаление основания, разные локации и порядок обхода при 30/60/144 FPS.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Проверка штабелирования: "+reason);}
    }
}
