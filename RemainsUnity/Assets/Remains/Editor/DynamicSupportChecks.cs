using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class DynamicSupportChecks
    {
        public static void Run()=>Run(true,true);
        public static void Run(bool log,bool actors)
        {
            SerializationAndConsumers();Passengers();ReleaseAndRecovery();ObstacleAndPublication();
            if(actors) ActorConsumers();
            if(log) Debug.Log("Динамическая опора: PASS — transient JSON, общая геометрия, carry 30/60/144, финальный delta, once/frame, отдельные пассажиры, jump/drop/held, препятствие и смена owner/generation. Сеть/runtime не проверены.");
        }
        private static RoomLayout Room()=>new() {id="dynamic-support-check",left=0,right=100,bottom=0,top=30,movementSound="normal",
            ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>(),platforms=new[]{new RoomSurface {x=0,y=-1,width=100,height=1,texture="floor",movementSound="metal"}}};
        private static DynamicSurfaceFrame Frame(RoomLayout room,bool oneWay=false)
        {
            var frame=room.dynamicSurfaces=new DynamicSurfaceFrame();
            frame.ReconcileIds(new[]{new DynamicSurfaceSpec("platform",oneWay,false,"floor","metal")});
            frame.SetPose(0,Bounds(49.2f),Bounds(49.2f),0,0,1,false);return frame;
        }
        private static Rect Bounds(float x)=>new(x-1.2f,9.62f,2.4f,.78f);
        private static MovementHull Hull()=>new() {width=.8f,height=1.4f};
        private static PassengerMotionState State(string id="actor")=>new(id,"connection:1",2,"factory:0");
        private static void SerializationAndConsumers()
        {
            var room=Room();string before=StateJson.ToJson(room);var frame=Frame(room);var feet=new Vector2(49.2f,10.4f);var hull=Hull();
            Check(StateJson.ToJson(room)==before && StateJson.Copy(room).dynamicSurfaces==null,"Физический кадр попал в сохранение/hash или JSON copy.");
            Check(ActorTerrainMotion.Supported(room,hull,feet) && RoomSupport.IsSupported(room,feet),"Опора отсутствует у потребителей grounded.");
            Check(!ActorTerrainMotion.CanDrop(room,hull,feet),"Сплошная platform2 разрешила drop.");
            Check(SurfaceContactSound.Resolve(room,hull,feet)=="metal","Шаг использует authored floor вместо новой опоры.");
            Check(ShotObstacles.FirstContact(room,new Vector2(45,10),new Vector2(53,10)).HasValue && !RoomVisibility.Clear(room,new Vector2(45,10),new Vector2(53,10)),"Solid платформа отсутствует у shots/visibility.");
            Check(Near(ActorTerrainMotion.Move(room,hull,new Vector2(49.2f,12),Vector2.down*5,false),feet),"Падение пролетело через динамический верх.");
            Check(ActorTerrainMotion.Move(room,hull,new Vector2(49.2f,7),Vector2.up*5,false).y<9,"Подъём прошёл через solid underside.");
            Check(!ActorTerrainMotion.IsClear(room,hull,new Vector2(49.2f,10)),"Solid overlap объявлен свободным.");
            var oneWayRoom=Room();Frame(oneWayRoom,true);
            Check(ActorTerrainMotion.CanDrop(oneWayRoom,hull,feet) && ActorTerrainMotion.Move(oneWayRoom,hull,feet,Vector2.down,false,feet.y).y<feet.y,"Явная oneWay опора потеряла drop.");
        }
        private static void Passengers()
        {
            foreach(int fps in new[]{30,60,144})
            {
                var room=Room();var frame=Frame(room);var actor=new Vector2(49.2f,10.4f);var passenger=State();
                var cargo=new ContainerState {id="cargo",owner="connection:1",level="factory:0",generation=2,mass=5.2488f,x=49.2f,y=10.4f,width=.72f,height=.54f,anchorHeight=0};
                cargo.items.Add(new ItemStack {id="ammo",definition="12p10",count=12});cargo.itemAliases.Add("former","ammo");
                string inventory=StateJson.ToJson(cargo.items),aliases=StateJson.ToJson(cargo.itemAliases);var propPassenger=State("cargo");
                PassengerMotion.Carry(room,Hull(),actor,1f/fps,passenger);PropMotion.Step(cargo,room,1f/fps,null,passenger:propPassenger);
                var type=new MovingPlatformDefinition {id="platform2",version=1,width=2.4f,height=.78f,graphicWidth=2.4f,graphicHeight=.7f,durationSeconds=6};
                var mechanism=new MovingPlatformState {id="factory:0:check",definition=type.id,definitionVersion=1,level="factory:0",roomId="check",groupId="d2",revision=1,
                    startX=49.2f,startY=9.62f,endX=66,endY=9.62f,segment=new MovingPlatformSegment {sequence=1,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=0,durationSeconds=6}};
                var sampler=new MovingPlatformMotion(type);string mechanismBefore=StateJson.ToJson(mechanism);
                for(int i=1;i<=fps*6;i++)
                {
                    double previous=(i-1d)/fps,elapsed=(double)i/fps;
                    frame.SetPose("platform",sampler.Evaluate(mechanism,previous).SupportBounds,sampler.Evaluate(mechanism,elapsed).SupportBounds,previous,elapsed,i+1,true);
                    actor=PassengerMotion.Carry(room,Hull(),actor,1f/fps,passenger);
                    var repeated=PassengerMotion.Carry(room,Hull(),actor,1f/fps,passenger);Check(Near(actor,repeated),"Carry повторился в одном кадре: FPS="+fps);
                    PropMotion.Step(cargo,room,1f/fps,null,passenger:propPassenger);
                }
                Check(Near(actor,new Vector2(66,10.4f)) && Near(cargo.Position,actor),"Разные пассажиры/финальный completed delta потеряны: FPS="+fps);
                Check(Mathf.Abs(cargo.vx)<.0001f && Mathf.Abs(cargo.vy)<.0001f && StateJson.ToJson(cargo.items)==inventory && StateJson.ToJson(cargo.itemAliases)==aliases,"Carry создал feedback скорости или изменил вещи.");
                Check(StateJson.ToJson(mechanism)==mechanismBefore,"Пассажир изменил общий mechanism state.");
            }
        }
        private static void ReleaseAndRecovery()
        {
            foreach(bool enabled in new[]{true,false})
            {
                var room=Room();var frame=Frame(room);var passenger=State();var feet=new Vector2(enabled?53:49.2f,10.4f);
                PassengerMotion.Carry(room,Hull(),feet,.1f,passenger);
                frame.SetPose(0,Bounds(49.2f),Bounds(49.48f),0,.1,2,true);
                Check(Near(PassengerMotion.Carry(room,Hull(),feet,.1f,passenger,enabled),feet),enabled?"Захвачен сосед без контакта.":"Jump/drop/held не отпустил carry.");
            }
            var lateRoom=Room();var late=Frame(lateRoom);var state=State();var start=new Vector2(49.2f,10.4f);
            PassengerMotion.Carry(lateRoom,Hull(),start,.1f,state);
            late.SetPose(0,Bounds(49.2f),Bounds(66),0,3,2,false);
            Check(Near(PassengerMotion.Carry(lateRoom,Hull(),start,.1f,state),start),"Late snapshot телепортировал пассажира.");
            late.SetPose(0,Bounds(66),Bounds(66.1f),3,3.1,3,true);
            Check(Near(PassengerMotion.Carry(lateRoom,Hull(),start,.1f,state),start),"После snapshot захвачен удалённый пассажир.");
            var clockRoom=Room();var clock=Frame(clockRoom);var clockState=State();PassengerMotion.Carry(clockRoom,Hull(),start,.1f,clockState);
            clock.SetPose(0,Bounds(49.2f),Bounds(57.6f),0,3,2,true);
            Check(Near(PassengerMotion.Carry(clockRoom,Hull(),start,.1f,clockState),start),"Исторический интервал перенесён за один короткий шаг.");
            var pausedRoom=Room();var paused=Frame(pausedRoom);var pausedState=State();PassengerMotion.Carry(pausedRoom,Hull(),start,.1f,pausedState);
            paused.SetPose(0,Bounds(49.2f),Bounds(49.2f),0,0,2,true);
            Check(Near(PassengerMotion.Carry(pausedRoom,Hull(),start,0,pausedState),start),"Пауза изменила пассажира.");
            paused.SetPose(0,Bounds(49.2f),Bounds(49.48f),0,.1,3,true);
            Check(Near(PassengerMotion.Carry(pausedRoom,Hull(),start,.1f,pausedState),new Vector2(49.48f,10.4f)),"После паузы потерян первый текущий delta.");
            var registry=new PassengerMotionRegistry();var prop=new PropState {id="p",owner="connection:1",generation=2,level="factory:0"};var previous=registry.ForProp(prop);
            Check(ReferenceEquals(previous,registry.ForProp(StateJson.Copy(prop))),"Замена snapshot-object сбросила состояние того же владельца.");
            prop.owner="connection:2";Check(!ReferenceEquals(previous,registry.ForProp(prop)),"Новый owner сохранил старый consumed state.");
            previous=registry.ForProp(prop);prop.generation++;Check(!ReferenceEquals(previous,registry.ForProp(prop)),"Новая generation сохранила consumed state.");
            var rebound=registry.ForProp(prop);
            Check(Near(PassengerMotion.Carry(pausedRoom,Hull(),start,.1f,rebound),start),"Новый владелец воспроизвёл delta до первого собственного наблюдения.");
            prop.level="factory:6";Check(registry.ForProp(prop).Level==prop.level,"Смешаны уровни пассажира.");registry.Reconcile(null,null);
            var heldRoom=Room();var heldFrame=Frame(heldRoom);var held=new PropState {id="held",owner="connection:1",held=true,x=49.2f,y=10.4f,anchorHeight=0};
            PropMotion.Step(held,heldRoom,.1f,held.Position);Check(Mathf.Abs(held.x-49.2f)<.0001f,"Held prop перенёс carry или потребовал free registry.");
            Reject(()=>PassengerMotion.Carry(heldRoom,Hull(),start,.1f,null),"Отсутствующий passenger cache скрыт.");
        }
        private static void ObstacleAndPublication()
        {
            var room=Room();room.platforms=new[]{new RoomSurface {x=52.5f,y=9,width=.5f,height=10,texture="floor"}};
            var frame=Frame(room);var passenger=State();var start=new Vector2(49.2f,10.4f);PassengerMotion.Carry(room,Hull(),start,2,passenger);
            frame.SetPose(0,Bounds(49.2f),Bounds(54.8f),0,2,2,true);
            var result=PassengerMotion.Carry(room,Hull(),start,2,passenger);
            Check(Mathf.Abs(result.x-52.1f)<.001f && passenger.Blocked && passenger.SupportId==null && ActorTerrainMotion.IsClear(room,Hull(),result),"Большой delta пронёс пассажира через стену.");
            var publication=new DynamicSurfaceFrame();publication.ReconcileIds(new[]{new DynamicSurfaceSpec("a",false,false,"floor","metal"),new DynamicSurfaceSpec("b",false,false,"floor","metal")});
            publication.SetPose(0,Bounds(49.2f),Bounds(49.2f),0,0,1,false);Reject(()=>publication.Pose(0),"Частично обновлённый frame был доступен.");
            publication.SetPose(1,Bounds(60),Bounds(60),0,0,1,false);Check(publication.IsPublished,"Общий frame не завершился.");
            Reject(()=>publication.SetPose(0,Bounds(49.2f),Bounds(49.2f),0,0,1,false),"Принят повторный sample одного ID/sequence.");
            Check(!publication.ReconcileIds(new[]{new DynamicSurfaceSpec("a",false,false,"floor","metal"),new DynamicSurfaceSpec("b",false,false,"floor","metal")}),"Одинаковая topology сбросила carry.");
        }
        private static void ActorConsumers()
        {
            var room=Room();var frame=Frame(room);var settings=MovementSettings.Load();var controller=new MovementController(room,settings);
            var actor=new ActorState {id="standing",owner="connection:1",player=true,connected=true,level="factory:0",x=49.2f,y=10.4f};
            controller.Step(actor,Vector2.zero,false,.1f);frame.SetPose(0,Bounds(49.2f),Bounds(49.48f),0,.1,2,true);
            controller.Step(actor,Vector2.zero,false,.1f);Check(Mathf.Abs(actor.x-49.48f)<.001f && actor.pose=="idle","Active actor не едет или анимация стала walk от carry.");
            frame.SetPose(0,Bounds(49.48f),Bounds(49.76f),.1,.2,3,true);controller.Step(actor,Vector2.zero,true,.1f);
            Check(Mathf.Abs(actor.x-49.48f)<.001f && actor.y>10.4f,"Jump не отделил active actor.");
            var walkingRoom=Room();var walkingFrame=Frame(walkingRoom);var walkingController=new MovementController(walkingRoom,settings);
            var walking=new ActorState {id="walking",owner="connection:1",player=true,connected=true,level="factory:0",x=49.2f,y=10.4f};
            walkingController.Step(walking,Vector2.zero,false,.1f);
            for(int i=1;i<=20;i++)
            {
                walkingFrame.SetPose(0,Bounds(49.2f),Bounds(49.2f),(i-1d)*.1,i*.1,i+1,true);
                walkingController.Step(walking,Vector2.right,false,.1f);
            }
            Check(walking.x>51.2f && walking.y<10.2f,"Выход ногами за край не освободил actor для падения.");
            var solidRoom=Room();var solidFrame=Frame(solidRoom);var solidController=new MovementController(solidRoom,settings);
            var solid=new ActorState {id="solid",owner="connection:1",player=true,connected=true,level="factory:0",x=49.2f,y=10.4f};
            solidController.Step(solid,Vector2.zero,false,.05f);
            for(int i=1;i<=4;i++)
            {
                solidFrame.SetPose(0,Bounds(49.2f),Bounds(49.2f),(i-1d)*.05,i*.05,i+1,true);
                solidController.Step(solid,i%2==1?Vector2.down:Vector2.zero,false,.05f);
            }
            Check(Mathf.Abs(solid.y-10.4f)<.001f && ActorTerrainMotion.Supported(solidRoom,Anatomy.Movement(solid),solid.Position),"Double-S провёл персонажа через сплошную platform2.");
            var passiveRoom=Room();var passiveFrame=Frame(passiveRoom);var body=new ActorState {id="body",owner="connection:1",player=true,connected=true,down=true,level="factory:0",x=49.2f,y=10.4f};
            var registry=new PassengerMotionRegistry();PassiveActorMotion.Step(body,passiveRoom,.1f,settings,registry.ForActor(body));
            passiveFrame.SetPose(0,Bounds(49.2f),Bounds(49.48f),0,.1,2,true);PassiveActorMotion.Step(body,passiveRoom,.1f,settings,registry.ForActor(body));
            Check(Mathf.Abs(body.x-49.48f)<.001f && body.pose=="down","Passive connected body не едет на своей опоре.");
        }
        private static bool Near(Vector2 a,Vector2 b)=>(a-b).sqrMagnitude<.000001f;
        private static void Check(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Проверка динамической опоры: "+reason);}
        private static void Reject(Action action,string reason)
        {try {action();} catch(InvalidOperationException) {return;} throw new InvalidOperationException("Проверка динамической опоры: "+reason);}
    }
}
