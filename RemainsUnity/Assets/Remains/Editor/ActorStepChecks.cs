using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorStepChecks
    {
        public static void Run()
        {
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                foreach(int direction in new[]{-1,1})
                {
                    var room=Fixture(direction,.2f);var actor=new ActorState {x=-2,y=0};
                    var controller=new MovementController(room);
                    for(int i=0;i<Mathf.CeilToInt(1.4f/dt);i++) controller.Step(actor,Vector2.right,false,dt);
                    Require(actor.x>2 && Mathf.Abs(actor.y-.6f*direction)<.001f,"Не пройдены три ступени",dt,actor.Position);
                    for(int i=0;i<Mathf.CeilToInt(1.4f/dt);i++) controller.Step(actor,Vector2.left,false,dt);
                    Require(actor.x<-1 && Mathf.Abs(actor.y)<.001f,"Обратное движение по ступеням",dt,actor.Position);
                }
                var entry=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/FactoryEntry46.prefab");
                var layout=entry.GetComponent<RoomAuthoring>().CreateLayout();
                var returning=new ActorState {x=16,y=5.6f};var movement=new MovementController(layout);
                for(int i=0;i<Mathf.CeilToInt(5/dt);i++)
                    movement.Step(returning,returning.x>layout.spawnX+.04f?Vector2.left:Vector2.zero,false,dt);
                Require(returning.x<3 && Mathf.Abs(returning.y-layout.spawnY)<.001f &&
                    ActorTerrainMotion.IsClear(layout,Anatomy.Movement(returning),returning.Position),"Возврат по исходной геометрии входа46",dt,returning.Position);
            }
            Limits();
            Debug.Log("Перешагивание: PASS — обе стороны ступеней и исходный проход входа46 при 30/60/144 Гц; высокая стена, потолок, воздух, отключение правила и пассивное тело.");
        }
        private static RoomLayout Fixture(int direction,float step)
        {
            var surfaces=new RoomSurface[4];
            for(int i=0;i<4;i++) surfaces[i]=new RoomSurface {x=i==0?-4:(i-1)*.8f,y=-2,width=i==0?4:i==3?4:.8f,height=2+i*step*direction};
            return new RoomLayout {id="step-check",left=-4,right=6,bottom=-3,top=6,platforms=surfaces,ladders=Array.Empty<RoomLadder>()};
        }
        private static void Limits()
        {
            var hull=new MovementHull {width=.6f,height=1.8f};
            bool CanStep(RoomLayout room,Vector2 start,float maximum)
            {
                var delta=new Vector2(.3f,-.01f);
                var blocked=ActorTerrainMotion.Move(room,hull,start,delta,false);
                return ActorStepMotion.TryMove(room,hull,start,delta,blocked,maximum,out _);
            }
            var room=Fixture(1,.2f);var origin=new Vector2(-.4f,0);
            Require(CanStep(room,origin,.4f),"Допустимый уступ заблокирован",0,origin);
            Require(!CanStep(room,origin,0),"Отключённое правило всё ещё действует",0,origin);
            Require(!CanStep(room,new Vector2(-.4f,.1f),.4f),"В воздухе применяется перешагивание",0,origin);
            Require(!CanStep(Fixture(1,.5f),origin,.4f),"Высокая стена стала ступенью",0,origin);
            Array.Resize(ref room.platforms,5);room.platforms[4]=new RoomSurface {x=-1,y=1.85f,width=2,height=.2f};
            Require(!CanStep(room,origin,.4f),"Голова прошла через низкий потолок",0,origin);
            room=Fixture(1,.2f);
            var passive=new ActorState {x=origin.x,y=0,vx=2,down=true};
            var controller=new MovementController(room);
            controller.Step(passive,Vector2.right,false,.15f);
            Require(passive.x<=-.2999f && passive.y<=.0001f,"Пассивное тело само взбирается",0,passive.Position);
        }
        private static void Require(bool valid,string reason,float dt,Vector2 position)
        {if(!valid) throw new InvalidOperationException($"Перешагивание: {reason}; dt={dt}, position={position}.");}
    }
}
