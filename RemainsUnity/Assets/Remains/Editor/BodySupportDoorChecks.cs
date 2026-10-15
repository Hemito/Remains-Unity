using System;
using System.Diagnostics;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // Door changes re-prepare only the door tail of the actor body support (ActorBodySupport.PreparedRoom): on the
    // full factory (seed 104) the incremental result after opening/closing doors equals an independent full
    // preparation of the same surfaces (door surfaces relabelled as level surfaces, i.e. the all-surfaces path).
    //   batch -executeMethod Remains.Editor.BodySupportDoorChecks.RunBatch
    public static class BodySupportDoorChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {FactoryContentSources.Clear();}
        }

        public static void Run()
        {
            FootSupportCatalog.Load();
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
            var world=new WorldState{seed=104};
            FactoryPreparation.Create(world,catalog,0);
            FactoryContentSources.Prepare(world,"factory:0");
            var source=RoomAuthoring.Load("factory:0",world);
            DoorContentInstaller.Apply(world,RoomDoorContent.Read(source,"factory:0"));
            var room=source.CreateLayout(false);
            var authored=room.platforms.Where(surface=>surface.sourceDoorId==null).ToArray();
            var doors=world.doors.Where(door=>door.level==room.id).ToArray();
            if(doors.Length<4) throw new InvalidOperationException("На заводе мало дверей для проверки: "+doors.Length);
            room.platforms=DoorGeometry.Compose(authored,doors,room.id);
            ActorBodySupport.PreparedSignature(room);   // initial full preparation

            double slowest=0,total=0;int steps=0;
            foreach(var door in doors.Take(6))
                for(int toggle=0;toggle<2;toggle++)
                {
                    door.open=!door.open;door.revision++;
                    room.platforms=DoorGeometry.Compose(authored,doors,room.id);
                    var timer=Stopwatch.StartNew();
                    ActorBodySupport.InvalidateRoom(room);
                    double ms=timer.Elapsed.TotalMilliseconds;if(steps>0) {slowest=Math.Max(slowest,ms);total+=ms;}steps++;
                    string incremental=ActorBodySupport.PreparedSignature(room);
                    // Independent: the same surfaces as plain level surfaces in a fresh layout (full preparation).
                    var oracle=new RoomLayout{id=room.id,left=room.left,right=room.right,bottom=room.bottom,top=room.top,movementSound=room.movementSound,
                        platforms=room.platforms.Select(surface=>new RoomSurface{x=surface.x,y=surface.y,width=surface.width,height=surface.height,
                            texture=surface.texture,movementSound=surface.movementSound,oneWay=surface.oneWay,shotsPassThrough=surface.shotsPassThrough,
                            slopeDirection=surface.slopeDirection}).ToArray()};
                    string full=ActorBodySupport.PreparedSignature(oracle);
                    if(incremental!=full)
                    {
                        int at=0;while(at<Math.Min(incremental.Length,full.Length) && incremental[at]==full[at]) at++;
                        throw new InvalidOperationException($"Опоры после двери {door.id} (open={door.open}) отличаются от полной подготовки с символа {at}: «{Around(incremental,at)}» против «{Around(full,at)}».");
                    }
                }
            Debug.Log($"Опоры при дверях: PASS — {steps} переключений {Math.Min(6,doors.Length)} дверей, частичная подготовка совпадает с полной; после первой среднее {total/Math.Max(1,steps-1):F2} мс, самая долгая {slowest:F1} мс (поверхностей уровня {authored.Length}).");
        }

        private static string Around(string text,int at)=>text.Substring(Math.Max(0,at-60),Math.Min(120,text.Length-Math.Max(0,at-60)));
    }
}
