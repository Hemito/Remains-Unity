using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Editor
{
    public static class LocalProjectileChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Run()
        {
            var root=new GameObject("Проверка немедленного выстрела");
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();
                var observer=new ActorState {id="observer",owner="b",player=true,connected=true,level="hub",x=-5};
                var target=new ActorState {id="target",owner="b",level="hub",x=2};
                world.actors.Add(observer);world.actors.Add(target);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                var geometry=new SimulationGeometry(session);
                ((Dictionary<string,RoomLayout>)typeof(SimulationGeometry).GetField("rooms",Private).GetValue(geometry))
                    .Add("hub",new RoomLayout {platforms=Array.Empty<RoomSurface>()});
                int hits=0,created=0;ProjectileFlight flight=null;
                var controller=new ProjectileController(session,geometry,(_,_,_)=>hits++);
                controller.Created+=value=>{flight=value;created++;};
                var definition=WeaponCatalog.Get("pistol");
                AttackEvent Attack(string id)
                {
                    var attack=new AttackEvent {id=id,attacker="source",sourceOwner="a",sourcePlayer=true,
                        weapon="pistol",level="hub",originY=.7f,endX=20,endY=.7f};
                    ProjectileLaunch.Prepare(attack,definition,definition.projectileSpeed);return attack;
                }
                var attack=Attack("immediate");var confirmed=ProjectileLaunch.Create(attack,definition);
                session.Projectiles.BeginLocal(attack);
                Require(created==1 && flight.path.Origin==attack.Origin && flight.path.Speed==confirmed.flightSpeed,
                    "Полёт не появился сразу или отличается от подтверждаемой траектории");
                controller.Step(.1f);
                Require(hits==0 && flight.path.Finished && flight.awaitingConfirmation,"Попадание применено до подтверждения");
                float position=flight.path.Traveled;
                var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",Private).GetValue(session.Pause);
                paused.Add("hub");
                void Confirm(ProjectileState state)=>typeof(SessionProjectiles).GetMethod("ReceiveLaunch",Private).Invoke(session.Projectiles,new object[]{state});
                Confirm(confirmed);Confirm(confirmed);controller.Step(.1f);
                Require(created==1 && hits==0 && !flight.awaitingConfirmation && flight.path.Traveled==position,
                    "Подтверждение перезапустило полёт или применило урон на паузе");
                paused.Clear();controller.Step(.1f);controller.Step(.1f);
                Require(hits==1,"Раннее попадание потеряно или повторено после подтверждения");
                Require(!session.Projectiles.AcceptEcho(attack.id),"Собственная сетевая вспышка не подавлена");
                var rejected=Attack("rejected");session.Projectiles.BeginLocal(rejected);controller.Step(.1f);
                var cancelled=flight;
                typeof(SessionProjectiles).GetMethod("ReceiveRejection",Private).Invoke(session.Projectiles,
                    new object[]{new ProjectileRejectedMessage {id=rejected.id,reason="Проверочная отмена"}});
                controller.Step(.1f);
                Require(cancelled.cancelled && hits==1 && !session.Projectiles.IsPending(rejected.id),"Отмена сохранила неподтверждённый урон");
                var restored=Attack("recovery");session.Projectiles.BeginLocal(restored);
                var newer=ProjectileLaunch.Create(restored,definition);newer.generation=2;newer.owner="c";newer.traveled=1;
                world.projectiles.RemoveAll(p=>p.launch.id==restored.id);world.projectiles.Add(newer);
                Confirm(ProjectileLaunch.Create(restored,definition));
                Require(flight.state.generation==2 && flight.state.owner=="c" && flight.path.Traveled==1,
                    "Позднее подтверждение затёрло восстановленного владельца");
                bool invalid=false;var malformed=Attack("bad");malformed.speed=float.NaN;
                try {ProjectileLaunch.Create(malformed,definition);} catch(ArgumentException) {invalid=true;}
                Require(invalid,"Не отклонена некорректная скорость владельца");
                Debug.Log("Собственный выстрел: PASS — немедленный полёт, одна траектория, подтверждение, пауза, один урон, отмена и восстановление.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException(reason);}
    }
}
