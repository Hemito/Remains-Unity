using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct TravelBlockedObserved : NetworkMessage {}
    public struct TravelAtExit : NetworkMessage {}
    public struct TravelRouteDone : NetworkMessage {}
    public struct TravelRouteFinish : NetworkMessage {}
    // Ordinary saved rooms, real movement/E and Unity's isolated virtual pointer.
    // No direct teleport permission and no actor-position injection.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkTravelPointProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private FactorySeamRoute seam;
        private int phase,route;
        private float ladderX,deadline;
        private bool finished,jumped,interacted,clicking,separated,blocked,farRejected,pausedExit,preserved,pauseRequested;
        private string roomState;
        private string capture;
        private readonly HashSet<int> done=new(),blockedPeers=new(),atExit=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Solo=>runtime.Options.ProbePeers==1;
        private ActorState Actor=>runtime.Session.LocalActor;
        private TravelPoint Map=>runtime.Session.Travel.Points("hub").Single(p=>p.kind==TravelPointKind.Map);
        private TravelPoint ExitPoint=>runtime.Session.Travel.Points("factory:0").Single(p=>p.kind==TravelPointKind.Exit);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+110;
            if(value.Options.Smoke.Contains("-seam-")) {seam=new FactorySeamRoute(value);deadline+=70;}
            if(value.Options.Smoke.Contains("-office-",StringComparison.Ordinal)) deadline+=240;
            if(value.Session.DebugTravelAllowed || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Маршрут требует обычных правил перехода и отдельного сохранения.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Маршрут не перезаписывает существующий мир.");
                var world=NewGameWorld.Create();world.worldId="2bbb03303d6848758e98eb9a573eac8d";value.Session.Saves.SaveWorld(world);
            }
            var ladder=RoomLayout.Load("hub").ladders.Where(l=>l.x<20 && l.bottom<1 && l.top>7.2f).OrderByDescending(l=>l.x).First();
            ladderX=ladder.x+ladder.movementOffset;
            value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                value.Session.Travel.Replied+=reply=>
                {
                    if(reply.id!="travel-points-far") return;
                    farRejected=!reply.accepted && reply.reason=="Подойдите к точке перехода ближе.";
                    if(!farRejected) Finish(false,"Сервер разрешил переход издалека или указал другую причину.");
                };
                NetworkClient.RegisterHandler<TravelRouteFinish>(_=>Finish(phase==12 && pausedExit && preserved && farRejected && (Solo || separated) && (Host || blocked) && (seam==null || seam.Complete),
                    "Движение к карте, E, выбор завода указателем, разделение уровней, отказ издалека, выход через дверь при паузе и повторный вход с сохранённым содержимым."));
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<TravelBlockedObserved>((connection,_)=>blockedPeers.Add(connection.connectionId));
                NetworkServer.RegisterHandler<TravelAtExit>((connection,_)=>atExit.Add(connection.connectionId));
                NetworkServer.RegisterHandler<TravelRouteDone>((connection,_)=>
                {if(value.Session.ConnectedActor(connection)?.level=="hub") done.Add(connection.connectionId);});
            };
        }
        private PlayerCommand Command()
        {
            var actor=Actor;if(actor==null || finished) return default;
            if(phase==13) return seam.Command();
            if(phase==0)
            {
                if(runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return default;
                if(route==0)
                {
                    if(Mathf.Abs(actor.x-ladderX)>.06f) return Move(ladderX);
                    route=1;
                }
                if(route==1)
                {
                    if(actor.y<7.22f) return new PlayerCommand {movement=Vector2.up};
                    route=2;
                }
                if(!jumped) {jumped=true;return new PlayerCommand {movement=Vector2.right,jump=true};}
                if(Mathf.Abs(actor.x-Map.arrivalX)>.08f) return Move(Map.arrivalX);
                if(!ActorTerrainMotion.Supported(runtime.Geometry.Get(actor.level),Anatomy.Movement(actor),actor.Position) || Map.Rejection(actor)!=null) return default;
                if(!Host && !runtime.Session.World.actors.Any(a=>a.player && a.connected && a.id!=actor.id && a.level=="factory:0")) return default;
                if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,20);
                phase=1;return new PlayerCommand {interact=true,hasAim=true,aim=Map.Bounds.center};
            }
            if(phase==4) return Move(6);
            if(phase==5) return Move(runtime.Geometry.Get(actor.level).spawnX);
            if(phase==7)
            {
                if(Host && !Solo && blockedPeers.Count<runtime.Options.ProbePeers-1) return default;
                if(!interacted)
                {
                    var selected=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,ExitPoint.Bounds.center,runtime.Session.Travel.Points(actor.level));
                    if(selected.kind!=InteractionKind.Exit) {Finish(false,"E у входа не выбирает выходную дверь: "+selected.kind);return default;}
                    interacted=true;pausedExit=runtime.Session.Pause.IsPaused(actor.level);
                    return new PlayerCommand {interact=true,hasAim=true,aim=ExitPoint.Bounds.center};
                }
            }
            if(phase==8 && !runtime.Hud.WorldMap.IsOpen) return new PlayerCommand {interact=true,hasAim=true,aim=Map.Bounds.center};
            if(phase==11 && !interacted) {interacted=true;return new PlayerCommand {interact=true,hasAim=true,aim=ExitPoint.Bounds.center};}
            return default;
        }
        private PlayerCommand Move(float x)=>new() {movement=Vector2.right*(Mathf.Abs(Actor.x-x)>.06f?Mathf.Sign(x-Actor.x):0)};
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут карты/выхода: phase={phase}, route={route}, pos={Actor?.Position}, level={Actor?.level}, {seam?.Progress}.");return;}
            var actor=Actor;if(actor==null) return;
            var session=runtime.Session;
            if(session.World.actors.Any(a=>a.player && a.connected && a.id!=actor.id && a.level!=actor.level)) separated=true;
            if(phase==1 && runtime.Hud.WorldMap.IsOpen && !clicking)
            {phase=2;clicking=true;StartCoroutine(ChooseFactory(true));}
            else if(phase==2 && actor.level=="factory:0" && !session.Travel.Pending)
            {
                if(WorldRooms.Definition(session.World,actor.level)!=NewGameWorld.FactoryDefinitionId) {Finish(false,"Маршрут использовал старый тестовый завод.");return;}
                phase=3;
            }
            else if(phase==3 && session.World.actors.Count(a=>a.player && a.connected && a.level=="factory:0")==runtime.Options.ProbePeers)
            {if(seam==null) {roomState=ContentState(session.World);phase=4;} else {seam.Begin();phase=13;}}
            else if(phase==13)
            {
                seam.Tick();
                if(seam.Failure!=null) {Finish(false,seam.Failure);return;}
                if(seam.Complete) {roomState=ContentState(session.World);phase=4;}
            }
            else if(phase==4 && Mathf.Abs(actor.x-6)<.08f)
            {
                if(ExitPoint.Rejection(actor)==null) {Finish(false,"Дверь достижима издалека.");return;}
                session.PublishActor(actor);
                NetworkClient.Send(new TravelRequest {id="travel-points-far",point=ExitPoint.id,source=actor.level,destination="hub",generation=actor.generation,lifecycle=actor.lifecycleSequence});
                phase=5;
            }
            else if(phase==5 && Mathf.Abs(actor.x-runtime.Geometry.Get(actor.level).spawnX)<.08f && farRejected)
            {
                phase=6;NetworkClient.Send(new TravelAtExit());
            }
            else if(phase==6 && Host && atExit.Count==runtime.Options.ProbePeers && !pauseRequested)
            {pauseRequested=true;runtime.Hud.ToggleMenu();StartCoroutine(PauseAtExit());}
            else if(phase==6 && session.Pause.IsPaused("factory:0") && !runtime.Hud.PauseMenu.IsOpen)
            {phase=7;interacted=false;}
            else if(phase==7 && actor.level=="hub" && !session.Travel.Pending)
            {
                if(Vector2.Distance(actor.Position,Map.Arrival)>.1f) {Finish(false,"Выход не вернул к карте на поддержанную позицию.");return;}
                phase=8;clicking=false;
            }
            else if(phase==8 && runtime.Hud.WorldMap.IsOpen && !clicking)
            {clicking=true;StartCoroutine(ChooseFactory(false));}
            else if(phase==9 && !session.Pause.IsPaused("factory:0") && !clicking)
            {phase=10;clicking=true;StartCoroutine(ClickTravel());}
            else if(phase==10 && actor.level=="factory:0" && !session.Travel.Pending)
            {
                preserved=roomState==ContentState(session.World);
                if(!preserved) {Finish(false,"Повторный вход изменил ID или содержимое объектов завода.");return;}
                Capture("exit");phase=11;interacted=false;
            }
            else if(phase==11 && actor.level=="hub" && !session.Travel.Pending)
            {phase=12;NetworkClient.Send(new TravelRouteDone());}
            if(Host && done.Count==runtime.Options.ProbePeers) {done.Clear();NetworkServer.SendToAll(new TravelRouteFinish());}
        }
        private IEnumerator PauseAtExit()
        {
            yield return UiPointerProbe.Click(runtime.Hud.PauseMenu.ManualPauseRect);
            while(!runtime.Session.Pause.IsPaused("factory:0")) yield return null;
            runtime.Hud.ToggleMenu();
        }
        private IEnumerator ChooseFactory(bool first)
        {
            yield return UiPointerProbe.Click(runtime.Hud.WorldMap.FactoryRect);
            if(first) {Capture("map");yield return ClickTravel();}
            else
            {
                if(!Host && !Solo)
                {
                    blocked=runtime.Session.Pause.IsPaused("factory:0") && !runtime.Hud.WorldMap.CanTravel;
                    if(!blocked) {Finish(false,"Карта разрешила вход в остановленный завод.");yield break;}
                    Capture("blocked");NetworkClient.Send(new TravelBlockedObserved());
                }
                phase=9;clicking=false;
            }
        }
        private IEnumerator ClickTravel()
        {
            if(!runtime.Hud.WorldMap.CanTravel) {Finish(false,"Выбранный завод недоступен после снятия паузы.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.WorldMap.TravelRect);clicking=false;
        }
        private static string ContentState(WorldState world)=>string.Join("|",world.containers.Where(c=>c.level=="factory:0").OrderBy(c=>c.id)
            .Select(c=>c.id+":"+string.Join(",",c.items.Select(i=>i.id+":"+i.definition+":"+i.count))))+
            ";"+string.Join("|",world.props.Where(p=>p.level=="factory:0").Select(p=>p.id).OrderBy(id=>id))+
            ";"+StateJson.ToJson(world.mechanismButtons.Where(p=>p.level=="factory:0").OrderBy(p=>p.id).ToArray())+
            ";"+StateJson.ToJson(world.movingPlatforms.Where(p=>p.level=="factory:0").OrderBy(p=>p.id).ToArray());
        private void Capture(string name)
        {if(runtime.Options.Smoke.Contains("-frames-")) capture=name;}
        private void LateUpdate()
        {seam?.Capture();if(capture==null) return;NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>());capture=null;}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(!success && runtime.Options.Smoke.Contains("-frames-",StringComparison.Ordinal) && Camera.main!=null)
                NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".route-failure.png",runtime.Hud.GetComponent<Canvas>());
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,separated=separated,blocked=blocked,farRejected=farRejected,pausedExit=pausedExit,preserved=preserved,seamVerified=seam?.Complete==true,bigBoxesVerified=seam?.BigBoxesVerified==true,workshopNpcsVerified=seam?.WorkshopNpcsVerified==true,ladderLandingVerified=seam?.LadderLandingVerified==true,
                officeRouteVerified=seam?.OfficeRouteVerified==true,officeLowerFloorVerified=seam?.OfficeLowerFloorVerified==true,officeReturnVerified=seam?.OfficeReturnVerified==true,
                officeContainer=seam?.OfficeContainer,officeClaimedItem=seam?.OfficeClaimedItem,officeClaimedDefinition=seam?.OfficeClaimedDefinition,officeClaimedCount=seam?.OfficeClaimedCount??0,
                officePlatformRepairReady=seam?.OfficePlatformRepairReady==true,officePlatformCarryVerified=seam?.OfficePlatformCarryVerified==true,
                officePlatformCarryDistance=seam?.OfficePlatformCarryDistance??0,officePlatformCarryMaximumRelativeError=seam?.OfficePlatformCarryMaximumRelativeError??0},true));
            exitCode=success?0:1;Invoke(nameof(Exit),3);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase;public bool separated,blocked,farRejected,pausedExit,preserved,seamVerified,bigBoxesVerified,workshopNpcsVerified,ladderLandingVerified;
            public bool officeRouteVerified,officeLowerFloorVerified,officeReturnVerified,officePlatformRepairReady,officePlatformCarryVerified;
            public string officeContainer,officeClaimedItem,officeClaimedDefinition;public int officeClaimedCount;
            public float officePlatformCarryDistance,officePlatformCarryMaximumRelativeError;}
    }
}
