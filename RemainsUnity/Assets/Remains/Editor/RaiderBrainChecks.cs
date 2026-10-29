using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // RaiderBrain against the UnitRaider.control rules it ports, on small rooms: a calm raider strolls and turns at an
    // edge, notices a seen target after its delay, shoots about one frame in ten while attacking and never without
    // sight, jumps toward a point above only with free cells over it, steps back from a close target, drops through a
    // platform toward a point below, catches a ladder toward a point above, and calms down after losing the target.
    public static class RaiderBrainChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var type=ActorTypes.Get("raider");
            Require(type.behavior==NpcCreatureBehaviours.Raider && type.raider!=null && type.raider.attacker==RaiderAttacker.Ranged &&
                Mathf.Approximately(type.raider.walkSpeed,2.4f) && Mathf.Approximately(type.raider.jumpSpeed,10.8f),"рейдер-бунтарь (raider5): стрелок, speed 4, jump 18");
            var s=type.raider;
            Stroll(s);Notice(s);Shooting(s);Jumps(s);StepBack(s);Vertical(s);Calm(s);Drive(type);
            Debug.Log("Рейдер: PASS — прогулка и край, замечание с задержкой, стрельба ~10% кадров только с целью, прыжок вверх при свободном месте, отскок, спрыгивание, лестница, успокоение.");
        }

        private static RoomLayout Room(params RoomSurface[] extra)
        {
            var platforms=new RoomSurface[extra.Length+1];
            platforms[0]=new RoomSurface {x=-20,y=-1,width=40,height=1};
            Array.Copy(extra,0,platforms,1,extra.Length);
            var room=new RoomLayout {id="raider-check",left=-20,right=20,bottom=-1,top=20,platforms=platforms,ladders=Array.Empty<RoomLadder>()};
            RoomSurfaces.Prepare(room);
            return room;
        }
        private static ActorState Raider(float x=0)=>new() {id="r",actorType="raider",anatomy="raider",level="l",x=x,y=0,health=50,facing=1};
        private static readonly RaiderSenses Nothing=new(false,default,false,default);
        private static RaiderSenses Sees(Vector2 point)=>new(true,point,true,point);

        private static void Stroll(RaiderSettings s)
        {
            // A ledge 1 m to the right: the strolling raider walks toward it and turns there (no ground beyond).
            var room=new RoomLayout {id="ledge",left=-20,right=20,bottom=-5,top=20,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-20,y=-1,width=21,height=1}}};
            RoomSurfaces.Prepare(room);
            var random=new System.Random(1);var mind=new RaiderMind {state=RaiderState.Stroll,timer=500};
            var actor=Raider(.85f);bool turned=false;
            for(int i=0;i<200 && !turned;i++)
            {
                var intent=RaiderBrain.Tick(mind,s,actor,room,Nothing,false,random);
                if(i==0) Require(intent.move==1 && !intent.run && !intent.shoot,"прогулка — шагом в своём направлении, без стрельбы");
                turned=mind.direction==-1;
            }
            Require(turned,"у края без опоры впереди рейдер разворачивается");
        }

        private static void Notice(RaiderSettings s)
        {
            var room=Room();var random=new System.Random(2);var mind=new RaiderMind {state=RaiderState.Stroll,timer=12};
            var actor=Raider();var target=new Vector2(6,.7f);int frames=0;
            while(mind.state!=RaiderState.Notice && frames<40) {RaiderBrain.Tick(mind,s,actor,room,Sees(target),false,random);frames++;}
            Require(mind.state==RaiderState.Notice && mind.timer>=s.slowness,"увиденная цель сначала замечается с задержкой tupizna");
            int delay=0;
            while(mind.state==RaiderState.Notice && delay<200) {RaiderBrain.Tick(mind,s,actor,room,Sees(target),false,random);delay++;}
            Require(delay>=s.slowness && (mind.state==RaiderState.Pursue || mind.state==RaiderState.Hold) && mind.alert>=40,"после задержки — атака");
        }

        private static void Shooting(RaiderSettings s)
        {
            var room=Room();var random=new System.Random(3);
            var mind=new RaiderMind {state=RaiderState.Pursue,timer=100000,alert=40,aware=true};
            var actor=Raider();var target=new Vector2(8,.7f);int shots=0,frames=3000;
            for(int i=0;i<frames;i++) {mind.timer=100000;if(RaiderBrain.Tick(mind,s,actor,room,Sees(target),false,random).shoot) shots++;actor.x=0;}
            float rate=shots/(float)frames;
            Require(rate>.08f && rate<.12f,$"стрельба с целью ≈ 10% кадров (attack: isrnd 0.1), получено {rate:P1}");
            mind=new RaiderMind {state=RaiderState.Pursue,timer=100000,alert=40,aware=true};
            for(int i=0;i<300;i++) {mind.timer=100000;Require(!RaiderBrain.Tick(mind,s,actor,room,Nothing,false,random).shoot,"без видимой цели рейдер не стреляет");}
        }

        private static void Jumps(RaiderSettings s)
        {
            var target=new Vector2(0,4);
            bool Jumped(RoomLayout room)
            {
                var random=new System.Random(4);var mind=new RaiderMind {state=RaiderState.Pursue,timer=100000,alert=40,aware=true};
                for(int i=0;i<60;i++) {mind.timer=100001-i;if(RaiderBrain.Tick(mind,s,Raider(),room,Sees(target),false,random).jump>0) return true;}
                return false;
            }
            Require(Jumped(Room()),"цель выше и над головой свободно — прыжок (checkJump)");
            Require(!Jumped(Room(new RoomSurface {x=-3,y=2.2f,width=6,height=.5f})),"над головой перекрытие на 85–125 px — без прыжка");
        }

        private static void StepBack(RaiderSettings s)
        {
            var room=Room();bool back=false;
            for(int seed=0;seed<20 && !back;seed++)
            {
                var mind=new RaiderMind {state=RaiderState.Hold,timer=500,alert=40,aware=true,point=new Vector2(2,.7f)};
                var intent=RaiderBrain.Tick(mind,s,Raider(),room,Sees(new Vector2(2,.7f)),false,new System.Random(seed));
                back=intent.pushX<0 && Mathf.Approximately(intent.jump,.4f);
                Require(mind.state==RaiderState.Pursue,"стрельба с места у близкой цели переходит в преследование");
            }
            Require(back,"близкая цель (< 200 px): отскок назад с прыжком 0,4");
        }

        private static void Vertical(RaiderSettings s)
        {
            var room=Room();var random=new System.Random(5);
            var mind=new RaiderMind {state=RaiderState.Pursue,timer=100000,alert=40,aware=true,point=new Vector2(3,-3)};
            Require(RaiderBrain.Tick(mind,s,Raider(),room,Sees(new Vector2(3,-3)),false,random).dropThrough,"цель ниже 80 px — сквозь платформу");
            mind=new RaiderMind {state=RaiderState.Pursue,timer=100000,alert=40,aware=true,ladderTime=-31,point=new Vector2(0,6)};
            var low=Room(new RoomSurface {x=-3,y=2,width=6,height=.5f});
            Require(RaiderBrain.Tick(mind,s,Raider(),low,Sees(new Vector2(0,6)),false,random).climb==1,"цель выше — хвататься за лестницу вверх (checkStairs)");
        }

        private static void Calm(RaiderSettings s)
        {
            var room=Room();var random=new System.Random(6);
            var mind=new RaiderMind {state=RaiderState.Pursue,timer=0,alert=40,aware=true,point=new Vector2(5,.7f)};
            int frames=0;
            while(mind.alert>0 && frames<10000) {RaiderBrain.Tick(mind,s,Raider(),room,Nothing,false,random);frames++;}
            Require(mind.alert==0 && frames>=300,"потеряв цель, рейдер ищет ~(maxSpok+10)×10 кадров и успокаивается");
            for(int i=0;i<400;i++) RaiderBrain.Tick(mind,s,Raider(),room,Nothing,false,random);
            Require(mind.state<=RaiderState.Stroll,"спокойный рейдер стоит или гуляет");
        }

        // The driver with the real movement controller: a seen target 12 m away on a flat floor is approached on the
        // floor, the raider faces it and wants shots.
        private static void Drive(ActorTypeDefinition type)
        {
            var room=Room();var raiders=new Remains.Gameplay.NpcRaiderController(MovementSettings.Load());
            var actor=Raider();var target=new ActorState {id="p",player=true,connected=true,level="l",x=12,y=0,health=100,actorType="pony",anatomy="pony"};
            int shots=0;float dt=1f/60;
            for(int i=0;i<600;i++)
            {
                if(raiders.Step(actor,type,target,true,room,dt)) shots++;
                raiders.EndFrame();
                Require(float.IsFinite(actor.x) && actor.y>-.01f && actor.y<4,"рейдер остаётся в комнате и на полу/в прыжке");
            }
            Require(actor.x>2 && actor.facing==1 && shots>0,$"рейдер идёт к видимой цели, смотрит на неё и хочет стрелять: x={actor.x:0.00}, выстрелов {shots}");
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Рейдер: "+message);}
    }
}
