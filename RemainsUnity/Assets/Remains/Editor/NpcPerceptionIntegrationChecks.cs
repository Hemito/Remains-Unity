using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcPerceptionIntegrationChecks
    {
        public static void Run()
        {
            var root=new GameObject("Восприятие NPC с геометрией и передачей памяти");
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();
                var player=new ActorState {id="player",owner="owner-a",player=true,connected=true,x=5};
                var npc=new ActorState {id="npc",owner="owner-a",actorType="raider",anatomy="raider",attackCooldown=100};
                world.actors.Add(player);world.actors.Add(npc);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,"owner-a");
                var ownership=new OwnershipService();ownership.Reassign(world,new[]{"owner-a"});
                Require(npc.target==null,"Координатор сети назначил боевую цель.");
                var floor=new RoomSurface {x=-10,y=-.4f,width=30,height=.4f};
                var room=new RoomLayout {left=-10,right=20,bottom=-.4f,top=5,platforms=new[]{floor},ladders=Array.Empty<RoomLadder>()};
                var geometry=new SimulationGeometry(_=>room);
                geometry.Reconcile(world);
                var controller=new NpcController(session,geometry);
                ((Dictionary<string,MovementController>)typeof(NpcController).GetField("movement",BindingFlags.Instance|BindingFlags.NonPublic)
                    .GetValue(controller)).Add(npc.level,new MovementController(room));
                for(int i=0;i<40;i++) controller.Step(1f/60);
                Require(npc.target==player.id && npc.perception.hasKnownPosition,"Игровой контроллер не подтвердил видимого игрока.");
                var known=npc.perception.KnownPosition;
                room.platforms=new[]{floor,new RoomSurface {x=2,y=0,width=.5f,height=4},new RoomSurface {x=-2,y=0,width=.5f,height=4}};
                // A cached target ID may survive until the next perception sample.
                // Even between samples, movement must use the remembered point, not a hidden player's new position.
                player.x=-6;controller.Step(0);
                Require(npc.target==player.id && npc.facing==1 && npc.perception.KnownPosition==known,
                    "Между опросами зрения NPC повернулся к новым скрытым координатам вместо известного места.");
                player.x=6;
                for(int i=0;i<12;i++) controller.Step(1f/60);
                Require(npc.target==null && npc.perception.KnownPosition==known,"Игровой NPC продолжает следить через стену.");
                var paused=(HashSet<string>)typeof(SessionPause).GetField("pausedLevels",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session.Pause);
                paused.Add(npc.level);float remaining=npc.perception.remainingSeconds;var position=npc.Position;
                controller.Step(5);
                Require(npc.perception.remainingSeconds==remaining && npc.Position==position,"Пауза уровня не остановила восприятие или поиск.");
                paused.Clear();
                var restored=npc.Copy();Require(restored.perception.KnownPosition==known,"ActorState не сохраняет память поиска.");
                var stale=npc.Copy();stale.motionSequence=4;stale.perception.knownX=100;stale.target=player.id;
                npc.motionSequence=5;ActorMotionReplication.PreserveNewerMotion(npc,stale);
                Require(stale.target==null && stale.perception.KnownPosition==known,"Поздний снимок откатил потерю контакта.");
                stale.perception.knownX=200;Require(npc.perception.KnownPosition==known,"Копии актёров делят изменяемую память.");
                var b=new ActorState {id="b",owner="owner-b",player=true,connected=true,x=12};world.actors.Add(b);
                npc.target=b.id;ownership.Reassign(world,new[]{"owner-a","owner-b"});
                Require(npc.owner==b.owner && npc.target==b.id && npc.perception.KnownPosition==known,
                    "Владелец выбран по близости вместо подтверждённой цели или потерял память.");
                room.platforms[1].oneWay=true;
                Require(RoomVisibility.Clear(room,new Vector2(0,1),new Vector2(6,1)),"Односторонняя платформа стала сплошной стеной зрения.");
                room.platforms[1].oneWay=false;
                if(npc.weaponInventoryVersion==0) NpcWeaponTestItems.Equip(npc);
                npc.attackCooldown=0;NpcAttackRules.Begin(npc,player);
                Require(NpcWeaponTestItems.Finish(npc,player,room)==null,"NPC выпустил прицельный выстрел через закрывшую цель стену.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("NpcPerceptionIntegrationChecks PASS: контроллер, геометрия, сохранение, защита от отката и владелец по цели.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
