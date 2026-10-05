using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class FreePropMotionChecks
    {
        public static void Run()
        {
            foreach(int fps in new[]{30,60,144})
            {
                CheckSeparatedLevels(fps);
                CheckPauseAndFreeze(fps);
                CheckOwnershipAndRelease(fps);
            }
            Debug.Log("Свободные объекты: PASS — разные активные уровни, общий контейнер с содержимым, пауза/заморозка, владелец/передача и удержание при 30/60/144 FPS.");
        }

        private static void CheckSeparatedLevels(int fps)
        {
            var world=World();var rooms=Rooms();
            var local=Prop("local-hub","local","hub",-4,4);
            var foreign=Prop("foreign","remote","factory:6",4,6);
            var missingOwner=Prop("unowned",null,"factory:6",6,6);
            var emptyOwner=Prop("empty-owner","","factory:6",-6,6);
            var cargo=new ContainerState {id="cargo",owner="local",level="factory:6",x=0,y=6,generation=7};
            var item=new ItemStack {id="cargo-heal",definition="heal",count=7};
            cargo.items.Add(item);cargo.itemAliases.Add("old-heal",item.id);
            var pending=new PendingGrant {transaction="pending-claim",containerId=cargo.id,recipient="visitor",item=item.ReservedCopy(2)};
            world.pendingGrants.Add(pending);
            world.props.AddRange(new[]{local,foreign,missingOwner,emptyOwner});world.containers.Add(cargo);
            var lookups=new HashSet<string>();int publications=0;
            RoomLayout Room(string level) {lookups.Add(level);return rooms[level];}
            void Publish(PropState prop)
            {
                Require(ReferenceEquals(prop,local) || ReferenceEquals(prop,cargo),"Опубликована копия или чужой объект");
                publications++;
            }
            for(int i=0;i<fps*2;i++)
                Require(FreePropMotion.Step(world,"local",1f/fps,Room,_=>false,Publish)==2,"Не обработаны оба уровня разделившейся группы");
            Require(Mathf.Abs(local.y-local.anchorHeight)<.001f && Mathf.Abs(cargo.y-2)<.001f,
                $"Ящик должен приземлиться на пол своего уровня: fps={fps}, hub={local.y}, factory={cargo.y}");
            Require(lookups.SetEquals(new[]{"hub","factory:6"}) && publications==fps*4,"Не та геометрия или повтор публикации");
            Require(foreign.y==6 && missingOwner.y==6 && emptyOwner.y==6,"Изменён объект чужого или отсутствующего владельца");
            Require(ReferenceEquals(WorldProps.Find(world,cargo.id),cargo) && ReferenceEquals(cargo.items[0],item) &&
                item.id=="cargo-heal" && item.count==7 && cargo.itemAliases["old-heal"]==item.id && cargo.generation==7 &&
                ReferenceEquals(world.pendingGrants[0],pending) && pending.item.count==2 && pending.containerId==cargo.id,
                "Физика скопировала/изменила контейнер, вещи, aliases, generation или ожидающую выдачу");
        }

        private static void CheckPauseAndFreeze(int fps)
        {
            var world=World();var rooms=Rooms();
            var paused=Prop("paused","local","factory:6",0,6);paused.vx=1;paused.vy=-1;
            var running=Prop("running","local","hub",0,4);
            var empty=Prop("empty-level","local","factory:-27",0,5);
            world.props.AddRange(new[]{paused,running,empty});
            int pausedLookups=0,pausedPublications=0;
            RoomLayout Room(string level) {if(level=="factory:6") pausedLookups++;return rooms[level];}
            void Publish(PropState prop) {if(prop==paused) pausedPublications++;Require(prop!=empty,"Опубликован замороженный пустой уровень");}
            for(int i=0;i<fps;i++) FreePropMotion.Step(world,"local",1f/fps,Room,level=>level=="factory:6",Publish);
            Require(paused.Position==new Vector2(0,6) && paused.vx==1 && paused.vy==-1 &&
                pausedLookups==0 && pausedPublications==0,"Пауза изменила объект или запросила геометрию/публикацию");
            Require(running.y<4 && empty.y==5,"Другой уровень остановлен либо пустой уровень продолжает симуляцию");
            FreePropMotion.Step(world,"local",1f/fps,Room,_=>false,Publish);
            Require(paused.y<6 && paused.y>5.9f && pausedLookups==1 && pausedPublications==1,"Возобновление применило время паузы одним скачком");
            var visitor=world.actors.Find(a=>a.id=="visitor");visitor.connected=false;
            var frozen=paused.Position;var velocity=new Vector2(paused.vx,paused.vy);
            for(int i=0;i<fps;i++) FreePropMotion.Step(world,"local",1f/fps,Room,_=>false);
            Require(paused.Position==frozen && new Vector2(paused.vx,paused.vy)==velocity && pausedLookups==1,
                "Отключённое тело ошибочно сохраняет активность уровня");
        }

        private static void CheckOwnershipAndRelease(int fps)
        {
            var world=World();var rooms=Rooms();
            var held=Prop("held","local","factory:6",0,6);held.held=true;
            var fixedProp=Prop("fixed","local","factory:6",2,6);fixedProp.movable=false;
            var transferred=Prop("transfer","departed","factory:6",4,6);
            world.props.AddRange(new[]{held,fixedProp,transferred});
            int lookups=0;
            RoomLayout Room(string level) {lookups++;return rooms[level];}
            Require(FreePropMotion.Step(world,"local",1f/fps,Room,_=>false)==0 && lookups==0,
                "Удерживаемый/неподвижный/чужой объект получил свободный шаг");
            // Ownership handoff is done by the existing owner service; free motion consumes its state.
            world.actors.Find(a=>a.id=="visitor").connected=false;
            world.actors.Add(new ActorState {id="successor",owner="local",player=true,connected=true,level="factory:6"});
            new OwnershipService().Reassign(world,new[]{"local"});
            Require(transferred.owner=="local" && transferred.generation==1,"Отключение не передало объект активному владельцу");
            held.held=false;
            Require(FreePropMotion.Step(world,"local",1f/fps,Room,_=>false)==2 && held.y<6 && transferred.y<6 && fixedProp.y==6,
                "Отпущенный или переданный объект не вернулся в свободную физику");
            var before=transferred.Position;int beforeLookups=lookups;
            FreePropMotion.Step(world,"departed",1f/fps,Room,_=>false);
            Require(transferred.Position==before && lookups==beforeLookups,"Предыдущий владелец повторно применил движение");
            FreePropMotion.Step(world,"local",0,Room,_=>false);
            Require(transferred.Position==before && lookups==beforeLookups,"Нулевой шаг применил физику");
        }

        private static WorldState World()
        {
            var world=new WorldState();
            world.actors.Add(new ActorState {id="home",owner="local",player=true,connected=true,level="hub"});
            world.actors.Add(new ActorState {id="visitor",owner="remote",player=true,connected=true,down=true,level="factory:6"});
            world.actors.Add(new ActorState {id="offline",owner="local",player=true,connected=false,level="factory:-27"});
            return world;
        }
        private static Dictionary<string,RoomLayout> Rooms()=>new()
        {
            ["hub"]=Room("hub",0),["factory:6"]=Room("factory:6",2)
        };
        private static RoomLayout Room(string id,float floor)=>new()
        {
            id=id,left=-10,right=10,bottom=-1,top=10,ladders=Array.Empty<RoomLadder>(),
            platforms=new[]{new RoomSurface {x=-10,y=floor-1,width=20,height=1}}
        };
        private static PropState Prop(string id,string owner,string level,float x,float y)=>new()
        {id=id,owner=owner,level=level,x=x,y=y};
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Свободные объекты: "+reason);}
    }
}
