using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class MovableContainerChecks
    {
        public static void Run()
        {
            var world=new WorldState();var box=new ContainerState {id="moving-container",owner="holder",generation=3,held=true};
            var item=SharedInventory.Create("heal",7);box.items.Add(item);world.containers.Add(box);
            Require(ReferenceEquals(WorldProps.Find(world,box.id),box),"Физика должна получать сам контейнер, не его копию");
            Require(PropReplication.Accept(box,3,1,1,3,0,0),"Контейнер принимает пакет позы");
            var inventory=InventorySource.Resolve(world,box.id);
            Require(inventory.Position==box.Position && ReferenceEquals(inventory.items,box.items),"Подбор и физика используют одно состояние");
            var restored=world.Copy();var loaded=restored.containers[0];
            Require(loaded.id==box.id && loaded.Position==box.Position && loaded.mass==25 && loaded.items[0].id==item.id && loaded.items[0].count==7,
                "Сохранение контейнера сохраняет положение, массу и те же ID предметов");
            new OwnershipService().BeginSession(restored);
            Require(!loaded.held && loaded.owner==null && loaded.items[0].count==7,"Перезапуск отпускает контейнер без потери содержимого");
            var room=new RoomLayout {left=-10,right=10,bottom=0,top=10,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                new RoomSurface{x=-10,y=1,width=20,height=1},new RoomSurface{x=2,y=2,width=.2f,height=5}}};
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                loaded.Position=new Vector2(0,4);loaded.vx=loaded.vy=0;
                for(int i=0;i<300;i++) PropMotion.Step(loaded,room,dt,null);
                Require(Mathf.Abs(loaded.y-2)<.001f,"Контейнер опирается на пол комнаты, не на фиксированный мировой ноль");
                for(int i=0;i<300;i++) PropMotion.Step(loaded,room,dt,new Vector2(6,3));
                Require(loaded.x<=1.601f,"Переносимый контейнер не проходит стену");
                Require(loaded.items[0].count==7 && loaded.items[0].id==item.id,"Перенос не копирует и не меняет вещи");
            }
            Debug.Log("Перенос контейнера: PASS — единое состояние, предметы, пакет позы, сохранение, перезапуск, пол и стены.");
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Перенос контейнера: "+message);}
    }
}
