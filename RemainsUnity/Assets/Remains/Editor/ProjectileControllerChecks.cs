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
                var geometry=new SimulationGeometry(session);
                var rooms=(Dictionary<string,RoomLayout>)typeof(SimulationGeometry).GetField("rooms",Private).GetValue(geometry);
                rooms.Add("hub",new RoomLayout {platforms=Array.Empty<RoomSurface>()});
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
                Debug.Log("Контроллер снаряда: PASS — отложенный контакт, уход/вход цели, пауза, восстановление владельца, один контакт, след и вспышка на физическом пути.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
