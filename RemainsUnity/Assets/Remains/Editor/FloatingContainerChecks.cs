using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Remains.Editor
{
    public static class FloatingContainerChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка плавающего окна",typeof(RectTransform),typeof(Canvas));
            try
            {
                var session=root.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var actor=new ActorState {id="player:window",owner="window",player=true,connected=true,level="factory:0"};
                var world=new WorldState();world.actors.Add(actor);var box=new ContainerState {x=0};world.containers.Add(box);
                for(int i=0;i<16;i++) {var item=SharedInventory.Create("caps",i+1);item.extensions=new Dictionary<string,JToken>{{"quality",i}};InventoryStacks.Add(box,item);}
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                var view=ContainerWindowView.Create(root.transform);int changes=0;
                string submitted=null;var intent=view.Intents[0];intent.Initialize(view.Slots[0],item=>submitted=item);
                intent.Bind("pressed-item");intent.OnPointerDown(new PointerEventData(null) {button=PointerEventData.InputButton.Left});
                intent.Bind("replacement-item");intent.OnPointerClick(new PointerEventData(null) {button=PointerEventData.InputButton.Left});
                Require(submitted=="pressed-item","Изменение сетки подменило предмет между нажатием и кликом");
                var panel=new FloatingContainerPanel(view,session,new InventoryArtwork(),null,()=>changes++);
                Require(panel.Open(box.id) && panel.RowCount==6 && panel.FirstItem==0 && view.Icons[8].enabled,"Нет первых девяти иконок");
                view.OnScroll(new PointerEventData(null) {scrollDelta=Vector2.down});
                Require(panel.FirstItem==3,"Колесо не прокрутило один ряд");
                view.Scrollbar.value=1;
                Require(panel.FirstItem==9 && view.Icons[6].enabled && !view.Icons[7].enabled,"Конец содержимого недоступен либо пустые клетки не очищены");
                Require(box.items.Count==16 && actor.inventory.Count==0 && changes==1,"Открытие/прокрутка изменили игровые вещи");
                panel.Close();Require(!view.gameObject.activeSelf && !panel.IsOpen,"Окно не закрыто");
                Require(panel.Open(box.id),"Повторное открытие не удалось");actor.x=3;panel.Step(false);
                Require(!panel.IsOpen,"Отход за дальность доступа не закрыл окно");actor.x=0;
                Require(panel.Open(box.id),"Открытие после возврата не удалось");world.containers.Clear();panel.Step(false);
                Require(!panel.IsOpen,"Потеря источника не закрыла окно");
                Debug.Log("Плавающий контейнер: PASS — сетка без текстовых заглушек, >9 предметов, прокрутка, независимость вещей, дальность и потеря источника.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Плавающий контейнер: "+reason);}
    }
}
