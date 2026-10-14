using System;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // BodyTopSurfaces: an actor lands on a box from above and stands on it, walks past it from the side, can drop
    // through it; a held box is not a support; other geometry queries do not see body tops.
    //   batch -executeMethod Remains.Editor.BodyTopSurfacesChecks.RunBatch
    public static class BodyTopSurfacesChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var room=new RoomLayout {id="check:body-tops",left=0,right=10,bottom=0,top=10,
                platforms=new[]{new RoomSurface {x=0,y=-1,width=10,height=1}}};
            RoomSurfaces.Prepare(room);
            var world=new WorldState();
            var box=new PropState {id="check:box",level=room.id,x=5,y=0,width=1,height=1,hasPhysicalBody=true};
            world.props.Add(box);
            var hull=new MovementHull {width=.6f,height=1.8f};
            BodyTopSurfaces.Update(room,world,room.id);

            float top=box.y-box.anchorHeight+box.height;   // the body top, as PropSupport stacks bodies
            var landed=ActorTerrainMotion.Move(room,hull,new Vector2(5,3),new Vector2(0,-5),false);
            Require(top>0 && Mathf.Abs(landed.y-top)<.001f,$"Падение не остановилось на ящике: y={landed.y}, верх ящика {top}.");
            Require(ActorTerrainMotion.Supported(room,hull,landed),"Стоящий на ящике не считается опорой.");
            Require(ActorTerrainMotion.CanDrop(room,hull,landed),"С ящика нельзя спрыгнуть вниз.");

            var walked=ActorTerrainMotion.Move(room,hull,new Vector2(2,0),new Vector2(6,0),false);
            Require(Mathf.Abs(walked.x-8)<.001f,$"Ящик преградил путь сбоку: x={walked.x}.");
            Require(ActorTerrainMotion.IsClear(room,hull,new Vector2(5,0)),"Ящик сделал место у пола занятым.");

            int general=0;foreach(var _ in RoomSurfaces.Near(room,new Rect(4,.2f,2,2))) general++;   // above the floor, around the box top
            Require(general==0,"Верх ящика виден общим запросам геометрии (выстрелы, NPC).");

            box.held=true;BodyTopSurfaces.Update(room,world,room.id);
            var fell=ActorTerrainMotion.Move(room,hull,new Vector2(5,3),new Vector2(0,-5),false);
            Require(Mathf.Abs(fell.y)<.001f,$"Удерживаемый ящик остался опорой: y={fell.y}.");
            Debug.Log("Опоры на телах: PASS — приземление и опора на ящике, проход сбоку, спрыгивание, удерживаемый ящик не опора, общие запросы не видят.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
