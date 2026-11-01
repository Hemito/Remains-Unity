using System;
using System.Diagnostics;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // GrenadeAim, a non-player thrower's aim, through the game's own grenade flight: on a flat floor, under a low ceiling
    // and at a target on a ledge the burst lands within the blast reach of the target and never within the thrower's
    // own; a target inside its own blast or behind a full wall gets no throw; the thrower's error spreads the bursts
    // without dropping one at its feet; and the planning cost of one throw.
    public static class GrenadeAimChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();
            var grenade=WeaponCatalog.Get("grenade");float blast=grenade.range,full=blast/2,own=blast+GrenadeAim.SafetyMargin;   // full: the whole-damage half of the blast
            var thrower=new Vector2(0,.6f);var origin=thrower+Vector2.up*.5f;

            void Lands(string what,RoomLayout room,Vector2 target)
            {
                var plan=GrenadeAim.Plan(room,origin,thrower,target,0,new System.Random(1));
                Require(plan.valid,$"{what}: нет броска");
                Require(Vector2.Distance(plan.burst,target)<full && Vector2.Distance(plan.burst,thrower)>own,
                    $"{what}: взрыв в {Vector2.Distance(plan.burst,target):0.00} м от цели (полный урон до {full}), в {Vector2.Distance(plan.burst,thrower):0.00} м от бросающего");
                // The prediction stops at rest; the real flight to the end of the fuse differs by its settling only.
                var flight=new GrenadeState {id="real",x=origin.x,y=origin.y,vx=plan.velocity.x,vy=plan.velocity.y,remaining=grenade.grenadeFuseSeconds,rotationVersion=1};
                GrenadeRules.Step(flight,room,grenade.grenadeFuseSeconds);
                Require(Vector2.Distance(flight.Position,plan.burst)<.05f,$"{what}: предсказание расходится с полётом на {Vector2.Distance(flight.Position,plan.burst):0.000} м");
            }
            Lands("ровный пол, 8 м",Room(),new Vector2(8,.6f));
            Lands("ровный пол, 12 м влево",Room(),new Vector2(-12,.6f));
            Lands("низкий потолок 2,4 м",Room(ceiling:2.4f),new Vector2(9,.6f));
            Lands("цель на уступе 1 м",Room(ledge:true),new Vector2(7.5f,1.6f));

            Require(!GrenadeAim.Plan(Room(),origin,thrower,new Vector2(5.5f,.6f),0,new System.Random(2)).valid,"цель в зоне своего взрыва — броска нет");
            Require(!GrenadeAim.Plan(Room(wall:true),origin,thrower,new Vector2(10,.6f),0,new System.Random(3)).valid,"цель за стеной до потолка — броска нет");

            // The error: bursts spread, none at the thrower's feet, most still within the blast reach.
            GrenadeAim.Plan(Room(),origin,thrower,new Vector2(9,.6f),6,new System.Random(9));   // warm-up (first-call JIT)
            int near=0,count=40;float worst=float.MaxValue;var watch=Stopwatch.StartNew();
            for(int i=0;i<count;i++)
            {
                var plan=GrenadeAim.Plan(Room(),origin,thrower,new Vector2(9,.6f),6,new System.Random(10+i));
                Require(plan.valid,"с ошибкой броска бросок есть");
                worst=Mathf.Min(worst,Vector2.Distance(plan.burst,thrower));
                if(Vector2.Distance(plan.burst,new Vector2(9,.6f))<full) near++;
            }
            watch.Stop();
            Require(worst>own && near>=count*3/4,$"ошибка 6°: в зоне полного урона {near} из {count}, ближайший к бросающему взрыв {worst:0.00} м");
            double ms=watch.Elapsed.TotalMilliseconds/count;
            Require(ms<3,$"один бросок планируется дольше 3 мс: {ms:0.00} мс");
            Debug.Log($"Прицел гранаты: PASS — пол, низкий потолок, уступ; без броска в свою зону и через стену; с ошибкой 6° в зоне полного урона {near} из {count}, ни одного под ноги; план броска {ms:0.00} мс.");
        }

        private static RoomLayout Room(float ceiling=20,bool ledge=false,bool wall=false)
        {
            float left=-20,right=20;
            var surfaces=new System.Collections.Generic.List<RoomSurface>
            {
                new() {x=left,y=-1,width=right-left,height=1},
                new() {x=left-1,y=-1,width=1,height=ceiling+2},new() {x=right,y=-1,width=1,height=ceiling+2},
                new() {x=left,y=ceiling,width=right-left,height=1}
            };
            if(ledge) surfaces.Add(new RoomSurface {x=6,y=0,width=14,height=1});
            if(wall) surfaces.Add(new RoomSurface {x=6,y=0,width=.5f,height=ceiling});
            var room=new RoomLayout {id="grenade-aim-check",left=left,right=right,bottom=-1,top=ceiling+1,ladders=Array.Empty<RoomLadder>(),platforms=surfaces.ToArray()};
            RoomSurfaces.Prepare(room);
            return room;
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Прицел гранаты: "+message);}
    }
}
