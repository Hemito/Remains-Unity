using System;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Runtime;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Detached diagnostic actors on the actual saved connected terrain. No native
    // adventure/content acceptance is inferred from this controller regression.
    public static class FactoryContinuationStairMotionChecks
    {
        public static void Run()
        {
            Anatomy.Load();SlopePoseCatalog.Load();FootSupportCatalog.Load();
            var source=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId);
            string sourceBefore=StateJson.ToJson(source.CreateLayout(false));
            string doorsBefore=StateJson.ToJson(RoomDoorContent.Read(source,"factory:0"));
            int cases=0;
            foreach(int fps in new[]{30,60}) foreach(bool missedStop in new[]{false,true})
                foreach(int spike in new[]{0,128,159})
                {Traverse(source,fps,spike,missedStop);cases++;}
            PrepareFiringCatalogs();
            int firingCases=FiringCases(source);
            Require(sourceBefore==StateJson.ToJson(source.CreateLayout(false)) &&
                doorsBefore==StateJson.ToJson(RoomDoorContent.Read(source,"factory:0")),
                "Проверка лестничного прохода изменила исходную геометрию или авторские двери.");
            Debug.Log($"Обычные наклонные ступени комнаты 27: PASS — {cases} проходов реального MovementController туда/обратно на сохранённой геометрии, 30/60 Гц и кадры 128/159 мс, пропущенный moving sample/остановка, нулевое время без ложного witness, четыре реальные опоры в каждом направлении. ИИ, урон и сетевой маршрут этим isolated check не проверяются.");
            Debug.Log($"Позиция стрелка комнаты 27: PASS — {firingCases} обычных переходов 163.12→163.7→163.2 при неподвижном союзнике 163.14, реальная опора тела/пистолет/дульный коридор до закрытой двери, 30/60 Гц и кадры 128/159/333 мс; пересекающий коридор союзник запрещает выстрел. Это detached controller fixture, не нативный парный маршрут.");
        }

        // A narrow fresh-Editor entry point for native diagnosis without running
        // the whole contract/build pipeline or installing/changing any asset.
        public static void RunFiringParking()
        {
            PrepareFiringCatalogs();
            var source=RoomCatalogAsset.Load().Resolve(LegacyFactoryFixture.DefinitionId);
            string geometryBefore=StateJson.ToJson(source.CreateLayout(false));
            string doorsBefore=StateJson.ToJson(RoomDoorContent.Read(source,"factory:0"));
            int cases=FiringCases(source);
            Require(geometryBefore==StateJson.ToJson(source.CreateLayout(false)) &&
                doorsBefore==StateJson.ToJson(RoomDoorContent.Read(source,"factory:0")),
                "Диагностика парковки изменила сохранённую геометрию или авторские двери.");
            Debug.Log($"Отдельная диагностика позиции стрелка: PASS — {cases} actual MovementController/BodySupport/WeaponPose/ShotSafety случаев; исходные данные не изменены.");
        }
        private static void PrepareFiringCatalogs()
        {
            Anatomy.Load();PonyAnimationData.Load();SlopePoseCatalog.Load();FootSupportCatalog.Load();
            StandingEdgePoseCatalog.Load();ActorTypes.Load();WeaponRigs.Load();
        }
        private static int FiringCases(RoomAuthoring source)
        {
            int cases=0;
            foreach(int fps in new[]{30,60}) foreach(int spike in new[]{0,128,159,333})
            {FiringParking(source,fps,spike);cases++;}
            return cases;
        }

        private static void FiringParking(RoomAuthoring source,int fps,int spike)
        {
            var room=source.CreateLayout(false);
            var doors=RoomDoorContent.Read(source,"factory:0").doors;
            room.platforms=DoorGeometry.Compose(room.platforms,doors,"factory:0");
            var door=doors.Single(d=>d.id==FactoryContinuationRouteMotion.SeptumId);
            Require(door.Blocks && !door.Destroyed && door.integrity.health==30,
                "Позиция стрелка требует настоящую закрытую исходную перегородку HP30.");
            var host=new ActorState {id="firing-parking-host",owner="firing-parking-owner",player=true,connected=true,
                generation=1,lifecycleSequence=1,level="factory:0",anatomy="pony",x=163.12f,y=4,weapon="pistol"};
            var guest=new ActorState {id="firing-parking-guest",owner="firing-parking-guest-owner",player=true,connected=true,
                generation=1,lifecycleSequence=1,level="factory:0",anatomy="pony",x=163.14f,y=4};
            var world=new WorldState {actors={host,guest},doors=doors.ToList()};
            ActorBodySupport.Bind(host,room);ActorBodySupport.Bind(guest,room);
            // Editor-only delegates exercise the actual internal probe helpers;
            // the input and friendly-fire corridor rules are not copied here.
            var assembly=typeof(FactoryContinuationRouteMotion).Assembly;
            var move=InternalDelegate<Func<ActorState,float,PlayerCommand>>(assembly,"FactoryRouteMovement","Move");
            var clear=InternalDelegate<Func<WorldState,ActorState,RoomLayout,Vector2,string,bool>>(assembly,"FactoryRouteShotSafety","Clear");
            string guestBefore=StateJson.ToJson(guest);
            string label=$"fps={fps}, spikeMs={spike}";
            try
            {
                var controller=new MovementController(room);
                MoveParking(controller,host,guest,room,move,163.7f,fps,spike,label);
                var body=ActorBodySupport.Sample(host);
                double surfaceTop=(double)body.SurfaceBounds.y+body.SurfaceBounds.height;
                string supportDiagnostic=FormattableString.Invariant($"{label}, actor={host.id}, anchor=({host.x:R},{host.y:R}), velocity=({host.vx:R},{host.vy:R}), facing={host.facing}, pose={host.pose}, poseTime={host.poseTime:R}, bodyPose={body.Pose}/{body.Frame}, outcome={body.Outcome}, bound={body.IsBound}, profile={body.HasFootProfile}, grounded={body.Grounded}, contact={body.HasContact}, surface=[{body.SurfaceBounds.x:R},{body.SurfaceBounds.y:R},{body.SurfaceBounds.width:R},{body.SurfaceBounds.height:R}], surfaceTopDouble={surfaceTop:R}, surfaceTopDelta={surfaceTop-4:R}, body=({body.BodyPosition.x:R},{body.BodyPosition.y:R}), foot=({body.ContactPoint.x:R},{body.ContactPoint.y:R}), physicalSupported={ActorTerrainMotion.Supported(room,Anatomy.Movement(host),host.Position)}.");
                Debug.Log("Диагностика опоры позиции стрелка: "+supportDiagnostic);
                Require(body.IsBound && body.Grounded && body.HasContact && RightBankSurface(body.SurfaceBounds),
                    "Стрелок не имеет настоящей sampled опоры правого берега: "+supportDiagnostic);
                var shiftedSurface=body.SurfaceBounds;shiftedSurface.y+=.01f;
                Require(!RightBankSurface(shiftedSurface),
                    "Проверка опоры приняла поверхность со сдвигом высоты на 1 см: "+supportDiagnostic);
                // Both existing lift states use the real authored pistol socket.
                foreach(float lift in new[]{0f,WeaponRigs.RaiseHeight})
                {
                    host.weaponLift=lift;WeaponPose.Aim(host,door.Bounds.center,room);
                    var muzzle=WeaponPose.Muzzle(host);
                    Require(muzzle.x<door.x && muzzle.x>InventorySource.BodyBounds(guest).xMax+.2f &&
                        clear(world,host,room,door.Bounds.center,door.id),
                        $"Поддержанный стрелок не получил безопасное настоящее дуло: {label}, lift={lift}, muzzle={muzzle}, guest={guest.Position}.");
                    var overlap=new ActorState {id="firing-parking-overlap",player=true,connected=true,level="factory:0",
                        anatomy="pony",x=muzzle.x,y=muzzle.y-Anatomy.Movement(guest).height*.5f};
                    world.actors.Add(overlap);
                    Require(!clear(world,host,room,door.Bounds.center,door.id),
                        "Actual ShotSafety разрешил дуло внутри тела союзника: "+label);
                    world.actors.Remove(overlap);
                }
                MoveParking(controller,host,guest,room,move,163.2f,fps,spike,label);
                Require(StateJson.ToJson(guest)==guestBefore && guest.x==163.14f && guest.y==4,
                    "Проверка парковки изменила неподвижного союзника: "+label);
            }
            finally {ActorBodySupport.Unbind(host);ActorBodySupport.Unbind(guest);}
        }
        private static void MoveParking(MovementController controller,ActorState host,ActorState guest,RoomLayout room,
            Func<ActorState,float,PlayerCommand> move,float target,int fps,int spike,string label)
        {
            float seconds=0;int steps=0;
            while(!ParkingRest(host,target) && seconds<5)
            {
                float dt=spike>0 && steps%17==0?spike/1000f:1f/fps;
                string before=StateJson.ToJson(host);
                var command=move(host,target);
                Require(StateJson.ToJson(host)==before && !command.fire && !command.jump && command.movement.y==0,
                    "Парковка должна выдавать только обычный горизонтальный intent: "+label);
                ActorPose.Advance(host,dt);controller.Step(host,command.movement,false,dt);
                var hull=Anatomy.Movement(host);
                Require(ActorTerrainMotion.IsClear(room,hull,host.Position) &&
                    ActorTerrainMotion.Supported(room,hull,host.Position) && !host.climbing &&
                    Mathf.Abs(host.y-4)<.0002f && Mathf.Abs(host.vy)<.001f &&
                    ActorTerrainMotion.Supported(room,Anatomy.Movement(guest),guest.Position),
                    $"Обычная парковка потеряла опору/попала в дверь: {label}, target={target}, dt={dt}, pos={host.Position}, velocity={host.Velocity}.");
                seconds+=dt;steps++;
            }
            Require(ParkingRest(host,target),$"Обычное торможение не завершило парковку: {label}, target={target}, pos={host.Position}, velocity={host.Velocity}.");
        }
        private static bool ParkingRest(ActorState actor,float target)=>Mathf.Abs(actor.x-target)<.08f &&
            Mathf.Abs(actor.y-4)<.0002f && Mathf.Abs(actor.vx)<.1f && Mathf.Abs(actor.vy)<.001f && !actor.climbing;
        private static bool RightBankSurface(Rect surface)=>Math.Abs((double)surface.y+surface.height-4)<.0002 &&
            Math.Abs((double)surface.x-162.4)<.001 && Math.Abs((double)surface.width-14.4)<.001 &&
            Math.Abs((double)surface.height-.8)<.001;
        private static T InternalDelegate<T>(Assembly assembly,string type,string method) where T:Delegate
        {
            var owner=assembly.GetType("Remains.Runtime."+type,true);
            var member=owner.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)??
                throw new InvalidOperationException($"Нет действительного route helper {type}.{method} для Editor проверки.");
            return (T)member.CreateDelegate(typeof(T));
        }

        private static void Traverse(RoomAuthoring source,int fps,int spike,bool missedStop)
        {
            var room=source.CreateLayout(false);var authored=room.platforms;
            var doors=RoomDoorContent.Read(source,"factory:0").doors;
            room.platforms=DoorGeometry.Compose(authored,doors,"factory:0");
            var actor=new ActorState {id="stair-motion-check",owner="stair-motion-owner",player=true,connected=true,
                generation=1,lifecycleSequence=1,level="factory:0",anatomy="pony",x=125.38f,y=.8f};
            ActorBodySupport.Bind(actor,room);
            try
            {
                var controller=new MovementController(room);
                var route=new FactoryContinuationRouteMotion(room,actor);
                bool upStop=false,downStop=false,zeroChecked=false,firstSpike=false;
                int stairSteps=0,steps=0;float seconds=0;string label=$"fps={fps}, spikeMs={spike}, missedStop={missedStop}";
                while(!route.Complete && seconds<90)
                {
                    bool up=route.Current==FactoryContinuationRouteMotion.Phase.Stairs;
                    bool down=route.Current==FactoryContinuationRouteMotion.Phase.ReturnStairs;
                    if(missedStop && (up && !upStop || down && !downStop))
                    {
                        // Replay the missed-observer condition as an explicit detached
                        // fixture. The route receives no historical moving samples.
                        // Up is the captured native 139.39/1.49/zero-velocity case.
                        // Down uses the same saved surface's exact physical Top.
                        float x=up?139.39f:141.79f;
                        var surface=authored.Single(s=>s.oneWay && s.slopeDirection==1 &&
                            Mathf.Abs(s.x-(up?139.2f:141.6f))<.001f);
                        actor.x=x;actor.y=up?1.49f:RoomSupport.Top(surface,x,Anatomy.Movement(actor).width);
                        actor.vx=0;actor.vy=0;actor.climbing=false;actor.pose="idle";actor.poseTime=0;
                        Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),actor.Position) &&
                            ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),
                            "Recorded остановка не принадлежит сохранённой свободной опоре: "+label);
                        route.Tick(actor,0);
                        if(up) upStop=true;else downStop=true;
                        int before=route.Witnesses.Count;
                        var command=Intent(route,actor);
                        Require(command.movement.x==(up?1:-1),"После пропущенного sample снова выбран вечный zero input: "+label);
                        if(!zeroChecked)
                        {
                            for(int i=0;i<5;i++)
                            {
                                command=Intent(route,actor);controller.Step(actor,command.movement,command.jump,0);
                                Observe(route,actor,0);
                                Require(route.Witnesses.Count==before,"Нулевой dt создал moving witness: "+label);
                            }
                            zeroChecked=true;
                        }
                    }
                    float dt=1f/fps;
                    if(spike>0 && (up || down))
                    {
                        // The first positive nudge sees a real long step; later spikes
                        // periodically recur while ordinary baseline steps continue.
                        if(missedStop && !firstSpike || stairSteps++%17==8)
                        {dt=spike/1000f;firstSpike=true;}
                    }
                    var input=Intent(route,actor);ActorPose.Advance(actor,dt);
                    controller.Step(actor,input.movement,input.jump,dt,input.sprint,input.jumpHeld);
                    Observe(route,actor,dt);seconds+=dt;steps++;
                    Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),actor.Position),
                        $"Обычный ввод попал в solid terrain: {label}, phase={route.Current}, pos={actor.Position}.");
                    if(route.AtDestruction)
                    {
                        // Actual domain operation on detached authored state, followed
                        // by the same explicit composed geometry publication.
                        var door=doors.Single(d=>d.id==FactoryContinuationRouteMotion.SeptumId);
                        Require(DoorDamageOperations.Apply(door,"stair-check-destruction",30,out _)==DoorChange.Changed,
                            "Detached преграда не разрушена доменным действием.");
                        room.platforms=DoorGeometry.Compose(authored,doors,"factory:0");ActorBodySupport.InvalidateRoom(room);
                        route.ContinueAfterDestruction(actor);
                    }
                    if(route.AtCup) route.BeginReturn(actor);
                }
                Require(route.Complete && route.AscendingSlopes && route.DescendingSlopes &&
                    route.MiddleClimbUp && route.MiddleClimbDown && route.RightLanding && route.LeftLanding &&
                    (!missedStop || upStop && downStop && zeroChecked),
                    $"Реальный контроллер не закончил stair regression: {label}, phase={route.Current}, pos={actor.Position}, steps={steps}.");
                var observations=route.Witnesses.Where(w=>w.kind.StartsWith("slope-",StringComparison.Ordinal)).ToArray();
                Require(observations.Length==8 && observations.Select(w=>w.kind).Distinct().Count()==8,
                    "Наклонные witnesses не содержат восемь отдельных настоящих контактов: "+label);
                foreach(var witness in observations)
                    Require(witness.supported && witness.bodyBound && !witness.climbing &&
                        Mathf.Abs(witness.vy)<.001f && witness.surfaceWidth==.8f && witness.surfaceHeight==.8f &&
                        witness.slopeDirection==1 && witness.x>=witness.surfaceX && witness.x<witness.surfaceX+witness.surfaceWidth &&
                        (witness.kind.StartsWith("slope-up-",StringComparison.Ordinal)?witness.vx>0:witness.vx<0),
                        "Фальшивый/неподдержанный moving slope witness: "+label+"/"+witness.kind);
            }
            finally {ActorBodySupport.Unbind(actor);}
        }

        private static PlayerCommand Intent(FactoryContinuationRouteMotion route,ActorState actor)
        {
            string before=StateJson.ToJson(actor);var command=route.Command(actor);
            Require(StateJson.ToJson(actor)==before,"Route.Command изменил актёра вместо обычного ввода.");return command;
        }
        private static void Observe(FactoryContinuationRouteMotion route,ActorState actor,float dt)
        {
            string before=StateJson.ToJson(actor);route.Tick(actor,dt);
            Require(StateJson.ToJson(actor)==before,"Route.Tick изменил актёра вместо наблюдения.");
        }
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException(reason);}
    }
}
