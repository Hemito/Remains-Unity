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
            Debug.Log("Сон тел: PASS — стопка засыпает, проверка по очереди, скорость будит, верхний падает без опоры, смена геометрии будит.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
