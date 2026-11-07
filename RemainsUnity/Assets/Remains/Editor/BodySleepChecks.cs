using System;
using Remains.Domain;
using Remains.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Body sleep (FreePropMotionContext): a settled box sleeps; velocity wakes it; moving the lower box of a stack wakes
    // the upper one, which then falls; a geometry change wakes sleepers; each sleeper is periodically re-verified.
    //   batch -executeMethod Remains.Editor.BodySleepChecks.RunBatch
    public static class BodySleepChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            const string level="check:sleep";
            var room=new RoomLayout {id=level,left=0,right=20,bottom=0,top=20,platforms=new[]{new RoomSurface {x=0,y=-1,width=20,height=1}}};
            RoomSurfaces.Prepare(room);
            var world=new WorldState();
            world.actors.Add(new ActorState {id="check:player",player=true,connected=true,level=level,owner="o",x=1,y=0});
            var lower=new PropState {id="check:lower",level=level,owner="o",x=5,y=2,width=1,height=1,hasPhysicalBody=true,movable=true,mass=10};
            var upper=new PropState {id="check:upper",level=level,owner="o",x=5,y=4,width=1,height=1,hasPhysicalBody=true,movable=true,mass=10};
            world.props.Add(lower);world.props.Add(upper);
            var context=new FreePropMotionContext();
            void Run(int frames) {for(int i=0;i<frames;i++) FreePropMotion.Step(world,"o",1f/60,_=>room,_=>false,null,null,context);}

            Run(240);
            float lowerTop=lower.y-lower.anchorHeight+lower.height;
            Require(Mathf.Abs(upper.y-upper.anchorHeight-lowerTop)<.01f,$"Стопка не легла: верхний y={upper.y}, верх нижнего {lowerTop}.");
            Run(1);
            Require(context.LastSleeping==2 || context.LastSleeping==1 && context.LastStepped==1,
                $"Покоящиеся ящики не уснули: шагов {context.LastStepped}, спят {context.LastSleeping}.");
            int maxStepped=0;for(int i=0;i<FreePropMotionContext.VerifyEvery;i++) {Run(1);maxStepped=Math.Max(maxStepped,context.LastStepped);}
            Require(maxStepped>=1 && maxStepped<=2,$"Спящие не проверяются по очереди или проверяются все сразу: максимум шагов {maxStepped}.");

            // Velocity wakes a sleeper.
            lower.vx=40;Run(1);
            Require(context.LastStepped>=1,"Скорость не разбудила ящик.");
            // The lower box slid away from under the upper one: the upper wakes and falls to the floor.
            Run(120);
            Require(Mathf.Abs(upper.x-lower.x)>=(upper.width+lower.width)*.5f,$"Нижний ящик не уехал: нижний x={lower.x}, верхний x={upper.x}.");
            Require(Mathf.Abs(upper.y-upper.anchorHeight)<.01f,$"Верхний ящик остался висеть: верхний ({upper.x},{upper.y}), нижний ({lower.x},{lower.y}).");
            // A support removed by another path (teleported away): the periodic verification lets the upper box fall.
            Run(120);
            lower.x=upper.x;lower.y=upper.y-upper.anchorHeight+lower.anchorHeight;   // rebuild the stack: lower under upper
            upper.y+=2;Run(240);
            Require(Mathf.Abs(upper.y-upper.anchorHeight-(lower.y-lower.anchorHeight+lower.height))<.01f,"Стопка не собралась заново.");
            Run(60);lower.x+=5;Run(FreePropMotionContext.VerifyEvery*2+5);
            Require(Mathf.Abs(upper.y-upper.anchorHeight)<.01f,$"После исчезновения опоры другим путём верхний ящик висит: y={upper.y}.");

            // A geometry change (a door opening, new platforms array) wakes sleepers.
            Run(120);
            room.platforms=(RoomSurface[])room.platforms.Clone();RoomSurfaces.Prepare(room);
            Run(1);
            Require(context.LastStepped==2,$"Смена геометрии не разбудила тела: шагов {context.LastStepped}.");
            CheckTrackedSupport();
            Debug.Log("Сон тел: PASS — стопка засыпает, проверка по очереди, скорость будит, верхний падает без опоры, смена геометрии будит.");
        }

        private static void CheckTrackedSupport()
        {
            const string level="tracked:sleep";
            var room=new RoomLayout {id=level,left=0,right=30,bottom=0,top=20,
                platforms=new[]{new RoomSurface {x=0,y=-1,width=30,height=1}}};
            RoomSurfaces.Prepare(room);
            var world=new WorldState();
            world.actors.Add(new ActorState {id="tracked:player",player=true,connected=true,level=level,owner="local"});
            var lower=new PropState {id="tracked:lower",level=level,owner="peer",x=5,y=.5f,width=1,height=1,anchorHeight=.5f};
            var upper=new PropState {id="tracked:upper",level=level,owner="local",x=5,y=1.5f,width=1,height=1,anchorHeight=.5f};
            world.props.Add(lower);world.props.Add(upper);
            var context=new FreePropMotionContext(trackSupport:true);
            int published=0;
            for(int i=0;i<17;i++) FreePropMotion.Step(world,"local",1f/60,_=>room,_=>false,_=>published++,null,context);
            Require(context.LastStepped==0 && context.LastSleeping==1 && published==10,
                "Спящее тело продолжило обычный шаг или обычную отправку позы.");
            var cadence=new FreePropPoseCadence();
            Require(cadence.ShouldPublish(upper,0,out bool initial) && initial,"Первая поза не опубликована.");
            int heartbeats=0;
            cadence.PublishDue(.04,_=>heartbeats++);
            cadence.PublishDue(1,id=>
            {
                Require(id==upper.id && cadence.ShouldPublish(upper,1,out bool changed) && !changed,
                    "Повтор покоя изменил версию сетевой позы.");
                heartbeats++;
            });
            cadence.PublishDue(1.99,_=>heartbeats++);
            Require(heartbeats==1,"Повтор позы прошёл раньше срока или не попал в очередь.");
            cadence.PublishDue(2,id=>cadence.Defer(id,2));
            cadence.PublishDue(2.99,_=>heartbeats++);
            cadence.PublishDue(3,id=>
            {
                Require(id==upper.id && cadence.ShouldPublish(upper,3,out bool changed) && !changed,
                    "После паузы повтор потерял позу или изменил её версию.");
                heartbeats++;
            });
            Require(heartbeats==2,"Отложенный повтор позы не вернулся в очередь.");
            var notify=typeof(FreePropMotionContext).GetMethod("ExternalMotion",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Require(notify!=null,"Не найден вход изменения сетевой позы.");
            var invalidate=typeof(FreePropMotionContext).GetMethod("InvalidateSupport",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Require(invalidate!=null,"Не найден вход изменения состава тел.");
            invalidate.Invoke(context,null);
            var previous=lower.Position;lower.x=11;
            notify.Invoke(context,new object[]{lower,previous});
            FreePropMotion.Step(world,"local",1f/60,_=>room,_=>false,null,null,context);
            Require(context.LastStepped==1 && upper.y<1.5f && upper.vy<0,
                "Сдвиг чужой опоры в другой сектор не разбудил тело в следующий шаг.");
            for(int i=0;i<120;i++) FreePropMotion.Step(world,"local",1f/60,_=>room,_=>false,null,null,context);
            var impulse=typeof(FreePropMotionContext).GetMethod("WakeImpulse",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Require(impulse!=null,"Не найден вход импульса сетевого контакта.");
            float x=upper.x;upper.vx=1;upper.generation++;
            impulse.Invoke(context,new object[]{upper});
            FreePropMotion.Step(world,"local",1f/60,_=>room,_=>false,null,null,context);
            Require(context.LastStepped==1 && upper.x>x,"Импульс не разбудил тело в следующий шаг.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
