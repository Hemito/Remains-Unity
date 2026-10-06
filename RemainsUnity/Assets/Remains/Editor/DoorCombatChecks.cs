using System;
using System.Reflection;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorCombatChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка ударов через дверь");
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();
                var source=new ActorState {id="source",owner="remote",player=true,connected=true,level="hub"};
                var target=new ActorState {id="target",owner="other",level="hub",x=3};
                world.actors.Add(source);world.actors.Add(target);
                world.doors.Add(new DoorState {id="blocking-door",definition="wood",surfaceTexture="door:wood",
                    level="hub",x=1.5f,y=0,width=.2f,height=3});
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                var geometry=new SimulationGeometry(_=>new RoomLayout {id="door-combat",platforms=Array.Empty<RoomSurface>()});
                geometry.Reconcile(world);var combat=new CombatController(session,geometry);
                var receive=typeof(CombatController).GetMethod("ReceiveAttack",BindingFlags.Instance|BindingFlags.NonPublic);
                int contacts=0;combat.ContactObserved+=(attack,actor)=>{if(actor.id==target.id) contacts++;};
                foreach(string weapon in new[]{"spear","crowbar"})
                {
                    contacts=0;world.doors[0].open=false;world.doors[0].revision++;geometry.Reconcile(world);
                    var attack=new AttackEvent {id=weapon+"-closed",attacker=source.id,sourceOwner="remote",weapon=weapon,
                        level="hub",originY=.7f,endX=4,endY=.7f,radius=.08f,
                        meleeAnchor=new MeleeAttackAnchor(new Vector2(0,.7f),new Vector2(0,.7f))};
                    receive.Invoke(combat,new object[]{attack});
                    Require(contacts==0,"Закрытая дверь пропустила удар "+weapon);
                    world.doors[0].open=true;world.doors[0].revision++;geometry.Reconcile(world);
                    attack.id=weapon+"-open";receive.Invoke(combat,new object[]{attack});
                    Require(contacts==1,"Открытая дверь продолжает блокировать удар "+weapon);
                    world.doors[0].open=false;world.doors[0].revision++;geometry.Reconcile(world);
                    target.x=1;attack.id=weapon+"-before-door";receive.Invoke(combat,new object[]{attack});
                    Require(contacts==2,"Дверь блокирует цель перед собой "+weapon);target.x=3;
                }
                Debug.Log("Удары и двери: PASS — копьё/лом, закрытая/открытая дверь и цель перед препятствием.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
