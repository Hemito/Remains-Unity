using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcDoorChecks
    {
        public static void Run()
        {
            Anatomy.Load();
            ActorTypes.Load();
            var npc=new ActorState {id="npc",actorType="raider",anatomy="raider",owner="b",x=3.5f,y=0};
            var a=new ActorState {id="a",owner="a",player=true,connected=true};
            var b=new ActorState {id="b",owner="b",player=true,connected=true};
            var door=new DoorState {id="door",definition="door1",level=npc.level,x=4.5f,y=0,width=.8f,height=3,surfaceTexture="floor"};
            var world=new WorldState();world.actors.AddRange(new[]{npc,a,b});world.doors.Add(door);
            var room=new RoomLayout {platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),world.doors,npc.level)};
            DoorState Select(float x=8)=>NpcDoorIntent.Select(npc,new Vector2(x,0),world.doors,room);
            Require(Select()==door && Select(1)==null && Select(4)==null,"Выбрана дверь вне направления или за целью.");
            room.platforms=new[]{new RoomSurface {x=4.1f,y=0,width=.1f,height=3,shotsPassThrough=true},room.platforms[0]};
            Require(Select()==null,"Дверь открывается через более близкую стену, пропускающую пули.");
            room.platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),world.doors,npc.level);
            door.level="hub";Require(Select()==null,"Выбрана дверь другого уровня.");door.level=npc.level;
            npc.attackBusy=true;Require(Select()==null,"Незавершённая атака прервана взаимодействием.");npc.attackBusy=false;
            npc.down=true;Require(Select()==null,"Дверь выбрана без сознания.");npc.down=false;
            npc.hold=new ActorHoldState {holder=b.id};Require(Select()==null,"Дверь выбрана удерживаемым телом.");npc.hold=null;
            npc.actorType="training-dummy";Require(Select()==null,"Тип без разрешения открывает двери.");npc.actorType="raider";
            var access=new NearbyDoorAccess();
            Require(access.Rejection(npc,door)==null,"Доступ боевого NPC не разрешён.");
            Require(DoorRequestRules.ResolveActor(world,a,npc.id,out _) == null && DoorRequestRules.ResolveActor(world,b,npc.id,out _) == npc,"Запрос принят от чужого владельца или отвергнут от текущего.");
            Require(DoorRequestRules.ResolveActor(world,b,a.id,out _)==null && DoorRequestRules.ResolveActor(world,b,null,out _)==null,"Чужой игрок или пустой ID подменён владельцем соединения.");
            b.connected=false;Require(DoorRequestRules.ResolveActor(world,b,npc.id,out _)==null,"Отключённое соединение управляет NPC.");b.connected=true;
            DoorChange Apply(long revision,int generation,bool pause=false)=>DoorRequestRules.Apply(world,npc,world.worldId,door.id,generation,npc.lifecycleSequence,revision,true,pause,access,out _);
            Require(Apply(0,npc.generation+1)==DoorChange.Rejected && Apply(0,npc.generation,true)==DoorChange.Rejected && door.revision==0,"Устаревшее поколение или пауза изменили дверь.");
            npc.hold=new ActorHoldState {holder=b.id};Require(Apply(0,npc.generation)==DoorChange.Rejected,"Удерживаемый NPC открыл дверь.");npc.hold=null;
            Require(Apply(0,npc.generation)==DoorChange.Changed && Apply(0,npc.generation)==DoorChange.Rejected && door.revision==1,"Повтор запроса повторно изменил дверь.");
            room.platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),world.doors,npc.level);Require(Select()==null,"Открытая дверь вновь выбрана.");
            var copy=StateJson.Copy(world);Require(copy.doors[0].open && copy.doors[0].revision==1,"Сохранение потеряло открытие NPC.");
            npc.owner="a";npc.generation++;Require(DoorRequestRules.ResolveActor(world,b,npc.id,out _)==null,"Старый владелец действует после передачи симуляции.");
            var actual=RoomCatalogAsset.Load().Resolve(NewGameWorld.FactoryDefinitionId);
            var actualDoor=RoomDoorContent.Read(actual,"factory:0").doors.Single(d=>d.id.EndsWith(":aaChLmLMEdgLxJEr",StringComparison.Ordinal));
            var actualRoom=actual.CreateLayout();actualRoom.platforms=DoorGeometry.Compose(actualRoom.platforms,new[]{actualDoor},"factory:0");
            var actualNpc=new ActorState {id="actual-npc",actorType="raider",anatomy="raider",owner="a",x=actualDoor.x-.5f,y=actualDoor.y};
            var nearby=actualRoom.platforms.Where(s=>s.x<actualDoor.x+1 && s.x+s.width>actualDoor.x-1 && s.y<actualDoor.y+1.5f && s.y+s.height>actualDoor.y)
                .Select(s=>$"({s.x},{s.y},{s.width},{s.height}; oneWay={s.oneWay}; texture={s.texture})");
            Require(NpcDoorIntent.Select(actualNpc,new Vector2(actualDoor.x+8,actualDoor.y),new[]{actualDoor},actualRoom)==actualDoor,
                "Исходная дверь не выбрана из фактического упора тела; близкие поверхности: "+string.Join("|",nearby));
            Debug.Log("Двери NPC: PASS — направление/ближайшая преграда, разрешение типа, атака/захват, владельцы, поколение, пауза, повтор и сохранение.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Двери NPC: "+reason);}
    }
}
