using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorStateChecks
    {
        public static void Run()
        {
            var door=new DoorState {id="door:entry",definition="wood-door",level="factory:0",surfaceTexture="Hub/concrete",x=2,y=0,width=.4f,height=2.4f};
            var actor=new ActorState {id="player",player=true,connected=true,x=1,y=0};
            var access=new NearbyDoorAccess();
            var baseSurfaces=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}};
            RoomLayout Layout()=>new() {platforms=DoorGeometry.Compose(baseSurfaces,new[]{door},"factory:0")};
            Require(ShotObstacles.FirstContact(Layout(),new Vector2(0,1),new Vector2(4,1)).HasValue,"Закрытая дверь пропустила выстрел.");
            Require(DoorOperations.SetOpen(door,actor,0,true,access,false,out _)==DoorChange.Changed && door.revision==1,"Открытие не применено.");
            Require(!ShotObstacles.FirstContact(Layout(),new Vector2(0,1),new Vector2(4,1)).HasValue,"Открытая дверь осталась препятствием.");
            Require(DoorOperations.SetOpen(door,actor,0,true,access,false,out _) == DoorChange.Rejected && door.revision==1,"Повтор изменил дверь.");
            Require(DoorOperations.SetOpen(door,actor,1,true,access,false,out _) == DoorChange.Unchanged && door.revision==1,"Запрос текущего состояния создал изменение.");
            Require(DoorOperations.SetOpen(door,actor,1,false,access,true,out _) == DoorChange.Rejected && door.open,"Закрытие в занятый проём разрешено.");
            var legacy=Remains.Serialization.StateJson.FromJson<WorldState>("{\"schema\":1,\"worldId\":\"old-world\"}");
            Require(legacy.doors!=null && legacy.doors.Count==0,"Старый мир без поля doors не загружается.");
            var world=new WorldState();world.doors.Add(door);var copy=world.Copy();
            Require(copy.doors.Count==1 && copy.doors[0].open && copy.doors[0].revision==1 && !ReferenceEquals(copy.doors[0],door),"Копия мира потеряла состояние двери.");
            actor.level="hub";
            Require(DoorOperations.SetOpen(door,actor,1,false,access,false,out _) == DoorChange.Rejected,"Разрешён запрос из другой локации.");
            actor.level="factory:0";actor.x=20;
            Require(DoorOperations.SetOpen(door,actor,1,false,access,false,out _) == DoorChange.Rejected,"Разрешён удалённый запрос.");
            actor.x=1;
            Require(DoorOperations.SetOpen(door,actor,1,false,access,false,out _) == DoorChange.Changed && door.revision==2,"Закрытие не применено.");
            Require(DoorOperations.SetOpen(door,actor,1,true,access,false,out _) == DoorChange.Rejected && !door.open,"Запоздалое открытие отменило закрытие.");
            Require(baseSurfaces.Length==1 && DoorGeometry.Compose(baseSurfaces,new[]{door},"hub").Length==1,"Изменена исходная геометрия или затронута другая локация.");
            bool duplicate=false;try {DoorGeometry.Compose(baseSurfaces,new[]{door,door},"factory:0");} catch(InvalidOperationException) {duplicate=true;}
            Require(duplicate,"Дубликаты дверей приняты.");
            Debug.Log("Двери: PASS — состояние, сериализация, повтор/устаревшая версия, занятый проём, дальность и преграда выстрелу.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
        public static void Verify() {ContractChecks.Run();}
    }
}
