using System;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Exercises the production adapter with injected clocks/content; no Resources or network shortcuts.
    public static class PlatformSimulationChecks
    {
        private const string Level="factory:0";
        public static void Run()=>Run(true);
        public static void Run(bool log)
        {
            foreach(int fps in new[]{30,60,144})
            {
                InterleavedPassengers(fps,false);InterleavedPassengers(fps,true);
            }
            CommonFrameAndNewSegment();ClockRecovery();UnconfirmedEpoch();FinalDelta();GeometryLifecycle();
            if(log) Debug.Log("Адаптер платформы: PASS — carry A/Changed/carry B при 30/60/144, snapshot rebind, общие часы двух опор, segment/clock rebase без исторического переноса, неподтверждённый epoch, финальный delta, reload/add level на том же World. Сетевой runtime не проверен.");
        }
        private sealed class Fixture : IDisposable
        {
            public WorldState World;
            public double Now;
            public bool ClockConfirmed=true;
            public readonly SimulationGeometry Geometry;
            public readonly PlatformSimulation Simulation;
            public Fixture(double now=0,bool moving=true,bool confirmed=true)
            {
                Now=now;ClockConfirmed=confirmed;
                World=new WorldState {worldId="platform-simulation-check",levelTimeVersion=1};
                World.actors.Add(new ActorState {id="rider",owner="connection:1",player=true,connected=true,level=Level});
                World.levelTimes.Add(new LevelSimulationTime {level=Level,elapsedSeconds=now});
                World.movingPlatforms.Add(Platform("a",49.2f,9.62f,moving));
                World.movingPlatforms.Add(Platform("b",20,3,moving));
                Geometry=new SimulationGeometry(_=>Room());Geometry.Reconcile(World);
                Simulation=new PlatformSimulation(Geometry,()=>World,_=>ClockConfirmed,_=>Now,_=>Type());
                Simulation.Step(0); // Publish the initial baseline before priming any entity owner.
            }
            public RoomLayout RoomGeometry=>Geometry.Get(Level);
            public DynamicSurfaceFrame Frame=>RoomGeometry.dynamicSurfaces;
            public void Snapshot(Action<WorldState> mutate=null)
            {
                World=StateJson.Copy(World);mutate?.Invoke(World);
                Geometry.Reconcile(World);Simulation.Reconcile();
            }
            public void Advance(double delta) {Now+=delta;Simulation.Step(delta);}
            public Vector2 Feet(int index=0)
            {var surface=Frame.Pose(index).Current;return new Vector2(surface.x+surface.width*.5f,surface.y+surface.height);}
            public void Dispose()=>Simulation.Dispose();
        }
        private static RoomLayout Room()=>new() {id="platform-simulation-room",left=0,right=100,bottom=0,top=30,
            platforms=new[]{new RoomSurface {x=0,y=-1,width=100,height=1,texture="floor",movementSound="metal"}},
            ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>()};
        private static MovingPlatformDefinition Type()=>new() {id="platform2",version=1,width=2.4f,height=.78f,
            graphicWidth=2.4f,graphicHeight=.7f,durationSeconds=6,oneWay=false,shotsPassThrough=false,surfaceTexture="floor",movementSound="metal"};
        private static MovingPlatformState Platform(string id,float x,float y,bool moving)=>new()
        {
            id=Level+":"+id,definition="platform2",definitionVersion=1,level=Level,roomId="check",groupId="d2",revision=2,
            startX=x,startY=y,endX=x+16.8f,endY=y,endpoint=0,
            segment=moving?new MovingPlatformSegment {sequence=2,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=0,durationSeconds=6}:null
        };
        private static MovementHull Hull()=>new() {width=.8f,height=1.4f};
        private static PassengerMotionState State(string id)=>new(id,"connection:1",2,Level);
        private static void InterleavedPassengers(int fps,bool snapshot)
        {
            using var f=new Fixture();float dt=1f/fps;
            var actor=f.Feet();var actorState=State("actor");var cargoState=State("cargo");
            var cargo=new ContainerState {id="cargo",owner="connection:1",generation=2,level=Level,mass=5.2488f,
                x=actor.x,y=actor.y,width=.72f,height=.54f,anchorHeight=0};
            cargo.items.Add(new ItemStack {id="contents",definition="12p10",count=12});cargo.itemAliases.Add("former","contents");
            string inventory=StateJson.ToJson(cargo.items),aliases=StateJson.ToJson(cargo.itemAliases);
            PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,dt,actorState);PropMotion.Step(cargo,f.RoomGeometry,dt,null,passenger:cargoState);
            for(int i=0;i<fps;i++)
            {
                f.Advance(dt);actor=PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,dt,actorState);
                var frame=f.Frame;var before=frame.Pose(0);long sequence=frame.Sequence;
                var previous=Bounds(before.Previous);var current=Bounds(before.Current);
                if(snapshot) f.Snapshot();else {f.World.Find("rider").revision++;f.Simulation.Reconcile();}
                var after=f.Frame.Pose(0);
                Require(ReferenceEquals(frame,f.Frame) && f.Frame.Sequence==sequence && after.Continuous &&
                    Bounds(after.Previous)==previous && Bounds(after.Current)==current,"Changed перезаписал interval между пассажирами: fps="+fps+", snapshot="+snapshot);
                PropMotion.Step(cargo,f.RoomGeometry,dt,null,passenger:cargoState);
                Require(Near(actor,cargo.Position) && Near(actor,f.Feet()),"Поздний cargo пропустил delta: fps="+fps+", snapshot="+snapshot);
            }
            Require(StateJson.ToJson(cargo.items)==inventory && StateJson.ToJson(cargo.itemAliases)==aliases && Mathf.Abs(cargo.vx)<.0001f,
                "Snapshot/carry изменил содержимое или создал feedback скорости.");
        }
        private static void CommonFrameAndNewSegment()
        {
            using var f=new Fixture(3,false);
            var feetA=f.Feet(0);var feetB=f.Feet(1);var a=State("a-rider");var b=State("b-rider");
            PassengerMotion.Carry(f.RoomGeometry,Hull(),feetA,.1f,a);PassengerMotion.Carry(f.RoomGeometry,Hull(),feetB,.1f,b);
            f.Snapshot(world=>world.movingPlatforms[0].segment=new MovingPlatformSegment
                {sequence=3,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=0,durationSeconds=6});
            f.Advance(.1);
            var first=f.Frame.Pose(0);var second=f.Frame.Pose(1);
            Require(!first.Continuous && second.Continuous && Bounds(first.Previous)==Bounds(first.Current),"Новый segment сохранил исторический delta.");
            Require(first.Sequence==second.Sequence && first.PreviousElapsedSeconds==second.PreviousElapsedSeconds && first.ElapsedSeconds==second.ElapsedSeconds,
                "Две опоры одного кадра получили разные timestamps/sequence.");
            Require(Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feetA,.1f,a),feetA) && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feetB,.1f,b),feetB),
                "Snapshot нового segment телепортировал прежнего пассажира.");
            feetA=f.Feet(0);f.Advance(.1);
            Require(f.Frame.Pose(0).Continuous && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feetA,.1f,a),f.Feet(0)),"После segment rebase не возобновился текущий carry.");
        }
        private static void ClockRecovery()
        {
            using var f=new Fixture();var state=State("clock-rider");var feet=f.Feet();PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state);
            foreach(double correction in new[]{3d,1d})
            {
                f.Now=correction;f.Simulation.Step(.1);
                var pose=f.Frame.Pose(0);
                Require(!pose.Continuous && Bounds(pose.Previous)==Bounds(pose.Current) && pose.PreviousElapsedSeconds==pose.ElapsedSeconds,
                    "Clock jump/rewind не создал baseline.");
                Require(Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state),feet),"Clock rebase перенёс историческое движение.");
            }
            feet=f.Feet();f.Advance(.1);
            Require(f.Frame.Pose(0).Continuous && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state),f.Feet()),"После clock rebase не возобновился текущий carry.");
        }
        private static void UnconfirmedEpoch()
        {
            using var f=new Fixture(0,true,false);var state=State("epoch-rider");var feet=f.Feet();long sequence=f.Frame.Sequence;
            PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state);f.Now=3;f.Simulation.Step(.1);
            Require(f.Frame.Sequence==sequence && Near(f.Feet(),feet),"Неподтверждённый clock двигал сохранённую опору.");
            f.ClockConfirmed=true;f.Simulation.Step(.1);
            Require(!f.Frame.Pose(0).Continuous && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state),feet),"Первый поздний epoch перенёс историческое движение.");
        }
        private static void FinalDelta()
        {
            using var f=new Fixture(5.9);var state=State("arriving-rider");var feet=f.Feet();PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state);
            f.Snapshot(world=>{world.movingPlatforms[0].endpoint=1;world.movingPlatforms[0].segment=null;world.movingPlatforms[0].revision++;});
            f.Advance(.1);feet=PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state);
            Require(f.Frame.Pose(0).Continuous && Near(feet,new Vector2(66,10.4f)),"Snapshot завершения потерял последний delta.");
        }
        private static void GeometryLifecycle()
        {
            using var f=new Fixture(.5);var originalWorld=f.World;var originalFrame=f.Frame;long sequence=originalFrame.Sequence;
            const string separated="factory:6";
            f.World.actors.Add(new ActorState {id="separated-player",owner="connection:2",player=true,connected=true,level=separated});
            var platform=Platform("separated-platform",30,5,true);platform.level=separated;platform.id=separated+":platform";f.World.movingPlatforms.Add(platform);
            f.World.levelTimes.Add(new LevelSimulationTime {level=separated,elapsedSeconds=f.Now});
            f.Geometry.Reconcile(f.World);f.Simulation.Reconcile();
            var added=f.Geometry.Get(separated).dynamicSurfaces;
            Require(ReferenceEquals(originalWorld,f.World) && ReferenceEquals(originalFrame,f.Frame) && f.Frame.Sequence==sequence,
                "Добавление второй локации перезаписало текущую рамку прежней локации.");
            var expected=new MovingPlatformMotion(Type()).Evaluate(platform,f.Now).SupportBounds;
            Require(added!=null && added.Count==1 && added.IsPublished && Bounds(added.Pose(0).Current)==expected,
                "Actor-only загрузка второй локации не восстановила её платформу.");
            f.World.rooms.Add(new WorldRoom {level=Level,definition="rebuilt-room"});
            f.Geometry.Reconcile(f.World);f.Simulation.Reconcile();
            Require(ReferenceEquals(originalWorld,f.World) && !ReferenceEquals(originalFrame,f.Frame) && f.Frame.Count==2 && f.Frame.IsPublished,
                "Пересозданная геометрия на том же World reference осталась без опор.");
            for(int i=0;i<2;i++)
                Require(Bounds(f.Frame.Pose(i).Current)==new MovingPlatformMotion(Type()).Evaluate(f.World.movingPlatforms[i],f.Now).SupportBounds,
                    "Пересозданная комната получила устаревшую физическую позу: surface="+i);
        }
        private static Rect Bounds(RoomSurface surface)=>new(surface.x,surface.y,surface.width,surface.height);
        private static bool Near(Vector2 a,Vector2 b)=>(a-b).sqrMagnitude<.000001f;
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка адаптера платформы: "+reason);}
    }
}
