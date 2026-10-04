using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Exercises the gameplay controller and its real renderer with controlled simulation time.
    // Transport delivery is covered separately by the multi-process probes.
    public static class ProjectileControllerChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Run()
        {
            CheckSpeculativeContactHandoff();
            var root=new GameObject("Проверка полёта");
            var visual=new GameObject("Проверка следа");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var player=new ActorState {id="observer",owner="b",player=true,connected=true,level="hub",x=-5};
                var target=new ActorState {id="target",owner="b",player=true,connected=true,level="hub",x=8};
                world.actors.Add(player);world.actors.Add(target);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,player.id);
                var geometry=new SimulationGeometry(_=>new RoomLayout {platforms=Array.Empty<RoomSurface>()});
                geometry.Reconcile(world);
                int hits=0;string decidingOwner=null;float contactSpeed=0;
                var controller=new ProjectileController(session,geometry,(attack,contact,owner)=>{hits++;decidingOwner=owner;contactSpeed=attack.speed;});
                ProjectileFlight flight=null;int created=0;
                controller.Created+=value=>{flight=value;created++;};
                var state=new ProjectileState {launch=new AttackEvent {id="timed",attacker="source",sourceOwner="a",sourcePlayer=true,
                    weapon="pistol",level="hub",speed=133.333f,originY=.7f,endX=20,endY=.7f},owner="a",generation=1,flightSpeed=90};
                world.projectiles.Add(state);
                void Reconcile()=>typeof(ProjectileController).GetMethod("Reconcile",Private).Invoke(controller,null);
                Reconcile();Reconcile();Require(created==1,"Повтор запуска создаёт второе представление.");
                controller.Step(.02f);
                Require(hits==0 && Mathf.Abs(flight.path.Traveled-1.8f)<.0001f,"Будущая цель поражена до прихода снаряда.");
                ShotArtwork.Prepare();ShotArtwork.TryGet("pistol",out var artwork);
                player.level="factory:0";
                var manager=new GameObject("Представления полётов").AddComponent<ProjectileVisuals>();manager.transform.SetParent(root.transform);
                manager.Initialize(session,controller);
                void SyncViews()=>typeof(ProjectileVisuals).GetMethod("Reconcile",Private).Invoke(manager,null);
                ProjectileVisual[] ActiveViews()=>manager.GetComponentsInChildren<ProjectileVisual>();
                Require(ActiveViews().Length==0,"Снаряд другого уровня отображается");
                player.level="hub";SyncViews();SyncViews();
                Require(ActiveViews().Length==1 && created==1,"Вход в уровень не восстановил единственное представление существующего полёта");
                player.level="factory:0";SyncViews();Require(ActiveViews().Length==0,"После ухода остался видимый снаряд");
                player.level="hub";SyncViews();Require(ActiveViews().Length==1 && hits==0 && created==1,"Повторный вход перезапустил полёт или контакт");
                var restoredView=ActiveViews()[0];
                var pausedView=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",Private).GetValue(session.Pause);
                pausedView.Add("hub");typeof(ProjectileVisual).GetMethod("LateUpdate",Private).Invoke(restoredView,null);
                Require(restoredView.GetComponent<SpriteRenderer>().enabled &&
                    Mathf.Abs(restoredView.GetComponent<SpriteRenderer>().bounds.max.x-flight.path.Position.x)<.0001f,"Восстановленный след не рисуется на паузе из текущей позиции");
                pausedView.Clear();
                var view=visual.AddComponent<ProjectileVisual>();view.Initialize(flight,artwork,session);
                void Draw()=>typeof(ProjectileVisual).GetMethod("LateUpdate",Private).Invoke(view,null);
                Draw();var renderer=visual.GetComponent<SpriteRenderer>();
                Require(renderer.enabled && Mathf.Abs(renderer.bounds.max.x-flight.path.Position.x)<.0001f,"Изображение опережает физическую траекторию.");
                var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",Private).GetValue(session.Pause);
                paused.Add("hub");controller.Step(1);
                Require(hits==0 && Mathf.Abs(flight.path.Traveled-1.8f)<.0001f,"Пауза продолжила активный полёт.");
                paused.Clear();
                // Current target geometry, not launch-time predicted contact.
                target.y=3;controller.Step(.07f);
                Require(hits==0 && !flight.path.Finished,"Снаряд поразил ушедшую цель.");
                // Recover an authoritative progress point under a different owner.
                state.owner="b";state.generation++;state.traveled=9;Reconcile();
                Require(flight.path.Traveled==9 && flight.state.launch.sourceOwner=="a","Передача потеряла прогресс или изменила автора.");
                target.x=12;target.y=0;controller.Step(.05f);
                Require(hits==1 && decidingOwner=="b" && flight.contact && flight.path.Finished,"Новый владелец не завершил полёт по вошедшей цели.");
                Require(contactSpeed==flight.path.Speed && contactSpeed==90 && state.launch.speed==133.333f,"Скорость контакта взята из старой оценки, либо переписан исходный запуск.");
                Draw();Require(renderer.sprite==artwork.impact[0] && (Vector2)visual.transform.position==flight.path.Position,"Вспышка не находится в точке фактического контакта.");
                controller.Step(.1f);Require(hits==1,"Завершённый полёт повторил урон.");
                Require(state.launch.originX==0 && state.launch.endX==20,"Покадровый отрезок изменил исходный запуск.");
                player.level="factory:0";SyncViews();player.level="hub";SyncViews();
                Require(ActiveViews().Length==0,"Вход в уровень воспроизвёл законченный выстрел");
                Debug.Log("Контроллер снаряда: PASS — отложенный контакт, уход/вход цели, пауза, восстановление владельца, один контакт, след и вспышка на физическом пути.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckSpeculativeContactHandoff()
        {
            var root=new GameObject("Проверка опережающего контакта");
            try
            {
                ActorTypes.Load();
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var observer=new ActorState {id="observer",owner="b",player=true,connected=true,level="hub",x=-5};
                var target=new ActorState {id="npc",owner="b",level="hub",x=8,health=100};
                world.actors.Add(observer);world.actors.Add(target);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                var geometry=new SimulationGeometry(_=>new RoomLayout {platforms=Array.Empty<RoomSurface>()});
                geometry.Reconcile(world);
                var damage=new DamageReceiver(new PrototypeDamageRule());int applied=0,contacts=0,observed=0;
                // The observer sees the contact before becoming the decision owner.
                var controller=new ProjectileController(session,geometry,(attack,contact,owner)=>
                {contacts++;if(owner=="b" && damage.Apply(target,attack,contact.contact)) applied++;});
                controller.ContactObserved+=contact=>{observed++;Require(contact.target=="npc" && contact.targetType=="pony" && contact.position.x>7 && contact.position.x<9,"Неверные данные контакта для представления");};
                var state=new ProjectileState {launch=new AttackEvent {id="handoff-contact",attacker="source",sourceOwner="a",
                    sourcePlayer=true,weapon="pistol",level="hub",originY=.7f,endX=20,endY=.7f},owner="a",generation=1,flightSpeed=90};
                world.projectiles.Add(state);
                void Reconcile()=>typeof(ProjectileController).GetMethod("Reconcile",Private).Invoke(controller,null);
                Reconcile();controller.Step(.1f);
                Require(contacts==1 && applied==0 && target.health==100,"Наблюдатель присвоил себе решение о попадании");
                state.owner="b";state.generation=2;state.traveled=2;Reconcile();controller.Step(.1f);
                Require(applied==1 && target.health==89,"Раннее визуальное попадание подавило удар нового владельца");
                state.generation=3;Reconcile();controller.Step(.1f);
                Require(observed==1,"Передача владельца повторила событие представления контакта");
                Require(applied==1 && target.health==89 && target.resolvedAttacks.Count==1,
                    "Повтор передачи применил урон второй раз");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
