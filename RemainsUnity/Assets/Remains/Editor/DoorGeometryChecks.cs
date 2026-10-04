using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorGeometryChecks
    {
        public static void Run()
        {
            int loads=0;
            var geometry=new SimulationGeometry(level=>
            {
                loads++;return new RoomLayout {id=level,left=-10,right=10,bottom=0,top=8,
                    platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1,texture="floor"}},
                    ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>()};
            });
            var world=new WorldState();
            var actor=new ActorState {id="door-walker",player=true,connected=true,level="factory:0",x=0,y=0};
            world.actors.Add(actor);world.actors.Add(new ActorState {id="hub-player",player=true,connected=true,level="hub"});
            world.doors.Add(new DoorState {id="door",definition="test",level="factory:0",surfaceTexture="floor",x=2,y=0,width=.4f,height=3});
            geometry.Reconcile(world);var room=geometry.Get("factory:0");var surfaces=room.platforms;
            var controller=new MovementController(room);
            for(int i=0;i<90;i++) controller.Step(actor,Vector2.right,false,1f/60);
            Require(actor.x<2,"Игрок прошёл через закрытую дверь.");
            Require(ShotObstacles.FirstContact(room,new Vector2(0,1),new Vector2(4,1)).HasValue,"Выстрел прошёл через закрытую дверь.");
            actor.revision++;geometry.Reconcile(world);
            Require(ReferenceEquals(surfaces,room.platforms) && loads==2,"Изменение позы перестроило препятствия или загрузило комнату заново.");
            var incoming=world.Copy();incoming.doors[0].open=true;incoming.doors[0].revision++;
            geometry.Reconcile(incoming);
            Require(ReferenceEquals(room,geometry.Get("factory:0")) && !ReferenceEquals(surfaces,room.platforms),"Контроллер потерял общую геометрию при открытии.");
            for(int i=0;i<60;i++) controller.Step(actor,Vector2.right,false,1f/60);
            Require(actor.x>3 && !ShotObstacles.FirstContact(room,new Vector2(0,1),new Vector2(4,1)).HasValue,"Игрок или выстрел всё ещё видит закрытую дверь.");
            Require(geometry.Get("hub").platforms.Length==1,"Дверь попала в другую локацию.");
            incoming.doors.Clear();geometry.Reconcile(incoming);Require(room.platforms.Length==1,"Удалённая дверь осталась в геометрии.");
            incoming.worldId="another-world";geometry.Reconcile(incoming);
            Require(!ReferenceEquals(room,geometry.Get("factory:0")) && loads==4,"Новый мир сохранил геометрию старого.");
            incoming.actors[1].connected=false;geometry.Reconcile(incoming);
            bool unloaded=false;try {geometry.Get("hub");} catch(InvalidOperationException) {unloaded=true;}
            Require(unloaded,"Пустая локация не выгружена.");
            Debug.Log("Общая геометрия дверей: PASS — живой контроллер, выстрелы, другой уровень, без перестроения на позах, смена мира и выгрузка.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
