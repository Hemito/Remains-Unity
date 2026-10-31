using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    // The body strike shared by players and sapient raiders (PonyStrikeRules, PonyStrikes) against AllData punch and
    // WKick: numbers, the segments ahead and behind, standing only, one attack of all segments at the contact moment,
    // busy for the strike, a raider punching through the same executor; the robobrain and zombies keep their touch.
    // Also the natural touch spread at its upper bound (review 2026-10-31, finding 3).
    public static class PonyStrikeChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var punch=WeaponCatalog.Get(PonyStrikeRules.Punch);var kick=WeaponCatalog.Get(PonyStrikeRules.Kick);
            Require(WeaponCatalog.IsStrike(punch.id) && WeaponCatalog.IsStrike(kick.id) && !WeaponCatalog.IsMelee(punch.id) &&
                Mathf.Approximately(punch.damage,25) && Mathf.Approximately(kick.damage,50) && punch.damageType==DamageTypes.Physical &&
                Mathf.Approximately(punch.cooldown,13f/30) && Mathf.Approximately(punch.strike.contact,8f/13) &&
                !punch.strike.behind && kick.strike.behind && punch.strike.heights.Length==5 && kick.strike.heights.Length==2 &&
                Mathf.Approximately(punch.strike.far,1.2f) && Mathf.Approximately(kick.strike.far,1.4f) && Mathf.Approximately(punch.strike.near,.4f),
                "удар и пинок по AllData punch (1.04: damage 25) и WKick");

            var room=new RoomLayout {id="strike-check",left=-20,right=20,bottom=-1,top=20,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-20,y=-1,width=40,height=1}}};
            RoomSurfaces.Prepare(room);
            var player=new ActorState {id="p",player=true,connected=true,owner="o",level="l",x=0,y=0,health=100,actorType="pony",anatomy="pony",facing=1};
            var world=new WorldState();world.actors.Add(player);

            var segments=new List<(Vector2 start,Vector2 end)>();
            PonyStrikeRules.Segments(player,kick,segments);
            Require(segments.Count==2 && segments.All(s=>s.start.x<player.x && s.end.x<s.start.x && Mathf.Approximately(s.start.y,s.end.y)),"пинок — отрезки позади, от тела наружу");

            var strikes=new PonyStrikes();var fired=new List<AttackEvent>();
            player.crouched=true;
            Require(!strikes.Begin(player,false,room),"пригнувшись не бьёт");
            player.crouched=false;
            Require(strikes.Begin(player,false,room) && strikes.Busy(player) && !strikes.Begin(player,true,room),"удар начинается стоя, второй — только после первого");
            float dt=1f/30,time=0;
            while(strikes.Busy(player))
            {
                strikes.Step(world,"o",dt,_=>false,fired.Add);time+=dt;
                if(time<punch.strike.contact*punch.cooldown-.001f) Require(fired.Count==0,"касание раньше момента WKick");
                Require(time<1,"удар не заканчивается");
            }
            Require(fired.Count==5 && fired.All(a=>a.id==fired[0].id && a.weapon==punch.id && a.attacker==player.id && a.originX>player.x && a.endX>a.originX) &&
                Mathf.Abs(time-punch.cooldown)<=dt+.001f,$"одно событие из пяти отрезков впереди, занят на время удара ({fired.Count}, {time:0.###} с)");

            // A strike of a knocked-down actor is dropped.
            Require(strikes.Begin(player,false,room),"новый удар");
            player.down=true;fired.Clear();strikes.Step(world,"o",punch.cooldown,_=>false,fired.Add);
            Require(fired.Count==0 && !strikes.Busy(player),"удар сбитого с ног отменяется");
            player.down=false;

            // A raider's body hit is the same strike; the robobrain and zombies on the raiders' brain are not sapient ponies.
            var raider=new ActorState {id="r",owner="o",level="l",x=0,y=0,health=50,actorType="raider",anatomy="raider",facing=1};
            Require(PonyStrikeRules.Allowed(raider) && PonyStrikeRules.Allowed(player) &&
                !PonyStrikeRules.Allowed(new ActorState {id="b",actorType="robobrain-robolaser"}) && !PonyStrikeRules.Allowed(new ActorState {id="z",actorType="zombie0"}),
                "общий удар — у игрока и рейдера, не у робомозга и зомби");
            world.actors.Add(raider);player.x=.8f;
            var raiders=new NpcRaiderController(MovementSettings.Load(),new System.Random(1));
            var raiderType=ActorTypes.Get("raider");
            Require(raiders.Strike(raider,raiderType,player,room) && raiders.Striking(raider),"рейдер бьёт цель впереди");
            fired.Clear();
            for(int i=0;i<30 && raiders.Striking(raider);i++) {raiders.StepStrikes(world,"o",dt,_=>false,fired.Add);raiders.EndFrame(dt);}
            Require(fired.Count==5 && fired.All(a=>a.weapon==punch.id && a.attacker==raider.id),"удар рейдера идёт тем же исполнителем");
            player.x=-.8f;
            Require(!raiders.Strike(raider,raiderType,player,room),"рейдер выбирает удар не чаще окна жертвы (attKorp)");
            for(int i=0;i<30;i++) raiders.EndFrame(dt);
            Require(raiders.Strike(raider,raiderType,player,room),"цель позади — пинок");
            player.x=3;
            Require(!raiders.Strike(new ActorState {id="r2",owner="o",level="l",x=0,y=0,health=50,actorType="raider",anatomy="raider",facing=1},raiderType,player,room),"вне досягаемости не бьёт");

            float top=NpcNaturalAttack.Variation(1,new Edge(1-1e-9));float bottom=NpcNaturalAttack.Variation(.5f,new Edge(0));
            Require(top<=NpcNaturalAttack.MaxVariation && top>NpcNaturalAttack.MaxVariation-.001f && bottom>=NpcNaturalAttack.MinVariation,$"разброс касания в пределах: {top:R}, {bottom:R}");
            Debug.Log("Удар телом: PASS — числа punch/WKick, отрезки впереди и позади, только стоя, одно событие в момент контакта, удар рейдера тем же исполнителем, робомозг и зомби без него; разброс касания на границе.");
        }

        private sealed class Edge : System.Random
        {
            private readonly double value;
            public Edge(double value)=>this.value=value;
            public override double NextDouble()=>value;
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Удар телом: "+message);}
    }
}
