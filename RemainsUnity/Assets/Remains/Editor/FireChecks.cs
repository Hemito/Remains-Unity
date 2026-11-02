using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // FireRules: the molotov and the incendiary grenade from the original's data; a bottle breaks by its speed into a
    // surface (never below 3 m/s, surely from 6); a bottle's droplets splash along its flight and stop at a wall, a
    // grenade's fly out evenly; the same patches on every run; a torch burns all its droplets' time; the host's fire
    // from a grenade and its expiry; an actor touches a patch only when standing in it.
    public static class FireChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();ActorTypesLoad();
            var molotov=WeaponCatalog.Get("item:molotov");var incendiary=WeaponCatalog.Get("item:fgren");
            Require(molotov.IsFire && incendiary.IsFire && molotov.fire.shatter && !molotov.fire.radial && incendiary.fire.radial && !incendiary.fire.shatter,
                "коктейль — бутылка с инерцией, зажигательная — граната во все стороны");
            Require(molotov.fire.pulses==10 && Mathf.Approximately(molotov.fire.pulseDamage,20) && Mathf.Approximately(molotov.fire.igniteDamage,10) &&
                Mathf.Approximately(incendiary.fire.pulseDamage,30) && Mathf.Approximately(incendiary.fire.igniteDamage,15) && Mathf.Approximately(molotov.fire.igniteSeconds,7),
                "числа оригинала: 10 вспышек, 20/30 урона, поджог 10/15 в секунду 7 с");

            int some=0;
            for(int i=0;i<40;i++)
            {
                Require(!FireRules.Shatters(molotov.fire,2.9f,"b",i) && FireRules.Shatters(molotov.fire,6.1f,"b",i),"медленнее 3 м/с цела, от 6 м/с бьётся");
                if(FireRules.Shatters(molotov.fire,4.5f,"b",i)) some++;
            }
            Require(some>5 && some<35,$"между 3 и 6 м/с — по шансу: {some} из 40");

            var room=Room();
            var bottle=new FireState {id="m",weapon="item:molotov",level="l",x=0,y=0,vx=9,vy=-3,seed=11};
            var splash=FireRules.Patches(bottle,room);
            Require(splash.Count>=10 && splash.Average(p=>p.position.x)>.5f && splash.All(p=>p.position.x<4),
                $"брызги бутылки — по ходу полёта и не за стену: {splash.Count} пятен, в среднем x={splash.Average(p=>p.position.x):0.00}");
            Require(FireRules.Patches(bottle,room).Select(p=>p.position).SequenceEqual(splash.Select(p=>p.position)),"одни и те же пятна при каждом расчёте");
            var burst=FireRules.Patches(new FireState {id="g",weapon="item:fgren",level="l",x=-6,y=0,seed=12},room);
            Require(burst.Count>=10 && burst.Any(p=>p.position.x<-6.3f) && burst.Any(p=>p.position.x>-5.7f) && Mathf.Abs(burst.Average(p=>p.position.x)+6)<1.2f,
                "граната разбрызгивает во все стороны");

            var torch=new FireState {id="t",weapon="item:molotov",level="l",x=1,y=0,torch=true,start=10};
            Require(Math.Abs(FireRules.Duration(torch)-15*10/3.0)<.01 && FireRules.Patches(torch,room).Count==1,"горелка — одно пятно на время всех 15 капель");
            Require(FireRules.Pulse(torch,10.1)==0 && FireRules.Pulse(torch,10.4)==1 && FireRules.Pulse(torch,9)==-1 && FireRules.Pulse(torch,61)==-1,"вспышки раз в треть секунды, вне огня нет");

            var near=new ActorState {id="a",actorType="pony",anatomy="pony",x=splash[0].position.x,y=splash[0].position.y,level="l"};
            var far=new ActorState {id="b",actorType="pony",anatomy="pony",x=splash[0].position.x+3,y=splash[0].position.y+2,level="l"};
            Require(FireRules.Touches(splash[0],near) && !FireRules.Touches(splash[0],far),"обжигает стоящего в пятне, не стоящего в стороне");

            // The bottle in flight: hard into the floor it breaks with the velocity it hit; a soft drop stays whole.
            var hard=new GrenadeState {id="hard",weapon="item:molotov",x=0,y=1,vx=6,vy=-8,remaining=molotov.grenadeFuseSeconds,rotationVersion=1};
            GrenadeRules.Step(hard,room,.5f);
            Require(hard.shattered && hard.remaining==0 && hard.impactVy<-6,"сильный удар разбивает бутылку");
            var soft=new GrenadeState {id="soft",weapon="item:molotov",x=0,y=.15f,vx=.5f,vy=0,remaining=molotov.grenadeFuseSeconds,rotationVersion=1};
            GrenadeRules.Step(soft,room,.5f);
            Require(!soft.shattered && soft.remaining>0,"мягкое падение не разбивает");

            var world=new WorldState();soft.remaining=0;world.grenades.Add(soft);hard.remaining=0;world.grenades.Add(hard);
            var lit=FireRules.Ignite(world,soft,3);var broke=FireRules.Ignite(world,hard,3);
            Require(lit.torch && !broke.torch && world.grenades.Count==0 && world.fires.Count==2 && Mathf.Approximately(broke.vy,hard.impactVy),"целая — горелка, разбитая — брызги со скоростью удара");
            Require(!FireRules.Expire(world,_=>4) && FireRules.Expire(world,_=>3+FireRules.Duration(broke)+.01) && world.fires.Count==1 && world.fires[0]==lit,"догоревший огонь убирается");
            Debug.Log($"Огонь: PASS — оригинальные числа, порог разбивания, брызги бутылки {splash.Count} по ходу и до стены, граната во все стороны, повторяемость, горелка, вспышки, касание, хост и догорание.");
        }
        private static void ActorTypesLoad() {ActorTypes.Load();Anatomy.Load();}
        private static RoomLayout Room()
        {
            var room=new RoomLayout {id="fire-check",left=-20,right=20,bottom=-1,top=10,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                new RoomSurface {x=-20,y=-1,width=40,height=1},new RoomSurface {x=4,y=0,width=.5f,height=6},
                new RoomSurface {x=-21,y=-1,width=1,height=12},new RoomSurface {x=20,y=-1,width=1,height=12}}};
            RoomSurfaces.Prepare(room);return room;
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Огонь: "+message);}
    }
}
