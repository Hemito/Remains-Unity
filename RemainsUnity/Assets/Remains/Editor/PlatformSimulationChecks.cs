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
                AcceleratedPlayback(fps);EarlyCompletion(fps);
            }
            CommonFrameAndNewSegment();ReplacedMovingSegment();PendingDepartureReplacement();LateDepartureContacts();ClockRecovery();UnconfirmedEpoch();FinalDelta();GeometryLifecycle();
            CheckDepartures(Type(),Platform("departure",49.2f,9.62f,false),Room(),Hull());
            InPlaceCompletion();RouteAndDefinitionRebind();PassengerIdentityRebind();
            if(log) Debug.Log("Адаптер платформы: PASS — carry A/Changed/carry B при 30/60/144, accelerated playback с обычным dt, ранний canonical completion без скачка, snapshot rebind, первый departure/reverse уже стоящего пассажира и груза, future start внутри кадра, позднее подтверждение известной неподвижной опоры с однократным sweep и frozen-time defer, in-place completion, type/route/owner rebind, общие часы двух опор, moving predecessor/clock rebase без исторического переноса, неподтверждённый epoch, финальный delta, reload/add level на том же World. Сетевой runtime не проверен.");
        }
        private sealed class Fixture : IDisposable
        {
            public WorldState World;
            public double Now;
            public bool ClockConfirmed=true;
            public MovingPlatformDefinition Definition;
            public readonly SimulationGeometry Geometry;
            public readonly PlatformSimulation Simulation;
            public Fixture(double now=0,bool moving=true,bool confirmed=true,MovingPlatformDefinition definition=null,
                MovingPlatformState platform=null,RoomLayout room=null)
            {
                Now=now;ClockConfirmed=confirmed;Definition=StateJson.Copy(definition??Type());
                World=new WorldState {worldId="platform-simulation-check",levelTimeVersion=1};
                World.actors.Add(new ActorState {id="rider",owner="connection:1",player=true,connected=true,level=Level});
                World.levelTimes.Add(new LevelSimulationTime {level=Level,elapsedSeconds=now});
                if(platform==null)
                {World.movingPlatforms.Add(Platform("a",49.2f,9.62f,moving));World.movingPlatforms.Add(Platform("b",20,3,moving));}
                else World.movingPlatforms.Add(StateJson.Copy(platform));
                Geometry=new SimulationGeometry(_=>room==null?Room():StateJson.Copy(room));Geometry.Reconcile(World);
                Simulation=new PlatformSimulation(Geometry,()=>World,_=>ClockConfirmed,_=>Now,_=>Definition);
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
        private static ContainerState Cargo(Vector2 position)
        {
            var cargo=new ContainerState {id="cargo",owner="connection:1",generation=2,level=Level,mass=5.2488f,
                x=position.x,y=position.y,width=.72f,height=.54f,anchorHeight=0};
            cargo.items.Add(new ItemStack {id="contents",definition="12p10",count=12});cargo.itemAliases.Add("former","contents");return cargo;
        }
        private static void InterleavedPassengers(int fps,bool snapshot)
        {
            using var f=new Fixture();float dt=1f/fps;
            var actor=f.Feet();var actorState=State("actor");var cargoState=State("cargo");
            var cargo=Cargo(actor);
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
        private static void AcceleratedPlayback(int fps)
        {
            using var f=new Fixture();float realDt=1f/fps;var playback=new LevelTimePlayback();playback.Advance(0,true,0);
            var actor=new ActorState {id="accelerated-rider",owner="connection:1",generation=2,level=Level,player=true,connected=true};actor.Position=f.Feet();
            var actorState=State(actor.id);var cargo=Cargo(actor.Position);var cargoState=State(cargo.id);
            PassengerMotion.Carry(f.RoomGeometry,Hull(),actor.Position,realDt,actorState);PropMotion.Step(cargo,f.RoomGeometry,realDt,null,passenger:cargoState);
            string items=StateJson.ToJson(cargo.items),aliases=StateJson.ToJson(cargo.itemAliases),mechanism=StateJson.ToJson(f.World.movingPlatforms[0]);
            double canonicalElapsed=f.World.levelTimes[0].elapsedSeconds;
            for(int i=1;i<=fps*2;i++)
            {
                double previous=playback.ElapsedSeconds;playback.Advance(i*(double)realDt+2,true,realDt);
                Require(playback.ElapsedSeconds-previous>realDt+.001 && LevelTimePlayback.IsContinuous(previous,playback.ElapsedSeconds,realDt),
                    "Fixture не упражняет ускоренный clock с прежним real dt: fps="+fps);
                f.Now=playback.ElapsedSeconds;f.Simulation.Step(realDt);
                Require(f.Frame.Pose(0).Continuous && PassengerMotion.PreviouslySupported(f.RoomGeometry,Hull(),actor.Position,realDt),
                    "Accelerated frame не принят общим support contract: fps="+fps);
                actor.Position=PassengerMotion.Carry(f.RoomGeometry,Hull(),actor.Position,realDt,actorState);
                if(i%3==0) f.Snapshot();
                PropMotion.Step(cargo,f.RoomGeometry,realDt,null,passenger:cargoState);
                Require(Near(actor.Position,f.Feet()) && Near(cargo.Position,actor.Position),"Actor/cargo потерял ускоренный delta при обычном dt: fps="+fps);
            }
            Require(actor.x>55.8f && Mathf.Abs(cargo.vx)<.0001f && Mathf.Abs(cargo.vy)<.0001f &&
                StateJson.ToJson(cargo.items)==items && StateJson.ToJson(cargo.itemAliases)==aliases &&
                StateJson.ToJson(f.World.movingPlatforms[0])==mechanism && f.World.levelTimes[0].elapsedSeconds==canonicalElapsed,
                "Playback не перевёз дополнительный путь или изменил cargo/canonical state.");
        }
        private static void EarlyCompletion(int fps)
        {
            using var f=new Fixture(5.7);float realDt=1f/fps;var actor=f.Feet();var actorState=State("tail-rider");
            var cargo=Cargo(actor);var cargoState=State(cargo.id);
            PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,realDt,actorState);PropMotion.Step(cargo,f.RoomGeometry,realDt,null,passenger:cargoState);
            f.Snapshot(world=>{world.movingPlatforms[0].endpoint=1;world.movingPlatforms[0].segment=null;world.movingPlatforms[0].revision++;});
            string completed=StateJson.ToJson(f.World.movingPlatforms[0]);
            Require(Near(actor,f.Feet()) && f.World.movingPlatforms[0].segment==null,"Completion snapshot сразу передвинул опору.");
            int tailFrames=(int)Math.Ceiling(.3/realDt);
            for(int i=0;i<tailFrames;i++)
            {
                var previous=actor;f.Advance(realDt);actor=PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,realDt,actorState);
                if(i%3==0) f.Snapshot();
                PropMotion.Step(cargo,f.RoomGeometry,realDt,null,passenger:cargoState);
                Require(f.Frame.Pose(0).Continuous && actor.x>previous.x && actor.x-previous.x<=2.8f*realDt+.00002f &&
                    Near(actor,f.Feet()) && Near(cargo.Position,actor),"Оставшийся .3 s tail дал endpoint jump или пропущенный delta: fps="+fps+", frame="+i);
                Require(f.World.movingPlatforms[0].segment==null && StateJson.ToJson(f.World.movingPlatforms[0])==completed,
                    "Локальная выборка восстановила retired segment в canonical state.");
            }
            Require(Near(actor,new Vector2(66,10.4f)),"Плавный tail не закончил весь оставшийся путь: fps="+fps);
            f.Advance(realDt);var stopped=PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,realDt,actorState);
            PropMotion.Step(cargo,f.RoomGeometry,realDt,null,passenger:cargoState);
            Require(Near(stopped,actor) && Near(cargo.Position,actor),"После local completion повторён оставшийся delta.");
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
            Require(first.Continuous && second.Continuous && Bounds(first.Previous)!=Bounds(first.Current),"Подтверждение известного endpoint потеряло прежнюю опору.");
            Require(first.Sequence==second.Sequence && first.PreviousElapsedSeconds==second.PreviousElapsedSeconds && first.ElapsedSeconds==second.ElapsedSeconds,
                "Две опоры одного кадра получили разные timestamps/sequence.");
            Require(Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feetA,.1f,a),f.Feet(0)) && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feetB,.1f,b),feetB),
                "Подтверждение endpoint потеряло известного пассажира или передвинуло другую опору.");
            feetA=f.Feet(0);f.Advance(.1);
            Require(f.Frame.Pose(0).Continuous && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feetA,.1f,a),f.Feet(0)),"После segment rebase не возобновился текущий carry.");
        }
        private static void ReplacedMovingSegment()
        {
            using var f=new Fixture(3);var feet=f.Feet();var state=State("moving-predecessor");
            PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state);
            f.Snapshot(world=>world.movingPlatforms[0].segment=new MovingPlatformSegment
                {sequence=3,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=1,durationSeconds=6});
            f.Advance(.1);var pose=f.Frame.Pose(0);
            Require(!pose.Continuous && Bounds(pose.Previous)==Bounds(pose.Current) &&
                Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,.1f,state),feet),"Замена движущейся траектории выдала произвольную историю за endpoint departure.");
        }
        private static void LateDepartureContacts()
        {
            using var f=new Fixture(1,false);var feet=f.Feet();var rider=State("late-rider");
            var adjacent=feet+Vector2.right*2;var adjacentState=State("late-adjacent");
            var jumpingState=State("late-jump");
            PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,rider);
            PassengerMotion.Carry(f.RoomGeometry,Hull(),adjacent,1f/60,adjacentState);
            PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,jumpingState);
            f.RoomGeometry.platforms=new[]{new RoomSurface {x=49.8f,y=feet.y,width=.2f,height=2,texture="floor",movementSound="metal"}};
            StartSegment(f,f.Now-.39016828377543,false);f.Advance(1f/60);
            var moved=PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,rider);
            Require(rider.Blocked && rider.SupportId==null && Near(moved,new Vector2(49.4f,feet.y)) && ActorTerrainMotion.IsClear(f.RoomGeometry,Hull(),moved),
                "Поздний departure протащил пассажира сквозь стену.");
            Require(Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),adjacent,1f/60,adjacentState),adjacent) &&
                Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,jumpingState,false),feet) &&
                Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,State("late-new-owner")),feet),
                "Поздний departure подхватил соседа, прыжок или новую owner binding.");
            f.Advance(1f/60);
            Require(Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),moved,1f/60,rider),moved),"Заблокированный пассажир снова протащен той же опорой.");
        }
        private static void PendingDepartureReplacement()
        {
            using var f=new Fixture(10,false);var feet=f.Feet();var state=State("pending-trajectory-replacement");
            PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,state);
            StartSegment(f,10,false);
            f.Snapshot(world=>{world.movingPlatforms[0].endpoint=1;world.movingPlatforms[0].segment=null;world.movingPlatforms[0].revision++;});
            StartSegment(f,16,false);f.Advance(1f/60);
            var pose=f.Frame.Pose(0);
            Require(!pose.Continuous && Bounds(pose.Previous)==Bounds(pose.Current) &&
                Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),feet,1f/60,state),feet),
                "Pending departure другого segment перенёс ненаблюдавшийся endpoint при нескольких snapshots до Step.");
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

        // Native integration calls this with the saved platform definition, route, room and
        // actual player hull. The standalone suite uses the same workshop dimensions above.
        public static void CheckDepartures(MovingPlatformDefinition type,MovingPlatformState source,RoomLayout room,MovementHull hull)
        {
            ObservedLateDeparture(type,source,room,hull);
            foreach(int endpoint in new[]{0,1})
            foreach(bool inPlace in new[]{false,true})
            foreach(double delay in new[]{1d/60,.39016828377543,.8}) LateDeparture(type,source,room,hull,endpoint,inPlace,delay);
            foreach(int fps in new[]{30,60,144})
            foreach(int endpoint in new[]{0,1})
            foreach(bool inPlace in new[]{false,true})
            foreach(float firstDelta in new[]{1f/fps,.11841638f,1f/3})
            {
                ConfirmedDeparture(type,source,room,hull,fps,endpoint,inPlace,firstDelta,0);
                ConfirmedDeparture(type,source,room,hull,fps,endpoint,inPlace,firstDelta,.5);
            }
        }

        private static void ObservedLateDeparture(MovingPlatformDefinition type,MovingPlatformState source,RoomLayout room,MovementHull hull)
        {
            var stationary=StateJson.Copy(source);stationary.endpoint=1;stationary.segment=null;
            using var f=new Fixture(64.85479426541177,false,definition:type,platform:stationary,room:room);
            var offset=new Vector2(.32101440429688f,0);var actor=f.Feet()+offset;var state=State("observed-late-rider");
            var cargo=Cargo(f.Feet());var cargoState=State(cargo.id);
            PassengerMotion.Carry(f.RoomGeometry,hull,actor,1f/60,state);PropMotion.Step(cargo,f.RoomGeometry,1f/60,null,passenger:cargoState);
            StartSegment(f,64.48545922525227,false);f.Now=64.8756275090277;f.Simulation.Step(1f/60);
            var pose=f.Frame.Pose(0);actor=PassengerMotion.Carry(f.RoomGeometry,hull,actor,1f/60,state);
            PropMotion.Step(cargo,f.RoomGeometry,1f/60,null,passenger:cargoState);
            Require(pose.Continuous && Mathf.Abs(pose.Delta.x+1.09246826171875f)<.00002f &&
                Near(actor,f.Feet()+offset) && Near(cargo.Position,f.Feet()) && !state.Blocked && !cargoState.Blocked &&
                Near(PassengerMotion.Carry(f.RoomGeometry,hull,actor,1f/60,state),actor),
                "Точные часы/offset позднего frame3583 не перенесли actor/cargo однократно.");
        }
        private static void ConfirmedDeparture(MovingPlatformDefinition type,MovingPlatformState source,RoomLayout room,MovementHull hull,
            int fps,int endpoint,bool inPlace,float firstDelta,double startFraction)
        {
            var stationary=StateJson.Copy(source);stationary.endpoint=endpoint;stationary.segment=null;
            using var f=new Fixture(53.75664048,false,definition:type,platform:stationary,room:room);
            Require(f.Frame.Count==1,"Departure fixture требует ровно одну настоящую платформу.");
            // Captured ordinary reverse stood .32378 m to the right of endpoint 1.
            // Both directions use that same relative position; no passenger correction.
            var offset=new Vector2(.32378f,0);var actor=f.Feet()+offset;var actorState=State("departure-rider");
            var cargo=Cargo(f.Feet());var cargoState=State(cargo.id);
            string contents=StateJson.ToJson(cargo.items),aliases=StateJson.ToJson(cargo.itemAliases);
            Require(ActorTerrainMotion.IsClear(f.RoomGeometry,hull,actor) && ActorTerrainMotion.Supported(f.RoomGeometry,hull,actor),
                "Начальное место пассажира не свободно/не поддержано: endpoint="+endpoint);
            PassengerMotion.Carry(f.RoomGeometry,hull,actor,firstDelta,actorState);
            PropMotion.Step(cargo,f.RoomGeometry,firstDelta,null,passenger:cargoState);
            var previous=Bounds(f.Frame.Pose(0).Current);double start=f.Now+firstDelta*startFraction;
            f.Now+=firstDelta; // Same elapsed-level interval advanced before confirmation/Step.
            StartSegment(f,start,inPlace);f.Snapshot(); // Duplicate confirmation must keep the published baseline.
            string canonical=StateJson.ToJson(f.World);f.Simulation.Step(firstDelta);
            var published=f.Frame.Pose(0);long sequence=f.Frame.Sequence;
            string context=$"fps={fps}, endpoint={endpoint}, inplace={inPlace}, dt={firstDelta}, fraction={startFraction}";
            Require(published.Continuous && Bounds(published.Previous).Equals(previous) && published.Delta.sqrMagnitude>0,
                "Подтверждённый departure потерял первый interval: "+context);
            var expected=new MovingPlatformMotion(type).Evaluate(f.World.movingPlatforms[0],f.Now);
            var expectedFeet=new Vector2(expected.SupportBounds.center.x,expected.SupportBounds.yMax);
            actor=PassengerMotion.Carry(f.RoomGeometry,hull,actor,firstDelta,actorState);
            Require(StateJson.ToJson(f.World)==canonical,"Step/carry изменил canonical state: "+context);
            f.World.Find("rider").revision++;canonical=StateJson.ToJson(f.World);f.Simulation.Reconcile(); // Actor-only Changed must keep A/cargo interval.
            f.Snapshot();
            Require(f.Frame.Sequence==sequence && Bounds(f.Frame.Pose(0).Previous).Equals(previous),"Reconcile удалил текущий departure interval: "+context);
            PropMotion.Step(cargo,f.RoomGeometry,firstDelta,null,passenger:cargoState);
            Require(!actorState.Blocked && !cargoState.Blocked && Near(actor,expectedFeet+offset) && Near(cargo.Position,expectedFeet) &&
                Near(actorState.LastAppliedDelta,published.Delta) && Near(cargoState.LastAppliedDelta,published.Delta),
                "Первый delta не перенёс уже стоящих actor/cargo: "+context+", actor="+actor+", support="+expectedFeet);
            Require(Near(PassengerMotion.Carry(f.RoomGeometry,hull,actor,firstDelta,actorState),actor),"Повтор одного кадра удвоил carry: "+context);
            Require(StateJson.ToJson(f.World)==canonical && StateJson.ToJson(cargo.items)==contents && StateJson.ToJson(cargo.itemAliases)==aliases &&
                Mathf.Abs(cargo.vx)<.0001f && Mathf.Abs(cargo.vy)<.0001f,"Carry изменил canonical/инвентарь/relative velocity: "+context);
            float nextDelta=1f/fps;f.Advance(nextDelta);
            actor=PassengerMotion.Carry(f.RoomGeometry,hull,actor,nextDelta,actorState);
            PropMotion.Step(cargo,f.RoomGeometry,nextDelta,null,passenger:cargoState);
            Require(f.Frame.Pose(0).Continuous && Near(actor,f.Feet()+offset) && Near(cargo.Position,f.Feet()),"Следующий interval потерял relative position: "+context);
        }

        private static void LateDeparture(MovingPlatformDefinition type,MovingPlatformState source,RoomLayout room,MovementHull hull,int endpoint,bool inPlace,double delay)
        {
            var stationary=StateJson.Copy(source);stationary.endpoint=endpoint;stationary.segment=null;
            using var f=new Fixture(53.75664048,false,definition:type,platform:stationary,room:room);
            var actor=f.Feet()+new Vector2(.32378f,0);var state=State("late-departure");
            var cargo=Cargo(f.Feet());var cargoState=State(cargo.id);string contents=StateJson.ToJson(cargo.items),aliases=StateJson.ToJson(cargo.itemAliases);
            PassengerMotion.Carry(f.RoomGeometry,hull,actor,1f/60,state);
            PropMotion.Step(cargo,f.RoomGeometry,1f/60,null,passenger:cargoState);
            StartSegment(f,f.Now-delay,inPlace);f.Snapshot();string canonical=StateJson.ToJson(f.World);
            f.Simulation.Step(0);
            Require(Near(f.Feet()+new Vector2(.32378f,0),actor) && Near(PassengerMotion.Carry(f.RoomGeometry,hull,actor,0,state),actor),
                "Нулевой шаг/пауза потребил подтверждённый departure correction.");
            PropMotion.Step(cargo,f.RoomGeometry,0,null,passenger:cargoState);
            f.Advance(1f/60);
            var pose=f.Frame.Pose(0);
            actor=PassengerMotion.Carry(f.RoomGeometry,hull,actor,1f/60,state);
            Require(StateJson.ToJson(f.World)==canonical,"Поздний correction изменил canonical state до snapshot.");
            f.Snapshot();PropMotion.Step(cargo,f.RoomGeometry,1f/60,null,passenger:cargoState);
            // The explicit test round-trip normalizes decimal doubles; it is not
            // a simulation write. Check immutability on each side of that boundary.
            canonical=StateJson.ToJson(f.World);
            Require(pose.Continuous && Near(actor,f.Feet()+new Vector2(.32378f,0)) && Near(cargo.Position,f.Feet()) &&
                !state.Blocked && !cargoState.Blocked && Near(state.LastAppliedDelta,pose.Delta) && Near(cargoState.LastAppliedDelta,pose.Delta),
                "Позднее подтверждение известного endpoint потеряло actor/cargo: endpoint="+endpoint+", inplace="+inPlace+", delay="+delay);
            Require(Near(PassengerMotion.Carry(f.RoomGeometry,hull,actor,1f/60,state),actor),"Поздний correction повторился.");
            Require(StateJson.ToJson(f.World)==canonical,"Поздний correction изменил canonical state после snapshot.");
            Require(StateJson.ToJson(cargo.items)==contents && StateJson.ToJson(cargo.itemAliases)==aliases,"Поздний correction изменил cargo inventory/aliases.");
        }

        private static void StartSegment(Fixture f,double start,bool inPlace)
        {
            void Apply(WorldState world)
            {
                var platform=world.movingPlatforms[0];platform.revision++;
                platform.segment=new MovingPlatformSegment {sequence=platform.revision,fromEndpoint=platform.endpoint,toEndpoint=1-platform.endpoint,
                    startElapsedSeconds=start,durationSeconds=f.Definition.durationSeconds};world.revision++;
            }
            if(inPlace) {Apply(f.World);f.Geometry.Reconcile(f.World);f.Simulation.Reconcile();}
            else f.Snapshot(Apply);
        }
        private static void InPlaceCompletion()
        {
            using var f=new Fixture(5.7);var actor=f.Feet();var state=State("inplace-tail");
            PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,.1f,state);
            var platform=f.World.movingPlatforms[0];platform.endpoint=1;platform.segment=null;platform.revision++;f.World.revision++;
            f.Simulation.Reconcile();string canonical=StateJson.ToJson(f.World.movingPlatforms[0]);
            for(int i=0;i<3;i++)
            {
                f.Advance(.1);actor=PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,.1f,state);
                Require(f.Frame.Pose(0).Continuous && Near(actor,f.Feet()) && !state.Blocked,"In-place completion потерял незавершённый local tail.");
                Require(StateJson.ToJson(platform)==canonical,"Sampling вернул retired segment в canonical state.");
            }
            Require(Near(actor,new Vector2(66,10.4f)),"In-place completion не достиг endpoint.");
        }
        private static void RouteAndDefinitionRebind()
        {
            foreach(bool definition in new[]{false,true})
            {
                using var f=new Fixture(0,false);var actor=f.Feet();var state=State("route-rebind");
                PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,.1f,state);
                if(definition) {f.Definition.version++;f.World.movingPlatforms[0].definitionVersion=f.Definition.version;}
                else f.World.movingPlatforms[0].endX+=1;
                StartSegment(f,0,true);f.Advance(.1);var pose=f.Frame.Pose(0);
                Require(!pose.Continuous && Bounds(pose.Previous).Equals(Bounds(pose.Current)) &&
                    Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),actor,.1f,state),actor),
                    "In-place смена route/definition проиграла прошлый delta: definition="+definition);
            }
        }
        private static void PassengerIdentityRebind()
        {
            using var f=new Fixture(0,false);var registry=new PassengerMotionRegistry();
            var actor=f.World.Find("rider");actor.generation=2;actor.Position=f.Feet();
            var oldState=registry.ForActor(actor);PassengerMotion.Carry(f.RoomGeometry,Hull(),actor.Position,.1f,oldState);
            StartSegment(f,0,false);f.Advance(.1);
            var incoming=StateJson.Copy(actor);incoming.owner="connection:2";incoming.generation++;
            var newState=registry.ForActor(incoming);
            Require(!ReferenceEquals(oldState,newState) && Near(PassengerMotion.Carry(f.RoomGeometry,Hull(),incoming.Position,.1f,newState),incoming.Position),
                "Новый owner/generation проиграл первый наблюдаемый interval.");
            using var restored=new Fixture(.1,definition:f.Definition,platform:f.World.movingPlatforms[0]);
            var restoredState=State("restored-rider");var restoredFeet=restored.Feet();
            Require(Near(PassengerMotion.Carry(restored.RoomGeometry,Hull(),restoredFeet,.1f,restoredState),restoredFeet),
                "Reload проиграл историческое движение.");
        }

        private static Rect Bounds(RoomSurface surface)=>new(surface.x,surface.y,surface.width,surface.height);
        private static bool Near(Vector2 a,Vector2 b)=>(a-b).sqrMagnitude<.000001f;
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка адаптера платформы: "+reason);}
    }
}
