using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct HeldColorReady : NetworkMessage {public string actor,world;public int generation,lifecycle;public bool prepared;}
    public struct HeldColorPrepare : NetworkMessage {}
    public struct HeldColorPhase : NetworkMessage
    {public int phase,startGeneration,magic;public string world,source,owner;}
    public struct HeldColorObserved : NetworkMessage
    {public int phase,targetGeneration,generation,lifecycle;public string actor,world;public bool success;public string detail;}
    public struct HeldColorFinish : NetworkMessage {public bool success;public string detail;}

    // Explicit initial fixture only: own player's feet and two different magic
    // colors. The existing crate, contents, health, stock and terrain are untouched.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkHeldPropColorProbe : MonoBehaviour
    {
        public const string TargetId="training:crate:1";
        public const int HostMagic=0x286CFF,ClientMagic=0xE040D0;
        private PrototypeRuntime runtime;
        private InteractionVisuals visuals;
        private RoomLayout room;
        private MovementHull cargoHull;
        private string world,contents,inventory,owner,character;
        private int generation,lifecycle,originalMagic,haloId,materialId,spriteId;
        private Vector2 initialFeet,lift;
        private RoomSurface initialSupport;
        private Vector2 initialTargetPosition;
        private float deadline,phaseAt,stableAt=-1;
        private bool fixture,readySent,reported,capturePending,finished,started,prepareAllowed,handlersReady,onlineSent;
        private int phase=-1,serverPhase=-1;
        private HeldColorPhase manifest;
        private readonly Dictionary<string,HeldColorReady> ready=new(StringComparer.Ordinal);
        private readonly HashSet<string> online=new(StringComparer.Ordinal);
        private readonly Dictionary<int,Dictionary<string,HeldColorObserved>> observed=new();
        private readonly List<Observation> observations=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Held=>phase is 1 or 3;
        private bool Source=>runtime.Session.LocalActorId==manifest.source;
        private ContainerState Cargo=>runtime.Session.World?.containers.Find(c=>c.id==TargetId);
        private string Label=>phase switch {0=>"baseline",1=>"host-held",2=>"host-released",3=>"client-held",4=>"client-released",_=>"preparing"};

        public void Initialize(PrototypeRuntime value,InteractionVisuals views)
        {
            runtime=value;visuals=views??throw new ArgumentNullException(nameof(views));deadline=Time.unscaledTime+100;
            if(value.Options.ProbePeers!=2 || !value.Options.Smoke.Contains("-frames-") || string.IsNullOrWhiteSpace(value.Options.SaveDirectory) ||
                string.IsNullOrWhiteSpace(value.Options.Report) || value.Hud==null || Screen.width!=1920 || Screen.height!=1080)
                throw new InvalidOperationException("Цвет удержания требует двух участников, -frames-, HUD, отдельные новые сохранения и1920×1080.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Цвет удержания не заменяет существующий выбранный мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;value.Session.Notice+=Notice;
            value.Session.ClientReady+=RegisterClient;
            value.Session.ServerReady+=RegisterServer;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<HeldColorPrepare>(_=>prepareAllowed=true);
            NetworkClient.RegisterHandler<HeldColorPhase>(message=>
            {
                if(finished) return;
                if(message.phase==phase && message.world==manifest.world) return;
                Require(fixture && message.world==world && message.phase==phase+1,"Нарушен мир/порядок фаз цвета удержания.");
                manifest=message;phase=message.phase;phaseAt=Time.unscaledTime;stableAt=-1;reported=capturePending=false;
            });
            NetworkClient.RegisterHandler<HeldColorFinish>(message=>Finish(message.success,message.detail));
            handlersReady=true;
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<HeldColorReady>((connection,message)=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                if(actor==null || actor.id!=message.actor || message.world!=runtime.Session.ServerWorld.worldId) return;
                if(!message.prepared)
                {
                    if(online.Add(actor.id) && online.Count==2) NetworkServer.SendToAll(new HeldColorPrepare());
                    return;
                }
                ready[actor.id]=message;
            });
            NetworkServer.RegisterHandler<HeldColorObserved>((connection,message)=>
            {
                try
                {
                    var actor=runtime.Session.ConnectedActor(connection);
                    Require(actor!=null && actor.id==message.actor && message.world==runtime.Session.ServerWorld.worldId &&
                        actor.generation==message.generation && actor.lifecycleSequence==message.lifecycle,"Неверный observer identity/epoch.");
                    Require(message.success,message.detail);
                    if(message.phase<serverPhase && observed.TryGetValue(message.phase,out var previous) && previous.ContainsKey(actor.id)) return;
                    Require(message.phase==serverPhase,"Свидетельство не относится к текущей фазе.");
                    if(!observed.TryGetValue(serverPhase,out var peers)) observed.Add(serverPhase,peers=new(StringComparer.Ordinal));
                    peers[actor.id]=message;if(peers.Count!=2) return;
                    var cargo=runtime.Session.ServerWorld.containers.Find(c=>c.id==TargetId);
                    foreach(var witness in peers.Values) Require(witness.targetGeneration==cargo.generation,"Разошлись подтверждённые поколения предмета.");
                    Require(cargo.held==(serverPhase is 1 or 3) && (!cargo.held || cargo.owner==manifest.owner),"Canonical хват не совпал с обоими свидетелями.");
                    if(serverPhase==4) NetworkServer.SendToAll(new HeldColorFinish {success=true,
                        detail="Два клиента независимо увидели исходный свободный ящик, обычный Q синего держателя, отпускание, Q второго пурпурного держателя и отпускание; actual renderer colors/ресурсы/слои и содержимое проверены."});
                    else Advance(serverPhase+1);
                }
                catch(Exception error) {Fail(error.Message);}
            });
        }
        private void Advance(int next)
        {
            var session=runtime.Session;var cargo=session.ServerWorld.containers.Find(c=>c.id==TargetId);
            ActorState source=null;
            foreach(var actor in session.ServerWorld.actors) if(actor.player && actor.connected &&
                (next<=2?actor.id==session.LocalActorId:actor.id!=session.LocalActorId))
            {Require(source==null,"Неоднозначный источник фазы.");source=actor;}
            Require(source!=null,"Нет источника следующей фазы.");serverPhase=next;
            // The host's local manifest is installed by the same reliable message as every observer.
            NetworkServer.SendToAll(new HeldColorPhase {phase=next,startGeneration=cargo.generation,world=session.ServerWorld.worldId,
                source=source.id,owner=source.owner,magic=next<=2?HostMagic:ClientMagic});
        }
        private void PrepareFixture()
        {
            var session=runtime.Session;var actor=session.LocalActor;var cargo=Cargo;
            Require(actor.level=="hub" && WorldRooms.Definition(session.World,"factory:0")==NewGameWorld.FactoryDefinitionId,
                "Нужен реальный NewGameWorld, не LegacyPrototypeFixture.");
            Require(cargo!=null && cargo.definition=="mcrate1" && cargo.level=="hub" && cargo.hasPhysicalBody && cargo.movable &&
                cargo.mass==25 && !cargo.held,"Нет существующего свободного25кг тренировочного ящика.");
            room=runtime.Geometry.Get("hub");float cargoFloor=cargo.y-cargo.anchorHeight;
            cargoHull=new MovementHull {width=cargo.width,height=cargo.height};
            var hull=Anatomy.Movement(actor);
            initialFeet=FindInitialFeet(room,hull,cargo.x+(Host?-1.3f:1.3f),cargoFloor-hull.height,out initialSupport);
            initialTargetPosition=cargo.Position;lift=cargo.Position+Vector2.up*.65f;
            Require(ActorTerrainMotion.IsClear(room,hull,initialFeet) && ActorTerrainMotion.Supported(room,hull,initialFeet),
                "Начальная собственная fixture не свободна/не имеет реальной опоры: feet="+initialFeet+", hull="+hull.width+"x"+hull.height+", support="+StateJson.ToJson(initialSupport));
            var body=new Rect(initialFeet.x-hull.width*.5f,initialFeet.y,hull.width,hull.height);
            foreach(var prop in WorldProps.All(session.World)) if(prop.level=="hub" && prop.hasPhysicalBody)
            {
                var bounds=new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height);
                Require(!body.Overlaps(bounds),"Fixture пересекает реальное тело prop="+prop.id+", bounds="+bounds);
            }
            var prepared=StateJson.Copy(actor);prepared.Position=initialFeet;
            var rules=session.Rules.Telekinesis;
            string rejection=rules.Rejection(prepared,cargo,false);
            if(rejection==null && rules is ITelekinesisSightRule sight) rejection=sight.SightRejection(prepared,cargo,room);
            Require(rejection==null,"Начальная fixture не допускает настоящий Q: feet="+initialFeet+", cargo="+cargo.Position+", reason="+rejection);
            Require(Vector2.Distance(rules.Target(prepared,lift),lift)<.00001f &&
                ActorTerrainMotion.IsClear(room,cargoHull,lift-Vector2.up*cargo.anchorHeight) &&
                Vector2.Distance(ActorTerrainMotion.Move(room,cargoHull,cargo.Position-Vector2.up*cargo.anchorHeight,lift-cargo.Position,false),lift-Vector2.up*cargo.anchorHeight)<.00001f,
                "Обычная вертикальная траектория Q заблокирована/обрезана: cargo="+cargo.Position+", lift="+lift+", feet="+initialFeet);
            var selection=TelekinesisTarget.Nearest(session.World,prepared,cargo.Center);
            Require(selection.HasValue && selection.Value.Id==TargetId,"Aim реального ящика выбирает другую цель.");
            world=session.World.worldId;contents=StateJson.ToJson(cargo.items);inventory=StateJson.ToJson(actor.inventory);
            owner=actor.owner;character=actor.character;generation=actor.generation;lifecycle=actor.lifecycleSequence;
            originalMagic=PonyAppearance.Snapshot(actor).Magic;
            actor.appearance??=new CharacterAppearance {version=1};actor.appearance.magic=Host?HostMagic:ClientMagic;
            actor.Position=initialFeet;actor.vx=actor.vy=0;actor.revision++;session.PublishActor(actor);fixture=true;phaseAt=Time.unscaledTime;
            Debug.Log($"Цвет удержания INITIAL fixture: world={world}, actor={actor.id}, feet={initialFeet}, originalMagic={originalMagic:X6}, magic={actor.appearance.magic:X6}; actual prop={TargetId}, pos={cargo.Position}. Объект/HP/запас/геометрия не изменены; полный путь до ящика не проверяется.");
        }
        // Probe-only initial placement: the highest actual clear flat support below
        // the crate shelf. Crate height is never substituted for an actor's floor.
        public static Vector2 FindInitialFeet(RoomLayout layout,MovementHull hull,float x,float maximumFloor,out RoomSurface support)
        {
            support=null;float highest=float.NegativeInfinity;
            foreach(var surface in RoomSurfaces.All(layout))
            {
                float top=surface.y+surface.height;
                if(surface.slopeDirection!=0 || top>maximumFloor || top<=highest) continue;
                var feet=new Vector2(x,top);
                if(x+hull.width*.5f<=surface.x || x-hull.width*.5f>=surface.x+surface.width ||
                    !ActorTerrainMotion.IsClear(layout,hull,feet) || !ActorTerrainMotion.Supported(layout,hull,feet)) continue;
                highest=top;support=surface;
            }
            Require(support!=null,"Нет свободной flat-опоры для INITIAL actor fixture: room="+layout.id+", x="+x+", maximumFloor="+maximumFloor+", hull="+hull.width+"x"+hull.height);
            return new Vector2(x,highest);
        }
        private PlayerCommand Command()
        {
            if(finished || !fixture || phase<0) return default;
            return new PlayerCommand {hasAim=true,aim=Held && Source?lift:Cargo.Center,telekinesis=Held && Source};
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                Require(Time.unscaledTime<=deadline,"Таймаут цвета удержания: "+Context());
                var session=runtime.Session;var actor=session.LocalActor;
                if(actor==null || session.World==null || Connected(session.World)!=2 || !runtime.LevelPresentationReady || !handlersReady) return;
                // ClientReady only installs handlers, before Welcome. Send after
                // the actual world/actor exist; both observers install Finish first.
                if(!onlineSent)
                {
                    onlineSent=true;NetworkClient.Send(new HeldColorReady {actor=actor.id,world=session.World.worldId,
                        generation=actor.generation,lifecycle=actor.lifecycleSequence,prepared=false});
                }
                if(!prepareAllowed) return;
                if(!fixture) PrepareFixture();
                Require(session.World.worldId==world && actor.owner==owner && actor.generation==generation && actor.lifecycleSequence==lifecycle &&
                    actor.character==character && actor.level=="hub" && !actor.down && actor.health>0 && !session.Pause.IsPaused("hub"),"Изменилась identity/управление наблюдателя: "+Context());
                Require(PonyAppearance.Snapshot(actor).Magic==(Host?HostMagic:ClientMagic),"Изменился первоначально объявленный цвет собственного профиля.");
                if(!readySent && Time.unscaledTime-phaseAt>2)
                {readySent=true;NetworkClient.Send(new HeldColorReady {actor=actor.id,world=world,generation=generation,lifecycle=lifecycle,prepared=true});}
                if(Host && !started && ready.Count==2 && CanonicalReady()) {started=true;Advance(0);}
                if(phase<0 || reported || capturePending) return;
                var cargo=Cargo;var view=visuals.FindContainer(TargetId);
                bool current=cargo.held==Held && (phase==0?cargo.generation==manifest.startGeneration:cargo.generation>manifest.startGeneration) &&
                    (!Held || cargo.owner==manifest.owner && Vector2.Distance(cargo.Position,lift)<.12f) && view!=null;
                if(current && Held)
                    current=view.HeldGlowVisible && view.HeldGlowHolder==manifest.source && view.HeldGlowMagic==manifest.magic;
                if(current && !Held)
                    current=!view.HeldGlowVisible && Mathf.Abs(cargo.vx)+Mathf.Abs(cargo.vy)<.12f &&
                        ActorTerrainMotion.Supported(room,cargoHull,cargo.Position-Vector2.up*cargo.anchorHeight);
                if(!current) {stableAt=-1;return;}
                if(stableAt<0) stableAt=Time.unscaledTime;
                if(Time.unscaledTime-stableAt>.65f && Visible(view.Artwork.bounds)) capturePending=true;
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private bool CanonicalReady()
        {
            foreach(var actor in runtime.Session.ServerWorld.actors) if(actor.player && actor.connected)
            {
                if(!ready.TryGetValue(actor.id,out var message) || actor.generation!=message.generation || actor.lifecycleSequence!=message.lifecycle || actor.level!="hub" ||
                    PonyAppearance.Snapshot(actor).Magic!=(actor.id==runtime.Session.LocalActorId?HostMagic:ClientMagic)) return false;
            }
            return true;
        }
        private void LateUpdate()
        {
            if(finished || !capturePending) return;
            try
            {
                capturePending=false;var cargo=Cargo;var view=visuals.FindContainer(TargetId);var halo=view.HeldGlowRenderer;
                Require(StateJson.ToJson(runtime.Session.LocalActor.inventory)==inventory && StateJson.ToJson(cargo.items)==contents,
                    "Q изменил исходный inventory/contents.");
                Require(view.HeldGlowVisible==Held && view.HeldGlowPreparedFrameCount==1 && halo!=null,"Actual frame/видимость свечения не совпали с фазой.");
                if(haloId==0) {haloId=halo.GetInstanceID();materialId=halo.sharedMaterial.GetInstanceID();spriteId=halo.sprite.GetInstanceID();}
                Require(halo.GetInstanceID()==haloId && halo.sharedMaterial.GetInstanceID()==materialId && halo.sprite.GetInstanceID()==spriteId,
                    "Передача/движение пересоздали actual renderer/material/sprite ящика.");
                var block=new MaterialPropertyBlock();halo.GetPropertyBlock(block);var color=block.GetColor("_Color");
                if(Held)
                {
                    var expected=PonyAppearance.FromRgb(manifest.magic);
                    Require(block.HasColor("_Color") && Mathf.Abs(color.r-expected.r)<.00001f && Mathf.Abs(color.g-expected.g)<.00001f &&
                        Mathf.Abs(color.b-expected.b)<.00001f && color.a>0 && view.HeldGlowHolder==manifest.source &&
                        view.Depth.Category==WorldVisualCategory.Telekinesis,"Actual цвет/слой не соответствует подтверждённому держателю.");
                }
                else Require(view.Depth.Category==WorldVisualCategory.OrdinaryObject,"После отпускания не восстановлен обычный слой.");
                var bounds=view.Artwork.bounds;Require(Visible(bounds),"Actual ящик находится вне кадра.");
                string path=runtime.Options.Report+".held-prop-color-"+Label+".png";
                var observation=new Observation {phase=phase,label=Label,world=world,observer=runtime.Session.LocalActorId,
                    target=TargetId,source=manifest.source,owner=cargo.owner,generation=cargo.generation,held=cargo.held,holder=view.HeldGlowHolder,
                    expectedMagic=manifest.magic,actualMagic=view.HeldGlowMagic,actualColor=color,haloVisible=view.HeldGlowVisible,
                    renderer=haloId,material=materialId,sprite=spriteId,frameCount=view.HeldGlowPreparedFrameCount,
                    propPosition=cargo.Position,actorPosition=runtime.Session.LocalActor.Position,cameraPosition=runtime.WorldCamera.transform.position,
                    minimum=bounds.min,maximum=bounds.max,viewportMinimum=runtime.WorldCamera.WorldToViewportPoint(bounds.min),
                    viewportMaximum=runtime.WorldCamera.WorldToViewportPoint(bounds.max),level=room.id,capture=path};
                NetworkFrameCapture.Save(runtime.WorldCamera,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);
                File.WriteAllText(Path.ChangeExtension(path,".json"),StateJson.ToJson(observation,true));
                NetworkSceneRecord.Save(runtime,80+phase);observations.Add(observation);reported=true;
                var actor=runtime.Session.LocalActor;
                NetworkClient.Send(new HeldColorObserved {phase=phase,targetGeneration=cargo.generation,actor=actor.id,world=world,
                    generation=actor.generation,lifecycle=actor.lifecycleSequence,success=true,detail=Label});
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private bool Visible(Bounds bounds)
        {var min=runtime.WorldCamera.WorldToViewportPoint(bounds.min);var max=runtime.WorldCamera.WorldToViewportPoint(bounds.max);return min.z>0 && min.x>=0 && min.y>=0 && max.x<=1 && max.y<=1;}
        private static int Connected(WorldState world) {int count=0;foreach(var actor in world.actors) if(actor.player && actor.connected) count++;return count;}
        private void Notice(string text) {if(!finished) Fail("Штатный ввод получил отказ: "+text+"; "+Context());}
        private string Context()=>"phase="+phase+", handlersReady="+handlersReady+", prepareAllowed="+prepareAllowed+", source="+manifest.source+", self="+runtime.Session.LocalActorId+", propOwner="+Cargo?.owner+
            ", generation="+Cargo?.generation+", held="+Cargo?.held+", position="+Cargo?.Position+", mana="+runtime.Session.LocalActor?.mana;
        private void Fail(string detail)
        {
            if(finished) return;
            Debug.LogError("Цвет удержания: "+detail);
            if(NetworkServer.active) NetworkServer.SendToAll(new HeldColorFinish {success=false,detail=detail});
            else if(NetworkClient.isConnected)
            {
                var actor=runtime.Session.LocalActor;
                NetworkClient.Send(new HeldColorObserved {phase=phase,actor=actor?.id,world=runtime.Session.World?.worldId,
                    generation=actor?.generation??0,lifecycle=actor?.lifecycleSequence??0,success=false,detail=detail});
            }
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && observations.Count!=5) {success=false;detail="Нет пяти независимых кадров/наблюдений: "+observations.Count;}
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,initialFixture=fixture,inputOnlyAfterFixture=fixture,
                fixtureReason="Только собственные начальные feet на проверенной настоящей опоре ниже полки существующего ящика и разные magic двух профилей; не полный маршрут до ящика.",
                originalMagic=originalMagic,preparedMagic=Host?HostMagic:ClientMagic,initialFeet=initialFeet,initialSupport=initialSupport,initialTargetPosition=initialTargetPosition,lift=lift,target=TargetId,world=world,
                observations=observations.ToArray()},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        private void OnDestroy()
        {if(runtime!=null) {runtime.Session.Notice-=Notice;runtime.Session.ClientReady-=RegisterClient;runtime.Session.ServerReady-=RegisterServer;}}
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Цвет удержания: "+reason);}
        [Serializable] private sealed class Observation
        {
            public int phase,expectedMagic,generation,renderer,material,sprite,frameCount;public int? actualMagic;
            public string label,world,observer,target,source,owner,holder,level,capture;
            public bool held,haloVisible;public Color actualColor;public Vector2 propPosition,actorPosition;
            public Vector3 cameraPosition,minimum,maximum,viewportMinimum,viewportMaximum;
        }
        [Serializable] private sealed class Report
        {public bool success,initialFixture,inputOnlyAfterFixture;public string detail,fixtureReason,target,world;public int originalMagic,preparedMagic;
            public Vector2 initialFeet,initialTargetPosition,lift;public RoomSurface initialSupport;public Observation[] observations;}
    }
}
