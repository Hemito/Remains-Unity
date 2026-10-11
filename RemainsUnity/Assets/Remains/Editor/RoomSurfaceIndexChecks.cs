using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomSurfaceIndexChecks
    {
        public static void RunBatch()
        {
            try
            {
                Anatomy.Load();WeaponCatalog.Load();Run();
                ActorStepChecks.Run();SlopedSupportChecks.Run();DoorGeometryChecks.Run();
                DynamicSupportChecks.Run();PlatformSimulationChecks.Run();ActorBodySupportChecks.Run();
                MeleeTerrainOcclusionChecks.Run();SurfaceSoundChecks.Run();NpcDoorChecks.Run();
                StandingSupportPoseChecks.Run();SlopeLocomotionPoseChecks.Run();
                Debug.Log("Индекс поверхностей: PASS — выборка, движение и существующие проверки опор.");
                EditorApplication.Exit(0);
            }
            catch(Exception exception) {Debug.LogException(exception);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var random=new System.Random(104);
            var surfaces=new List<RoomSurface>();
            for(int i=0;i<180;i++) surfaces.Add(new RoomSurface {
                x=Next(random,-30,30),y=Next(random,-20,20),width=Next(random,.1f,8),
                height=i%7==0?0:Next(random,.1f,4),oneWay=i%3==0,slopeDirection=i%11==0?1:0});
            surfaces.Add(new RoomSurface {x=-4,y=-4,width=4,height=4});
            surfaces.Add(new RoomSurface {x=0,y=0,width=4,height=0,oneWay=true});
            surfaces.Add(new RoomSurface {x=4,y=4,width=4,height=4});
            var room=Room(surfaces.ToArray());AttachDynamic(room);RoomSurfaces.Prepare(room);
            foreach(var rect in new[]{new Rect(0,0,0,0),new Rect(-4,-4,4,4),new Rect(4,4,0,0),new Rect(-100,-100,200,200)})
                Selection(room,rect,null);
            for(int i=0;i<1000;i++) Selection(room,new Rect(Next(random,-40,40),Next(random,-30,30),Next(random,0,14),Next(random,0,12)),
                i%4==0?surfaces[i%surfaces.Count]:i%4==1?room.dynamicSurfaces.Pose(0).Current:null);
            var outer=new Rect(-10,-10,20,20);int visited=0;
            foreach(var surface in RoomSurfaces.Near(room,outer)) {Selection(room,new Rect(surface.x,surface.y,.1f,.1f),surface);visited++;}
            Require(visited==Expected(room,outer,null).Count,"Вложенный обход изменил внешний перечислитель.");
            int local=0;foreach(var surface in RoomSurfaces.Near(room,new Rect(60,60,.2f,.2f))) local++;
            Require(local==0,"Локальная выборка вернула удалённую геометрию.");
            room.dynamicSurfaces.SetPose(0,new Rect(-2,2,2,.2f),new Rect(60,60,2,.2f),1,2,2,true);
            Selection(room,new Rect(60,60,.2f,.2f),null);Selection(room,new Rect(-2,2,2,.2f),null);
            room.platforms=new[]{new RoomSurface {x=60,y=60,width=2,height=2}};RoomSurfaces.Prepare(room);
            Selection(room,outer,null);Selection(room,new Rect(60,60,2,2),null);
            Motion(random);
        }
        private static void Motion(System.Random random)
        {
            var surfaces=new[]{
                new RoomSurface {x=-12,y=-2,width=12,height=2},
                new RoomSurface {x=0,y=-2,width=1,height=2.2f},
                new RoomSurface {x=1,y=-2,width=1,height=2.4f},
                new RoomSurface {x=2,y=0,width=4,height=3,oneWay=true,slopeDirection=1},
                new RoomSurface {x=6,y=3,width=5,height=0,oneWay=true},
                new RoomSurface {x=-4,y=4,width=3,height=.4f},
                new RoomSurface {x=10,y=-4,width=.3f,height=10},
                new RoomSurface {x=10,y=-4,width=.3f,height=10}};
            var indexed=Room(surfaces);var reference=Room(surfaces);AttachDynamic(indexed);reference.dynamicSurfaces=indexed.dynamicSurfaces;
            RoomSurfaces.Prepare(indexed);var hull=new MovementHull {width=.6f,height=1.8f};
            for(int i=0;i<2000;i++)
            {
                var start=new Vector2(Next(random,-11,11),Next(random,-3,9));
                var delta=new Vector2(Next(random,-20,20),Next(random,-15,15));
                RoomSurface excluded=i%5==0?surfaces[i%surfaces.Length]:i%5==1?indexed.dynamicSurfaces.Pose(0).Current:null;
                float? dropped=i%7==0?3f:null;bool ignore=i%3==0;
                var expected=ActorTerrainMotion.Move(reference,hull,start,delta,ignore,dropped,excluded);
                var actual=ActorTerrainMotion.Move(indexed,hull,start,delta,ignore,dropped,excluded);
                Require((actual-expected).sqrMagnitude<1e-10f,$"Движение отличается: i={i}, start={start}, delta={delta}, expected={expected}, actual={actual}.");
                Require(ActorTerrainMotion.IsClear(indexed,hull,start,excluded)==ActorTerrainMotion.IsClear(reference,hull,start,excluded),"IsClear отличается.");
                Require(ActorTerrainMotion.Supported(indexed,hull,start)==ActorTerrainMotion.Supported(reference,hull,start),"Supported отличается.");
                Require(ActorTerrainMotion.CanDrop(indexed,hull,start)==ActorTerrainMotion.CanDrop(reference,hull,start),"CanDrop отличается.");
                float radius=i%4*.15f;var end=start+delta;
                var hit=ShotObstacles.FirstContact(indexed,start,end,out var contacted,radius);
                float? bruteHit=null;RoomSurface bruteSurface=null;bool bruteVisible=true;
                foreach(var surface in RoomSurfaces.All(reference))
                {
                    var entry=SurfaceContact.Entry(surface,start,end,radius);
                    if(!surface.shotsPassThrough && entry.HasValue && (!bruteHit.HasValue || entry.Value<bruteHit.Value)) {bruteHit=entry;bruteSurface=surface;}
                    if(!surface.oneWay && SurfaceContact.Entry(surface,start,end).HasValue) bruteVisible=false;
                }
                Require(hit==bruteHit && ReferenceEquals(contacted,bruteSurface),$"Контакт выстрела/порядок равных контактов отличается: i={i}, radius={radius}.");
                Require(RoomVisibility.Clear(indexed,start,end)==bruteVisible,"Видимость отличается от полного обхода.");
            }
            for(float x=-8;x<10;x+=.1f)
            foreach(float y in new[]{0f,.2f,.4f,3f,2f})
            {
                var start=new Vector2(x,y);var delta=new Vector2(.3f,-.01f);
                var a=ActorTerrainMotion.Move(indexed,hull,start,delta,false);var b=ActorTerrainMotion.Move(reference,hull,start,delta,false);
                bool sa=ActorStepMotion.TryMove(indexed,hull,start,delta,a,.4f,out var pa);
                bool sb=ActorStepMotion.TryMove(reference,hull,start,delta,b,.4f,out var pb);
                Require(sa==sb && (!sa || (pa-pb).sqrMagnitude<1e-10f),"Перешагивание отличается.");
            }
        }
        private static RoomLayout Room(RoomSurface[] surfaces)=>new() {id="surface-index-check",left=-100,right=100,bottom=-100,top=100,platforms=surfaces,ladders=Array.Empty<RoomLadder>()};
        private static void AttachDynamic(RoomLayout room)
        {
            room.dynamicSurfaces=new DynamicSurfaceFrame();
            room.dynamicSurfaces.ReconcileIds(new[]{new DynamicSurfaceSpec("moving",true,false,"floor","stone")});
            room.dynamicSurfaces.SetPose(0,new Rect(-3,1,2,.2f),new Rect(-2,2,2,.2f),0,1,1,true);
        }
        private static List<RoomSurface> Expected(RoomLayout room,Rect rect,RoomSurface excluded)
        {
            // Near deliberately expands contact bounds by this conservative tolerance.
            rect=Rect.MinMaxRect(rect.xMin-.0001f,rect.yMin-.0001f,rect.xMax+.0001f,rect.yMax+.0001f);
            var result=new List<RoomSurface>();
            foreach(var surface in RoomSurfaces.All(room,excluded))
                if(surface.x<=rect.xMax && surface.x+surface.width>=rect.xMin && surface.y<=rect.yMax && surface.y+surface.height>=rect.yMin) result.Add(surface);
            return result;
        }
        private static void Selection(RoomLayout room,Rect rect,RoomSurface excluded)
        {
            var expected=Expected(room,rect,excluded);int index=0;
            foreach(var surface in RoomSurfaces.Near(room,rect,excluded))
            {Require(index<expected.Count && ReferenceEquals(surface,expected[index]),$"Состав/порядок выборки отличается: rect={rect}, index={index}.");index++;}
            Require(index==expected.Count,$"Пропущены поверхности: rect={rect}, expected={expected.Count}, actual={index}.");
        }
        private static float Next(System.Random random,float min,float max)=>min+(float)random.NextDouble()*(max-min);
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException("Индекс поверхностей: "+message);}
    }
}
