using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct SlopeRouteReady : NetworkMessage {public int phase;}
    public struct SlopeRoutePhase : NetworkMessage {public int phase;}
    public struct SlopeRouteDone : NetworkMessage {public bool success;public string detail;}

    // One declared starting-position fixture on installed Warehouse40 geometry.
    // After placement only ordinary commands move actors; this probe never sets poses.
    [DefaultExecutionOrder(20000)]
    public sealed class NetworkSlopePoseProbe : MonoBehaviour
    {
        [Serializable] private sealed class Observation
        {
            public string actor,pose,level;
            public bool local;
            public int frame,facing,generation;
            public uint sequence;
            public float x,y,vx,vy,time,muzzleError;
            public Vector2 bodyPosition,contactPoint;public NetworkFootSurfaceRecord surface;
            public int footPart,footPixelX,footPixelY;public float rootError,contactError;
        }
        [Serializable] private sealed class Report
        {
            public bool success,startingPositionFixture=true,ordinaryMovement=true;
            public string detail,world,role,actor;
            public int participants,frameRate;
            public Vector2 initialPosition,fixturePosition,finalPosition;
            public RoomSurface[] installedSlopes;
            public List<Observation> observations=new();
            public List<MovingEvidence> moving=new();
            public List<int> captures=new();
            public int sampledGroundedFrames;public float maximumRootError,maximumContactError;
            public int unsupportedSlopeFrames;public List<string> unsupportedExamples=new();
            public long terrainPairSnaps;
            public string limitation="Стартовая позиция на нижней опоре склада задана явно. Дальше штатное движение по существующему заводу. Original runtime, слышимый микс и два физических ПК не проверены.";
        }
        [Serializable] private sealed class MovingEvidence {public string actor,pose;public int[] frames;}
        private PrototypeRuntime runtime;
        private InteractionVisuals visuals;
        private readonly Report report=new();
        private readonly Dictionary<int,HashSet<int>> readyPeers=new();
        private readonly HashSet<string> observed=new(StringComparer.Ordinal);
        private readonly Dictionary<string,HashSet<int>> movingFrames=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Vector2> drawnPositions=new(StringComparer.Ordinal);
        private int phase=-1;
        private bool traveled,placed,ready,finished;
        private float deadline,lowerX,upperX,middleX,lowerY;
        private RoomLayout room;
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value,InteractionVisuals presentation)
        {
            runtime=value;visuals=presentation;
            Require(value.Options.Smoke.Contains("-frames-") && value.Options.ProbePeers is 1 or 2 &&
                !string.IsNullOrEmpty(value.Options.SaveDirectory),"Наклонный маршрут требует отдельный fresh save, кадры и1–2 участников.");
            if(Host)
            {
                Require(value.Session.Saves.SelectedSnapshot()==null,"Маршрут не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            report.role=value.Options.Role;report.participants=value.Options.ProbePeers;report.frameRate=value.Options.ProbeFrameRate;
            deadline=Time.unscaledTime+100;value.CommandSource=Command;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<SlopeRoutePhase>(m=>{phase=m.phase;ready=false;});
                NetworkClient.RegisterHandler<SlopeRouteDone>(m=>Finish(m.success,m.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<SlopeRouteReady>((connection,m)=>
            {
                if(value.Session.ConnectedActor(connection)==null) return;
                if(!readyPeers.TryGetValue(m.phase,out var peers)) readyPeers.Add(m.phase,peers=new());
                peers.Add(connection.connectionId);
            });
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(!placed || actor==null || ready || report.captures.Contains(phase)) return default;
            float direction=phase is 1 or 3?1:phase is 4 or 6?-1:0;
            float stop=phase==1?middleX-.15f:phase==3?upperX+.7f:phase==4?middleX+.15f:lowerX-.7f;
            if(direction>0 && actor.x>=stop || direction<0 && actor.x<=stop) direction=0;
            return new PlayerCommand {movement=Vector2.right*direction,weapon="pistol",hasAim=true,
                aim=actor.Position+new Vector2(direction<0?-3:3,1.1f)};
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException($"Таймаут наклона: phase={phase}, actor={runtime.Session.LocalActor?.Position}, pose={runtime.Session.LocalActor?.pose}.");
                var session=runtime.Session;var actor=session.LocalActor;
                if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(!traveled) {traveled=true;session.Travel.RequestFactory();return;}
                if(actor.level!="factory:0" || session.Travel.Pending || runtime.IsLevelPreparing) return;
                if(!placed)
                {
                    room=runtime.Geometry.Get(actor.level);
                    var slopes=room.platforms.Where(s=>s.slopeDirection==1).OrderBy(s=>s.x).ToArray();
                    Require(slopes.Length==4,"В установленном заводе ожидаются четыре положительных наклонных сегмента склада40.");
                    for(int i=0;i<slopes.Length;i++)
                    {
                        Require(slopes[i].oneWay && slopes[i].shotsPassThrough && slopes[i].movementSound=="metal","Свойства исходной лестницы склада изменились.");
                        if(i>0) Require(Mathf.Abs(slopes[i].x-slopes[i-1].x-slopes[i-1].width)<.001f &&
                            Mathf.Abs(slopes[i].y-slopes[i-1].y-slopes[i-1].height)<.001f,"Наклонная лестница имеет разрыв.");
                    }
                    lowerX=slopes[0].x;upperX=slopes[^1].x+slopes[^1].width;lowerY=slopes[0].y;middleX=(lowerX+upperX)*.5f;
                    report.initialPosition=actor.Position;report.fixturePosition=new Vector2(lowerX-1,lowerY);
                    var hull=Anatomy.Movement(actor);
                    Require(ActorTerrainMotion.IsClear(room,hull,report.fixturePosition) && ActorTerrainMotion.Supported(room,hull,report.fixturePosition),"Начальная нижняя опора склада занята или отсутствует.");
                    report.installedSlopes=slopes;report.world=session.World.worldId;
                    actor.Position=report.fixturePosition;actor.vx=actor.vy=0;actor.revision++;session.PublishActor(actor);
                    placed=true;phase=0;SendReady();return;
                }
                Require(!actor.down && actor.health>0,"Персонаж потерял сознание на наклонном маршруте.");
                if(Host && readyPeers.TryGetValue(phase,out var peers) && peers.Count==runtime.Options.ProbePeers)
                {
                    if(phase==7)
                    {
                        session.CommitWorld();session.SaveLocalCharacter();
                        NetworkServer.SendToAll(new SlopeRouteDone {success=true,detail="Четыре наклонные позы, ходьба вверх/вниз, остановки и оба плоских стыка проверены на установленной лестнице склада40."});
                    }
                    else {phase++;ready=false;NetworkServer.SendToAll(new SlopeRoutePhase {phase=phase});}
                }
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private static string Expected(int stage)=>stage switch {1=>"slopeWalkUp",2=>"slopeStandUp",4=>"slopeWalkDown",5=>"slopeStandDown",3 or 6 or 7=>"idle",_=>null};
        private void LateUpdate()
        {
            if(!placed || finished) return;
            try
            {
                var local=runtime.Session.LocalActor;if(local==null) return;
                foreach(var actor in runtime.Session.World.actors.Where(a=>a.player && a.connected && a.level==local.level))
                {
                    if(!actor.pose.StartsWith("slope",StringComparison.Ordinal) || runtime.FindActorVisual(actor.id) is not PonyVisual view ||
                        !view.gameObject.activeInHierarchy || view.DrawnPartCount==0 ||
                        view.DrawnClip!=actor.pose || view.DrawnFrame!=PonyMotionFrames.Sample(actor,view.DrawnTime)) continue;
                    Require(Anatomy.TrySupportContacts(actor,out var contacts) && contacts.Count==4,"Новая поза не имеет четырёх фактических контактов ног.");
                    var support=ActorBodySupport.Sample(actor);
                    Require(view.DrawnClip==support.Pose && view.DrawnFrame==support.Frame && Mathf.Abs(view.DrawnTime-actor.poseTime)<=.00001f,
                        $"Фаза рисунка отличается от общей опоры: actor={actor.id}, drawn={view.DrawnClip}/{view.DrawnFrame}/{view.DrawnTime:R}, body={support.Pose}/{support.Frame}/{actor.poseTime:R}.");
                    float rootError=Vector2.Distance(view.transform.position,support.BodyPosition),contactError=0;
                    Require(support.IsBound && support.HasFootProfile && rootError<=.0001f,
                        $"Составной рисунок не совпадает с общей опорой тела: actor={actor.id}, rootError={rootError:R}, outcome={support.Outcome}.");
                    report.maximumRootError=Mathf.Max(report.maximumRootError,rootError);
                    if(support.Grounded)
                    {
                        Require(support.HasContact,$"На установленном уклоне отсутствует sampled contact: actor={actor.id}, pose={actor.pose}, outcome={support.Outcome}.");
                        var bounds=support.SurfaceBounds;float fraction=(support.ContactPoint.x-bounds.xMin)/bounds.width;
                        float top=bounds.yMin+bounds.height*(support.SlopeDirection>0?fraction:1-fraction);
                        if(support.SlopeDirection==0) top=bounds.yMax;
                        contactError=Mathf.Abs(support.ContactPoint.y-top);
                        Require(support.ContactPoint.x>=bounds.xMin-.0001f && support.ContactPoint.x<=bounds.xMax+.0001f && contactError<=.0002f,
                            $"Контакт копыта выходит из конечной опоры: actor={actor.id}, point={support.ContactPoint}, surface={bounds}, error={contactError:R}.");
                        report.sampledGroundedFrames++;report.maximumContactError=Mathf.Max(report.maximumContactError,contactError);
                    }
                    else
                    {
                        report.unsupportedSlopeFrames++;
                        if(report.unsupportedExamples.Count<16) report.unsupportedExamples.Add($"actor={actor.id}, pose={actor.pose}, frame={support.Frame}, anchor={actor.Position}, phase={actor.poseTime:R}, outcome={support.Outcome}");
                    }
                    var weapon=visuals.FindWeapon(actor.id);
                    if(weapon?.Muzzle==null || actor.weapon!="pistol") continue;
                    float muzzleError=Vector2.Distance(weapon.Muzzle.position,WeaponPose.Muzzle(actor));
                    Require(muzzleError<=.0001f,"Дуло нового наклонного рисунка расходится с оружием.");
                    string key=actor.id+":"+actor.pose;
                    if(actor.pose.StartsWith("slopeWalk",StringComparison.Ordinal))
                    {
                        if(!movingFrames.TryGetValue(key,out var frames)) movingFrames.Add(key,frames=new());
                        Vector2 drawnPosition=view.transform.position;
                        if(view.DrawnFrame>0 && Mathf.Abs(actor.vx)>.1f && drawnPositions.TryGetValue(key,out var previous) &&
                            Vector2.Distance(drawnPosition,previous)>.00001f) frames.Add(view.DrawnFrame);
                        drawnPositions[key]=drawnPosition;
                    }
                    if(observed.Add(key)) report.observations.Add(new Observation {actor=actor.id,pose=actor.pose,level=actor.level,
                        local=actor.id==local.id,frame=view.DrawnFrame,facing=actor.facing,generation=actor.generation,sequence=actor.poseSequence,
                        x=actor.x,y=actor.y,vx=actor.vx,vy=actor.vy,time=view.DrawnTime,muzzleError=muzzleError,
                        bodyPosition=support.BodyPosition,contactPoint=support.ContactPoint,surface=NetworkFootSurfaceRecord.Capture(support),
                        footPart=support.PartIndex,footPixelX=support.PixelX,footPixelY=support.PixelY,rootError=rootError,contactError=contactError});
                }
                if(ready || phase<1) return;
                string expected=Expected(phase);
                if(report.captures.Contains(phase))
                {
                    if(runtime.Session.World.actors.Where(a=>a.player && a.connected).All(a=>observed.Contains(a.id+":"+expected) &&
                        movingFrames.TryGetValue(a.id+":"+expected,out var frames) && frames.Count>=2)) SendReady();
                    return;
                }
                if(local.pose!=expected || runtime.FindActorVisual(local.id) is not PonyVisual localView || localView.DrawnClip!=expected) return;
                bool walking=phase is 1 or 4;
                if(walking && (Mathf.Abs(local.vx)<=.1f || localView.DrawnFrame==0 ||
                    !movingFrames.TryGetValue(local.id+":"+expected,out var localFrames) || localFrames.Count<2 ||
                    phase==1 && local.x<middleX-.6f || phase==4 && local.x>middleX+.6f)) return;
                if(!walking && (Mathf.Abs(local.vx)>.001f || !ActorTerrainMotion.Supported(room,Anatomy.Movement(local),local.Position))) return;
                if(phase==3 && local.x<upperX+.5f || phase is 6 or 7 && local.x>lowerX-.5f) return;
                if(phase is 2 or 5 && runtime.Session.World.actors.Where(a=>a.player && a.connected).Any(a=>
                    a.pose!=expected || Mathf.Abs(a.vx)>.001f || !ActorTerrainMotion.Supported(room,Anatomy.Movement(a),a.Position) ||
                    runtime.FindActorVisual(a.id) is not PonyVisual standingView || !standingView.gameObject.activeInHierarchy ||
                    standingView.DrawnClip!=expected || standingView.DrawnFrame!=0)) return;
                NetworkSceneRecord.Save(runtime,phase);report.captures.Add(phase);
                if(!walking) SendReady();
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void SendReady() {ready=true;NetworkClient.Send(new SlopeRouteReady {phase=phase});}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            string[] poses={"slopeWalkUp","slopeStandUp","slopeWalkDown","slopeStandDown"};
            var players=runtime.Session.World?.actors.Where(a=>a.player && a.connected).ToArray()??Array.Empty<ActorState>();
            if(success && (report.sampledGroundedFrames<20 || report.captures.Count!=7 || players.Any(a=>poses.Any(p=>!observed.Contains(a.id+":"+p))) ||
                players.Any(a=>new[]{"slopeWalkUp","slopeWalkDown"}.Any(p=>!movingFrames.TryGetValue(a.id+":"+p,out var frames) || frames.Count<2))))
            {success=false;detail="Не все реальные локальные/удалённые наклонные кадры наблюдались до завершения маршрута.";}
            report.success=success;report.detail=detail;report.terrainPairSnaps=runtime.TerrainPairSnaps;runtime.Session.SaveLocalCharacter();
            report.actor=runtime.Session.LocalActorId;report.finalPosition=runtime.Session.LocalActor?.Position??Vector2.zero;
            report.moving=movingFrames.Select(pair=>new MovingEvidence {actor=pair.Key.Substring(0,pair.Key.LastIndexOf(':')),
                pose=pair.Key.Substring(pair.Key.LastIndexOf(':')+1),frames=pair.Value.OrderBy(frame=>frame).ToArray()}).ToList();
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));StartCoroutine(Quit(success));
        }
        private IEnumerator Quit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
