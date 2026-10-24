using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    public static class DoorVisualChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка представления двери");
            try
            {
                var visuals=root.AddComponent<DoorVisuals>();visuals.SetOrders(new WorldVisualOrderTable());var world=new WorldState();
                var door=new DoorState {id="visual-door",definition="door1",level="hub",x=2,y=1,width=.8f,height=1.6f,surfaceTexture="door:wood"};
                world.doors.Add(door);visuals.Reconcile(world,"hub");var view=visuals.Find(door.id);
                Require(view!=null && view.Artwork.sprite==view.Definition.Closed && visuals.SoundChanges==0,"Начальная загрузка не молчит или не рисует дверь.");
                foreach(string id in new[]{"door1","door2","door3"})
                {
                    var prefab=GameAssets.Load<DoorView>("Presentation/Doors/"+id);
                    Require(prefab!=null && prefab.Artwork.sortingOrder==ActorVisualLayers.RestingObjectOrder,
                        "Обычная дверь перекрывает персонажа: "+id);
                }
                var definition=view.Definition;var position=view.transform.position;door.open=true;door.revision++;
                visuals.Reconcile(world,"hub");visuals.Reconcile(world.Copy(),"hub");
                Require(view.Artwork.sprite==view.Definition.Opened && view.transform.position==position && visuals.SoundChanges==1,"Открытие смещает дверь или повторяет звук.");
                door.open=false;door.revision++;visuals.Reconcile(world,"hub");
                Require(view.Artwork.sprite==view.Definition.Closed && visuals.SoundChanges==2,"Закрытие не отражено.");
                visuals.Reconcile(world,"factory:0");Require(visuals.Count==0,"Дверь осталась в чужой локации.");
                visuals.Reconcile(world,"hub");Require(visuals.SoundChanges==2,"Возвращение повторило звук.");
                world.worldId="new-world";door.open=true;visuals.Reconcile(world,"hub");
                Require(visuals.SoundChanges==2 && visuals.Find(door.id).Artwork.sprite==definition.Opened,"Загрузка другого мира повторила звук.");
                world.doors.Clear();visuals.Reconcile(world,"hub");Require(visuals.Count==0,"Удалённая дверь видима.");
                Debug.Log("Представление дверей: PASS — открытие/закрытие, повтор снимка, смена уровня/мира, удаление и тихая загрузка.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
