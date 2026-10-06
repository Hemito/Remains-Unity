using System;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class CargoHotPathChecks
    {
        public static void Run(bool log=true)
        {
            CheckLiveView();CheckStationaryContact();
            foreach(int fps in new[]{30,60,144}) CheckFrameContext(fps);
            PropContactAudioChecks.RunMotion();
            if(log) Debug.Log("Грузы: PASS — живые ссылки, смена списка/мира, один запрос уровня за кадр, сохранность вещей, контакт после снятия опоры, ограниченные managed аллокации прогретой свободной физики.");
        }
        private static void CheckLiveView()
        {
            var world=new WorldState();var first=new PropState {id="first"};
            var cargo=new ContainerState {id="cargo"};world.props.Add(first);world.containers.Add(cargo);
            var view=new WorldPropView(world);
            Require(view.Count==2 && ReferenceEquals(view[1],cargo),"Скопирован контейнер");
            world.props=new() {new PropState {id="replacement"},first};
            Require(view.Count==3 && view[0].id=="replacement" && ReferenceEquals(view[2],cargo),"View сохранил устаревший список");
            world.containers.Remove(cargo);Require(view.Count==2,"Удалённое содержимое осталось во view");
            world.containers.Add(new ContainerState {id=first.id});
            try {WorldProps.Find(world,first.id);throw new Exception("Повтор ID не отклонён");}
            catch(InvalidOperationException) {}
        }
        private static void CheckStationaryContact()
        {
            var room=Room("hub",0);var prop=new PropState {id="idle",level="hub",y=.3f};
            var track=new PropContactObservation();var neighbours=new[]{prop};
            Require(track.Sample(prop,room,neighbours,false,3)==MotionContact.None,"Загрузка озвучена");
            int initial=track.SupportQueries;
            for(int i=0;i<1000;i++) Require(track.Sample(prop,room,neighbours,false,3)==MotionContact.None,"Покой озвучен");
            Require(track.SupportQueries==initial,"Неподвижный поддержанный груз повторно сканирует геометрию");
            room.platforms=Array.Empty<RoomSurface>();track.InvalidateSupport();
            track.Sample(prop,room,neighbours,false,3);
            Require(track.SupportQueries==initial+1,"Снятие опоры не перепроверено");
            room.platforms=new[]{new RoomSurface {x=-10,y=-2,width=20,height=1}};
            prop.y=-.7f;
            Require(track.Sample(prop,room,neighbours,false,3)==MotionContact.Landing,"После снятия опоры пропущено реальное приземление");
            prop.held=true;track.Sample(prop,room,neighbours,false,3);initial=track.SupportQueries;
            for(int i=0;i<100;i++) track.Sample(prop,room,neighbours,false,3);
            Require(track.SupportQueries==initial,"Удерживаемый объект сканирует опоры для отключённого звука");
            prop.held=false;prop.generation++;track.Sample(prop,room,neighbours,false,3);
            Require(track.SupportQueries==initial+1,"После освобождения использован старый контакт");
        }
        private static void CheckFrameContext(int fps)
        {
            var world=new WorldState();var room=Room("factory:6",2);
            world.actors.Add(new ActorState {id="owner",player=true,connected=true,level="factory:6",down=true});
            var item=new ItemStack {id="ammo",definition="p10",count=12};
            var cargo=new ContainerState {id="cargo",owner="local",level="factory:6",y=5,width=.7f,height=.6f,anchorHeight=0};
            cargo.items.Add(item);cargo.itemAliases.Add("old-ammo",item.id);world.containers.Add(cargo);
            for(int i=0;i<48;i++) world.props.Add(new PropState {id="p"+i,owner="local",level="factory:6",x=(i%16-8)*.9f,y=5+i/16f});
            var context=new FreePropMotionContext();int lookups=0,pauseReads=0;
            Func<string,RoomLayout> lookup=level=>{lookups++;return room;};
            Func<string,bool> pause=level=>{pauseReads++;return false;};
            for(int i=0;i<fps*3;i++) Require(FreePropMotion.Step(world,"local",1f/fps,lookup,pause,null,null,context)==49,"Пропущен подключённый down-владелец");
            Require(lookups==fps*3 && pauseReads==fps*3,"Геометрия/пауза читаются повторно для каждого груза");
            Require(ReferenceEquals(cargo.items[0],item) && item.count==12 && cargo.itemAliases["old-ammo"]==item.id,"Изменено содержимое");
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++) FreePropMotion.Step(world,"local",1f/fps,lookup,pause,null,null,context);
            Require(GC.GetAllocatedBytesForCurrentThread()==before,"Прогретая свободная физика создаёт managed объекты");
            var replacement=new WorldState();replacement.actors.Add(world.actors[0]);
            var next=new PropState {id="next",owner="local",level="factory:6",y=5};replacement.props.Add(next);
            Require(FreePropMotion.Step(replacement,"local",1f/fps,lookup,pause,null,null,context)==1 && next.y<5,"Context применил старый мир");
            world.actors[0].connected=false;var position=cargo.Position;
            Require(FreePropMotion.Step(world,"local",1f/fps,lookup,pause,null,null,context)==0 && cargo.Position==position,"Пустой уровень не заморожен");
        }
        private static RoomLayout Room(string level,float floor)=>new() {id=level,left=-10,right=10,bottom=-3,top=20,
            platforms=new[]{new RoomSurface {x=-10,y=floor-1,width=20,height=1}},ladders=Array.Empty<RoomLadder>()};
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Hot path грузов: "+reason);}
    }
}
