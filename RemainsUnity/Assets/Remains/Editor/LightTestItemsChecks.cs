using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LightTestItemsChecks
    {
        public static void RunBatch(){Run();EditorApplication.Exit(0);}
        public static void Run()
        {
            WeaponCatalog.Load();ActorTypes.Load();Anatomy.Load();
            var ball=WeaponCatalog.Get(LightTestItems.Ball);var stick=WeaponCatalog.Get(LightTestItems.Stick);
            Require(ball.IsGrenade && ball.IsThrowableLight && !ball.explosive && ball.damage==1 && ball.throwableLight.contactProjectile,"Некорректный профиль попрыгунчика.");
            Require(stick.IsGrenade && stick.IsThrowableLight && !stick.explosive && stick.damage==0 && !stick.throwableLight.contactProjectile,"Некорректный профиль ХИС.");
            foreach(var definition in new[]{ball,stick})
            {
                WeaponCatalogLoader.Validate(definition,"проверка тестового света");
                Require(definition.grenadeFuseSeconds==120,"Время горения тестового предмета отличается от 120 с.");
                Require(GrenadeArtwork.LoadPrefab(definition.id)!=null,"Нет префаба полёта: "+definition.id);
            }
            var menu=DebugItems.Build();
            foreach(string id in new[]{LightTestItems.Ball,LightTestItems.Stick,LightTestItems.Flashlight})
            {
                Require(menu.Any(x=>x.id==id),"Нет тестового предмета в меню выдачи: "+id);
                Require(ItemTextCatalog.Load().Contains(id),"Нет русского текста: "+id);
                Require(new InventoryArtwork().TryGet(id,out var sprite) && sprite!=null,"Нет иконки: "+id);
            }
            Require(new StandardItemActions().Action(SharedInventory.Create(LightTestItems.Flashlight,1))==StandardItemActions.Hold,"Фонарик должен быть обычным удерживаемым предметом.");
            var room=new RoomLayout{id="test-light-bounce",left=-10,right=10,bottom=-2,top=10,
                platforms=new[]{new RoomSurface{x=-10,y=-1,width=20,height=1}}};
            var state=new GrenadeState{id="light-test",weapon=ball.id,level=room.id,y=.3f,vy=-3,remaining=120};
            bool bounced=false;
            for(int i=0;i<60;i++){GrenadeRules.Step(state,room,1f/120);if(state.vy>0){bounced=true;break;}}
            Require(bounced && state.remaining>119,"Попрыгунчик не отскочил от пола.");
            var contactState=new GrenadeState{id="light-contact",weapon=ball.id,level=room.id,y=3,vx=10,remaining=120};
            int contacts=0;
            GrenadeRules.Step(contactState,room,.1f,(from,to)=>{contacts++;return true;});
            Require(contacts==1 && contactState.remaining==0 && contactState.simulationRemainder==0,
                "Попадание шарика не остановило симуляцию на первом контакте.");
            GrenadeRules.Step(contactState,room,.1f,(from,to)=>{contacts++;return true;});
            Require(contacts==1,"Исчезнувший шарик повторил попадание.");
            var output=new List<WorldLightEmission>();var lights=new BouncingLightSources();
            var flight=new GrenadeFlight{state=state};
            lights.Append(new[]{flight},room.id,output);
            Require(output.Count==1 && output[0].Samples[0].x==state.x && output[0].Samples[0].y==state.y,"Свет отстаёт от физической позиции шарика.");
            var world=new WorldState();world.grenades.Add(state);state.remaining=0;
            Require(LightTestItems.Expire(world,state) && world.grenades.Count==0 && world.explosions.Count==0,"Истёкший светящийся предмет создал взрыв или не удалён.");
            Require(!LightTestItems.Expire(world,state),"Повторное удаление светящегося предмета не идемпотентно.");
            lights.Clear();
            Debug.Log("Тестовые источники света: PASS — выдача, иконки, бросок/отскок, привязка света, 120 с и удаление без взрыва.");
        }
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    }
}
