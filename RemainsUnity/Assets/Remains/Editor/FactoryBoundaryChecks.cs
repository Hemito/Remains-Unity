using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryBoundaryChecks
    {
        public static int Check(RoomAuthoring room)
        {
            var group=room.transform.Find(FactoryBoundaryAuthoring.GroupName);
            Require(group!=null,"Нет сохранённого внешнего контура.");
            var layout=room.CreateLayout();
            Require(RoomBoundaryGaps.Find(layout,FactoryBoundaryAuthoring.Thickness,FactoryBoundaryAuthoring.Texture,"normal").Length==0,
                "Внешний контур оставляет разрыв или одностороннюю опору вместо стены.");
            var surfaces=group.GetComponentsInChildren<RoomSurfaceAuthoring>();
            Require(surfaces.Length>0,"Пустой внешний контур.");
            foreach(var part in surfaces)
            {
                var surface=part.Read(room);var artwork=part.GetComponentsInChildren<RoomArtworkAuthoring>();
                Require(artwork.Length==1,"Нет единственного оформления участка: "+part.name);
                var art=artwork[0].Read(room);
                Require(Mathf.Abs(art.x-surface.x)+Mathf.Abs(art.y-surface.y)+Mathf.Abs(art.width-surface.width)+Mathf.Abs(art.height-surface.height)<.001f &&
                    art.texture==surface.texture && !surface.oneWay && !surface.shotsPassThrough,"Оформление расходится с физической стеной: "+part.name);
                Require(surface.x+surface.width<=layout.left+FactoryBoundaryAuthoring.Thickness+.001f ||
                    surface.x>=layout.right-FactoryBoundaryAuthoring.Thickness-.001f ||
                    surface.y+surface.height<=layout.bottom+FactoryBoundaryAuthoring.Thickness+.001f ||
                    surface.y>=layout.top-FactoryBoundaryAuthoring.Thickness-.001f,"Участок оказался внутри комнаты.");
            }
            foreach(float fps in new[]{30,60,144})
            foreach(float x in new[]{2f,36f,40f,74f})
            {
                var hull=new MovementHull {width=.5f,height=2};var position=new Vector2(x,2.2f);
                for(int i=0;i<fps*2;i++) position=ActorTerrainMotion.Move(layout,hull,position,new Vector2(0,-8/fps),true);
                Require(position.y>=layout.bottom+FactoryBoundaryAuthoring.Thickness-.001f,"Падение прошло сквозь внешний пол.");
            }
            var empty=new RoomLayout {left=0,right=10,bottom=0,top=6,platforms=Array.Empty<RoomSurface>()};
            var seals=RoomBoundaryGaps.Find(empty,1,"wall","normal");
            Require(seals.Length==4,"Пустой контур не закрыт четырьмя полосами.");empty.platforms=seals;
            Require(RoomBoundaryGaps.Find(empty,1,"wall","normal").Length==0,"Повторный расчёт снова создаёт стены.");
            var shelf=new RoomLayout {left=0,right=10,bottom=0,top=6,platforms=new[]{new RoomSurface {x=0,y=0,width=10,height=1,oneWay=true}}};
            Require(RoomBoundaryGaps.Find(shelf,1,"wall","normal").Any(s=>s.y==0 && s.width==10),"Односторонняя балка скрыла отверстие пола.");
            Debug.Log("Внешний контур завода: PASS — замкнутость, единая геометрия/оформление, отсутствие внутренней стены, падение при 30/60/144 FPS.");
            return surfaces.Length;
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException("Контур завода: "+message);}
    }
}
