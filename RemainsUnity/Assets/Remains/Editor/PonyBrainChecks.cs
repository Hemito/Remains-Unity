using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // PonyBrain, the sapient ponies' utility brain, on a flat floor: calm without a target, the reaction delay before a
    // seen target is acted on, a shooter shooting in range and stepping back from a close one, teeth closing in and
    // touching, a levitated weapon swinging from its distance, a hurt pony retreating and, cornered, fighting again, a
    // lost target searched and forgotten, and no flicker between intents while nothing changes.
    public static class PonyBrainChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var shooter=ActorTypes.Get("raider").raider;
            Require(shooter.pony!=null && Mathf.Approximately(shooter.pony.retreatHealth,.25f) && SapientPonies.Is(Raider()) &&
                !SapientPonies.Is(new ActorState {id="t",actorType="tarakan"}),"у рейдера — характер разумного пони по умолчанию; таракан не разумный пони");
            Calm(shooter);Notice(shooter);Shooting(shooter);Melee(shooter);Retreat(shooter);Search(shooter);
            Debug.Log("Мозг разумных пони: PASS — спокойствие, задержка реакции, стрельба с отскоком, ближний бой в зубах и телекинезом, отступление и бой в углу, поиск, без дёрганья.");
        }

        private static RoomLayout Room(float left=-20,float right=20)
        {
            var room=new RoomLayout {id="pony-brain-check",left=left,right=right,bottom=-1,top=20,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=left,y=-1,width=right-left,height=1},new RoomSurface {x=left-1,y=-1,width=1,height=21},new RoomSurface {x=right,y=-1,width=1,height=21}}};
            RoomSurfaces.Prepare(room);
            return room;
        }
        private static ActorState Raider(float x=0)=>new() {id="r",actorType="raider",anatomy="raider",level="l",x=x,y=0,health=50,facing=1};
        private static RaiderSenses Sees(Vector2 point)=>new(true,point,true,point);
        private static readonly RaiderSenses Nothing=new(false,default,false,default);
        private sealed class Pony
        {
            public readonly PonyMind pony=new() {maxHealth=50};public readonly RaiderMind mind=new();
            public readonly ActorState actor;public readonly RoomLayout room;public readonly RaiderSettings s;public readonly System.Random random;
            public Pony(RaiderSettings s,ActorState actor,RoomLayout room,int seed) {this.s=s;this.actor=actor;this.room=room;random=new System.Random(seed);}
            public RaiderIntent Tick(RaiderSenses senses,RaiderAttacker? attacker=null,bool wall=false)=>
                PonyBrain.Tick(pony,mind,s,actor,room,senses,wall,random,attacker??s.attacker);
            // Seen long enough to be alarmed (the reaction delay over).
            public void Alarm(Vector2 target) {for(int i=0;i<200 && (pony.reaction>0 || mind.alert==0);i++) Tick(Sees(target));}
        }

        private static void Calm(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),1);int moving=0;
            for(int i=0;i<300;i++) {if(run.Tick(Nothing).move!=0) moving++;Require(run.pony.intent==PonyIntentKind.Calm || i<PonyBrain.DecisionFrames,"без цели — спокоен");}
            Require(moving>0 && moving<300,$"спокойный то гуляет, то стоит: шагов {moving} из 300");
        }

        private static void Notice(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),2);var target=new Vector2(8,.7f);int delay=0;bool shot=false;
            for(int i=0;i<12;i++) run.Tick(Sees(target));
            while(run.pony.reaction>0 && delay<200) {Require(run.pony.intent==PonyIntentKind.Notice || delay<PonyBrain.DecisionFrames,"замечая цель, стоит");shot|=run.Tick(Sees(target)).shoot;delay++;}
            Require(delay>=s.slowness/2 && !shot,$"увиденная цель сначала замечается с задержкой tupizna и без выстрелов: {delay} кадров");
        }

        private static void Shooting(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),3);var target=new Vector2(8,.7f);run.Alarm(target);
            int shots=0,switches=0;var last=run.pony.intent;
            for(int i=0;i<600;i++)
            {
                run.actor.x=0;run.actor.vx=0;
                if(run.Tick(Sees(target)).shoot) shots++;
                if(run.pony.intent!=last) {switches++;last=run.pony.intent;}
            }
            Require(run.pony.intent==PonyIntentKind.Shoot && shots>20 && switches<=1,$"стрелок в пределах дальности стреляет и не дёргается: выстрелов {shots}, смен {switches}");
            // A target 1.5 m away: the shooter steps back (state 4) now and then.
            var close=new Vector2(1.5f,.7f);bool back=false;
            for(int i=0;i<120;i++) {run.actor.x=0;var intent=run.Tick(Sees(close));back|=intent.pushX<0;}
            Require(back,"от близкой цели стрелок отскакивает");
        }

        private static void Melee(RaiderSettings source)
        {
            var teeth=StateJson.Copy(source);teeth.attacker=RaiderAttacker.Teeth;
            var run=new Pony(teeth,Raider(),Room(),4);var target=new Vector2(3,.7f);run.Alarm(target);
            int touches=0;bool charge=true;
            for(int i=0;i<60;i++) {run.actor.x=0;var intent=run.Tick(Sees(target));if(intent.bodyHit>0) touches++;charge&=run.pony.intent!=PonyIntentKind.Melee || intent.charge;}
            Require(run.pony.intent==PonyIntentKind.Melee && touches>0 && charge,$"оружие в зубах — ближний бой бегом и удар: ударов {touches}");

            var levitated=StateJson.Copy(source);levitated.attacker=RaiderAttacker.Levitated;levitated.swingHeight=2.4f;
            run=new Pony(levitated,Raider(),Room(),5);target=new Vector2(1.5f,.7f);run.Alarm(target);
            int swings=0,steps=0;
            for(int i=0;i<300;i++) {run.actor.x=0;var intent=run.Tick(Sees(target));if(intent.swing) swings++;if(intent.move!=0) steps++;}
            Require(run.pony.intent==PonyIntentKind.Melee && swings>45 && steps==0,$"оружие телекинезом — машет с 50–100 px, стоя: взмахов {swings}, шагов {steps}");
        }

        private static void Retreat(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),6);var target=new Vector2(6,.7f);run.Alarm(target);
            run.actor.health=5;int away=0;
            for(int i=0;i<30;i++) {run.actor.x=0;if(run.Tick(Sees(target)).move<0) away++;}
            Require(run.pony.intent==PonyIntentKind.Retreat && away>0,$"раненый (10%) отступает от цели: шагов прочь {away}");
            // Against a wall it is cornered: it stops and after a while fights again.
            bool fights=false;
            for(int i=0;i<200 && !fights;i++) {run.actor.x=0;run.Tick(Sees(target),wall:true);fights=run.pony.intent!=PonyIntentKind.Retreat;}
            Require(fights,"загнанный в угол снова дерётся");
        }

        private static void Search(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),7);var target=new Vector2(8,.7f);run.Alarm(target);
            for(int i=0;i<30;i++) run.Tick(Nothing);
            Require(run.pony.intent==PonyIntentKind.Search,$"потеряв цель, ищет: {run.pony.intent}");
            int frames=0;
            while(run.pony.intent!=PonyIntentKind.Calm && frames<20000) {run.actor.x=0;run.Tick(Nothing);frames++;}
            Require(run.pony.intent==PonyIntentKind.Calm,"поиск затухает, рейдер успокаивается");
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Мозг разумных пони: "+message);}
    }
}
