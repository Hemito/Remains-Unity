using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Evidence barriers only; movement and ladder release remain ordinary gameplay.
    public struct LadderContactStage : NetworkMessage {public int stage;}
    public struct LadderContactObserved : NetworkMessage {public int stage;public string observer,detail;public bool success;}
    public struct LadderContactFinish : NetworkMessage {public bool success;public string detail;}

    [DefaultExecutionOrder(10000)]
    public sealed class NetworkLadderContactProbe : MonoBehaviour
    {
        private enum Stage {Waiting,Approach,LowerStart,MountStop,MountReverse,MountJump,AscendShort,DescendShort,AscendHigh,DescendHigh,Complete}
        private PrototypeRuntime runtime;
        private RoomLayout room;
        private RoomLadder ladder;
        private RoomSurface lowerSupport;
        private string ladderId;
        private Stage stage,serverStage;
        private float deadline,stageAt,lowerY,shortY,highY;
        private bool prepared,reported,finished,shortDescendingCapture,highDescendingCapture;
        private bool shortAscended,shortLanded,highAscended,highLanded,repeatNearTop;
        private Vector2 startPosition,lastCommand;
        private int movementCommands,upCommands,downCommands,shortDescendingSamples,highDescendingSamples;
        private int mountCommands,mountSettleCommands;
        private float mountStoppedAt;
        private bool mountCaptured,mountJumpIssued,lastJump;
        private readonly HashSet<Stage> mountedSequences=new();
        private readonly HashSet<int> observations=new();
        private readonly Dictionary<string,PonyVisual> views=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Witness> witnesses=new(StringComparer.Ordinal);
        private readonly List<Sample> trace=new();
        private readonly List<Capture> captures=new();
        private readonly List<Barrier> barriers=new();
        private ContactSource[] contactSources;
        private string highRouteReason,failureCaptureError;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Actor=>runtime.Session.LocalActor;
        private float Axis=>ladder.x+ladder.movementOffset;
        private bool Descending=>stage is Stage.DescendShort or Stage.DescendHigh;
        private bool Mounting=>stage is Stage.MountStop or Stage.MountReverse or Stage.MountJump;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+115;
            if(value.Options.ProbePeers is not (1 or 2) || value.Hud==null || !value.Options.Smoke.Contains("-frames-") ||
                string.IsNullOrEmpty(value.Options.Report) || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Контакт лестницы требует 1–2 участников, HUD, кадры и отдельные новые сохранения.");
            if(Screen.width!=1920 || Screen.height!=1080)
                throw new InvalidOperationException($"Контакт лестницы требует 1920×1080; получено {Screen.width}×{Screen.height}.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null)
                    throw new InvalidOperationException("Проверка лестницы не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<LadderContactObserved>(AcceptObserved);
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<LadderContactStage>(Begin);
                NetworkClient.RegisterHandler<LadderContactFinish>(message=>Finish(message.success,message.detail));
            };
        }

        private void Advance(Stage next)
        {
            serverStage=next;observations.Clear();
            Debug.Log($"Проверка контакта лестницы: server stage={next}, peers={runtime.Options.ProbePeers}.");
            NetworkServer.SendToAll(new LadderContactStage {stage=(int)next});
        }
        private void Begin(LadderContactStage message)
        {
            if(message.stage<=(int)stage) return;
            stage=(Stage)message.stage;stageAt=Time.unscaledTime;reported=false;
            mountCommands=0;mountSettleCommands=0;mountStoppedAt=0;mountCaptured=false;mountJumpIssued=false;
            if(stage==Stage.Approach) StartCoroutine(PrepareWhenReady());
        }
        private void AcceptObserved(NetworkConnectionToClient connection,LadderContactObserved message)
        {
            var sender=runtime.Session.ConnectedActor(connection);
            if(sender==null || sender.id!=message.observer) return;
            if(!message.success)
            {NetworkServer.SendToAll(new LadderContactFinish {success=false,detail=message.detail});return;}
            if(message.stage!=(int)serverStage || !observations.Add(connection.connectionId) || observations.Count!=runtime.Options.ProbePeers) return;
            Stage next=serverStage switch
            {
                Stage.Approach=>Stage.LowerStart,Stage.LowerStart=>Stage.MountStop,Stage.MountStop=>Stage.MountReverse,
                Stage.MountReverse=>Stage.MountJump,Stage.MountJump=>Stage.AscendShort,Stage.AscendShort=>Stage.DescendShort,
                Stage.DescendShort=>Stage.AscendHigh,Stage.AscendHigh=>Stage.DescendHigh,Stage.DescendHigh=>Stage.Complete,
                _=>throw new InvalidOperationException("Неожиданный барьер лестницы: "+serverStage)
            };
            if(next==Stage.Complete)
                NetworkServer.SendToAll(new LadderContactFinish {success=true,
                    detail="Авторская лестница хаба: обычный путь от spawn, подъём на 2 м, спуск и настоящая нижняя опора; контакты фактически нарисованных кадров и отдельные наблюдения каждого владельца."});
            else Advance(next);
        }

        private PlayerCommand Command()
        {
            lastCommand=default;
            lastJump=false;
            if(finished || !prepared || reported || Actor==null) return default;
            if(!repeatNearTop && (stage is Stage.AscendHigh or Stage.DescendHigh)) return default;
            if(stage==Stage.Approach)
            {
                float distance=Axis-Actor.x;
                if(Mathf.Abs(distance)>.12f) lastCommand.x=Mathf.Sign(distance);
            }
            else if(Mounting)
            {
                // A synchronous evidence capture stalls the next game frame. Wait with
                // ordinary neutral commands so it cannot conceal a short first Up step.
                if(mountSettleCommands++<6) return default;
                if(mountCommands<2) {lastCommand.y=1;mountCommands++;}
                else if(stage==Stage.MountStop && (mountStoppedAt==0 || Time.unscaledTime-mountStoppedAt<.2f))
                {
                    if(mountStoppedAt==0) mountStoppedAt=Time.unscaledTime;
                }
                else if(stage==Stage.MountJump && !mountJumpIssued)
                {mountJumpIssued=true;lastJump=true;return new PlayerCommand {jump=true};}
                else if(stage!=Stage.MountJump && (Actor.climbing || Actor.y>lowerY+RoomSupport.ContactTolerance)) lastCommand.y=-1;
            }
            else if(stage==Stage.LowerStart || Descending)
            {
                if(Actor.climbing || Actor.y>lowerY+RoomSupport.ContactTolerance) lastCommand.y=-1;
            }
            else if(stage is Stage.AscendShort or Stage.AscendHigh)
            {
                float target=stage==Stage.AscendShort?shortY:highY;
                if(Actor.y<target) lastCommand.y=1;
            }
            if(lastCommand!=Vector2.zero) movementCommands++;
            if(lastCommand.y>0) upCommands++;
            if(lastCommand.y<0) downCommands++;
            return new PlayerCommand {movement=lastCommand};
        }

        private void Prepare()
        {
            if(Actor.level!="hub" || WorldRooms.Definition(runtime.Session.World,"factory:0")!=NewGameWorld.FactoryDefinitionId)
                throw new InvalidOperationException("Ожидается новый авторский мир и обычный spawn хаба; исключите ladder-contact из LegacyPrototypeFixture.");
            room=runtime.Geometry.Get("hub");startPosition=Actor.Position;
            var hull=Anatomy.Movement(Actor);
            ladder=room.ladders.Where(item=>item.bottom<room.spawnY && item.top>room.spawnY+2)
                .OrderBy(item=>Mathf.Abs(item.x+item.movementOffset-room.spawnX)).FirstOrDefault()
                ??throw new InvalidOperationException("У spawn хаба нет подходящей авторской лестницы.");
            var authoring=RoomAuthoring.Load("hub",runtime.Session.World);
            var authored=authoring.GetComponentsInChildren<RoomLadderAuthoring>(true).Where(authoring.Included).ToArray();
            int index=Array.IndexOf(room.ladders,ladder);
            if(index<0 || index>=authored.Length) throw new InvalidOperationException("Не совпали индексы авторской геометрии лестниц.");
            var read=authored[index].Read(authoring);
            if(read.x!=ladder.x || read.bottom!=ladder.bottom || read.top!=ladder.top || read.movementOffset!=ladder.movementOffset)
                throw new InvalidOperationException("Не совпали авторская лестница и runtime-геометрия.");
            ladderId="hub:"+AuthoringPath(authored[index].transform,authoring.transform);
            lowerSupport=room.platforms.Where(surface=>!surface.oneWay && RoomSupport.ContainsX(surface,Axis) &&
                RoomSupport.Top(surface,Axis,hull.width)<room.spawnY-RoomSupport.ContactTolerance &&
                RoomSupport.Top(surface,Axis,hull.width)>=ladder.bottom-RoomSupport.ContactTolerance)
                .OrderByDescending(surface=>RoomSupport.Top(surface,Axis,hull.width)).FirstOrDefault()
                ??throw new InvalidOperationException("Нет твёрдой нижней опоры у лестницы "+ladderId);
            lowerY=RoomSupport.Top(lowerSupport,Axis,hull.width);shortY=lowerY+2;highY=ladder.top-.5f;
            if(shortY>=ladder.top-.1f || !ActorTerrainMotion.IsClear(room,hull,new Vector2(Axis,lowerY)) ||
                !ActorTerrainMotion.Supported(room,hull,new Vector2(Axis,lowerY)))
                throw new InvalidOperationException("Нет свободной нижней опоры и участка подъёма на 2 м: "+ladderId);
            // Read-only route analysis runs once, never in the movement loop.
            repeatNearTop=highY>shortY+1;
            if(repeatNearTop && !ActorTerrainMotion.IsClear(room,hull,new Vector2(Axis,highY)))
            {repeatNearTop=false;highRouteReason="Верхняя цель закрыта твёрдой геометрией.";}
            for(float y=lowerY;repeatNearTop && y<=highY;y+=.1f)
                if(!ActorTerrainMotion.IsClear(room,hull,new Vector2(Axis,y)))
                {repeatNearTop=false;highRouteReason=$"Свободный вертикальный маршрут прерывается при y={y}.";}
            highRouteReason??=repeatNearTop?"Вертикальная колонна свободна для текущего movement hull; верхняя цель на 0,5 м ниже authored top.":"Недостаточная высота для отдельного верхнего участка.";
            if(!repeatNearTop) Debug.LogWarning("Верхний повтор лестницы пропущен: "+ladderId+"; "+highRouteReason,this);
            foreach(var view in UnityEngine.Object.FindObjectsByType<PonyVisual>(FindObjectsSortMode.None))
            {
                var actor=runtime.Session.World.Find(view.ActorId);
                if(actor==null || actor.level!="hub" || !actor.connected || !actor.player) continue;
                views.Add(actor.id,view);witnesses.Add(actor.id,new Witness {actor=actor.id,local=actor.id==Actor.id});
                // Populate the existing cached envelope before frame observations.
                _=PonyVisual.CameraBounds(actor);
            }
            if(views.Count!=runtime.Options.ProbePeers || !views.ContainsKey(Actor.id))
                throw new InvalidOperationException("Нет независимых визуальных представлений всех участников лестницы.");
            var asset=Resources.Load<TextAsset>("Content/anatomy")??throw new InvalidOperationException("Нет Content/anatomy для записи источника контактов.");
            var catalog=StateJson.FromJson<AnatomyCatalog>(asset.text);
            contactSources=views.Keys.Select(id=>runtime.Session.World.Find(id).anatomy).Distinct().Select(id=>
            {
                var definition=catalog.anatomies.Single(item=>item.id==id).supportContacts
                    ??throw new InvalidOperationException("Нет декларации источника контактов: "+id);
                return new ContactSource {anatomy=id,schema=definition.schema,alphaMinimum=definition.alphaMinimum,
                    renderSourceHash=definition.sourceHash,declaredShapes=definition.shapes};
            }).ToArray();
            prepared=true;
        }
        private IEnumerator PrepareWhenReady()
        {
            while(!finished && !prepared)
            {
                if(Actor!=null && runtime.Session.World!=null &&
                    UnityEngine.Object.FindObjectsByType<PonyVisual>(FindObjectsSortMode.None).Count(view=>
                        runtime.Session.World.Find(view.ActorId) is {player:true,connected:true,level:"hub"})==runtime.Options.ProbePeers)
                {
                    try {Prepare();}
                    catch(Exception error) {Fail(error.Message);}
                    yield break;
                }
                yield return new WaitForSecondsRealtime(.2f);
            }
        }
        private static string AuthoringPath(Transform child,Transform root)
        {
            var parts=new List<string>();
            for(var node=child;node!=root;node=node.parent) parts.Add(node.name+"["+node.GetSiblingIndex()+"]");
            parts.Reverse();return string.Join("/",parts);
        }

        private void LateUpdate()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Истёк срок проверки контакта лестницы.");
                if(Actor==null || runtime.Session.World==null || !NetworkClient.isConnected) return;
                if(Host && serverStage==Stage.Waiting && runtime.Session.World.actors.Count(actor=>actor.player && actor.connected)==runtime.Options.ProbePeers)
                    Advance(Stage.Approach);
                if(stage==Stage.Waiting) return;
                if(!prepared) return;
                if(Time.unscaledTime-stageAt>30) throw new InvalidOperationException("Истёк срок этапа лестницы.");
                if(Actor.level!="hub" || ActorSessionRules.IsPassive(Actor) || runtime.Session.Pause.IsPaused("hub"))
                    throw new InvalidOperationException("Изменилось состояние/уровень/пауза владельца лестничного маршрута.");
                ObserveFrames();
                if(reported) return;
                bool lower=!Actor.climbing && Mathf.Abs(Actor.y-lowerY)<=RoomSupport.ContactTolerance &&
                    Mathf.Abs(Actor.vy)<.001f && ActorTerrainMotion.Supported(room,Anatomy.Movement(Actor),Actor.Position);
                switch(stage)
                {
                    case Stage.Approach:
                        if(Mathf.Abs(Actor.x-Axis)<=.3f && Mathf.Abs(Actor.vx)<.1f &&
                            ActorTerrainMotion.Supported(room,Anatomy.Movement(Actor),Actor.Position))
                        {CaptureFrame(14,"approach-support");Observed();}
                        break;
                    case Stage.LowerStart:
                        if(lower) {CaptureFrame(1,"lower-start");Observed();} break;
                    case Stage.MountStop:
                    case Stage.MountReverse:
                    case Stage.MountJump:
                        bool firstAction=mountCommands==2 && (stage==Stage.MountStop?mountStoppedAt>0:
                            stage==Stage.MountJump?lastJump:lastCommand.y<0);
                        if(!mountCaptured && firstAction)
                        {
                            if(stage==Stage.MountJump && (Actor.climbing || Actor.vy<=0 || Actor.y<=lowerY || Actor.pose!="jump"))
                                throw new InvalidOperationException("Обычная команда прыжка после коротких Up не отпустила хват и не запустила настоящее тело.");
                            // Capture only after the stop/reverse/jump command was simulated.
                            // Capturing the preceding Up would turn this action into a long frame.
                            CaptureFrame(8+((int)stage-(int)Stage.MountStop)*2,"mount-"+stage+"-action");mountCaptured=true;
                        }
                        bool sequenceIssued=stage==Stage.MountStop?mountStoppedAt>0 && Time.unscaledTime-mountStoppedAt>=.2f:
                            stage==Stage.MountJump?mountJumpIssued:mountCommands==2 && lastCommand.y<0;
                        // Reverse may reach the floor in its first command. Keep that evidence
                        // after the command, rather than expecting a second airborne frame.
                        if(sequenceIssued) mountedSequences.Add(stage);
                        if(lower && mountedSequences.Contains(stage) && mountCaptured)
                        {CaptureFrame(9+((int)stage-(int)Stage.MountStop)*2,"mount-"+stage+"-lower");Observed();}
                        break;
                    case Stage.AscendShort:
                        if(Actor.y>=shortY && Actor.climbing)
                        {shortAscended=true;CaptureFrame(2,"ascending-short");Observed();} break;
                    case Stage.DescendShort:
                        if(!shortDescendingCapture && ActiveDescending(Actor,views[Actor.id]) && Actor.y<=lowerY+1.5f)
                        {CaptureFrame(3,"descending-short");shortDescendingCapture=true;}
                        if(lower && shortDescendingCapture && shortDescendingSamples>0)
                        {shortLanded=true;CaptureFrame(4,"lower-short");Observed();} break;
                    case Stage.AscendHigh:
                        if(!repeatNearTop) {Observed();break;}
                        if(Actor.y>=highY && Actor.climbing)
                        {highAscended=true;CaptureFrame(5,"ascending-high");Observed();} break;
                    case Stage.DescendHigh:
                        if(!repeatNearTop) {Observed();break;}
                        if(!highDescendingCapture && ActiveDescending(Actor,views[Actor.id]) && Actor.y<=highY-.2f)
                        {CaptureFrame(6,"descending-high");highDescendingCapture=true;}
                        if(lower && highDescendingCapture && highDescendingSamples>0)
                        {highLanded=true;CaptureFrame(7,"lower-high");Observed();} break;
                }
            }
            catch(Exception error) {Fail(error.Message);}
        }

        private static bool ActiveDescending(ActorState actor,PonyVisual view)
            =>actor.climbing && actor.vy<-.001f && view.DrawnClip=="climb";
        private void ObserveFrames()
        {
            var camera=Camera.main??throw new InvalidOperationException("Нет основной камеры контакта лестницы.");
            foreach(var pair in views)
            {
                var actor=runtime.Session.World.Find(pair.Key);
                var view=pair.Value;
                if(actor==null || !view.gameObject.activeInHierarchy) throw new InvalidOperationException("Исчез участник/визуал лестницы: "+pair.Key);
                if(view.DrawnFrame<0) continue;
                if(view.DrawnClip!=ActorPose.Clip(actor) || view.DrawnFrame!=PonyMotionFrames.Sample(actor,view.DrawnTime) ||
                    Mathf.Abs(view.DrawnTime-actor.poseTime)>.00001f ||
                    Vector2.Distance(view.transform.position,actor.Position)>.00001f)
                    throw new InvalidOperationException($"Контакт не соответствует фактическому кадру: actor={actor.id}, clip={view.DrawnClip}/{ActorPose.Clip(actor)}, frame={view.DrawnFrame}, time={view.DrawnTime}/{actor.poseTime}.");
                if(!Anatomy.TrySupportContacts(actor,out var points) || points.Count==0)
                    throw new InvalidOperationException("Нет контактов анатомии фактически нарисованного кадра: "+actor.id);
                float footMinimum=float.PositiveInfinity,clearance=float.PositiveInfinity;
                string supportId=null;float supportTop=0;
                var feet=new Vector2[points.Count];
                for(int index=0;index<points.Count;index++)
                {
                    var point=(Vector2)view.transform.position+new Vector2(points[index].x*view.transform.localScale.x,points[index].y*view.transform.localScale.y);
                    feet[index]=point;footMinimum=Mathf.Min(footMinimum,point.y);
                    for(int floor=0;floor<room.platforms.Length;floor++)
                    {
                        var surface=room.platforms[floor];
                        if(surface.oneWay || !RoomSupport.ContainsX(surface,point.x)) continue;
                        float top=RoomSupport.Top(surface,point.x);
                        // Match the actual floor on/below this drawn body, never an overhead ceiling.
                        if(view.transform.position.y<top-RoomSupport.ContactTolerance) continue;
                        if(point.y-top<clearance) {clearance=point.y-top;supportTop=top;supportId="hub:surface["+floor+"]";}
                    }
                }
                bool descending=ActiveDescending(actor,view);
                bool held=actor.climbing && !ActorSessionRules.IsPassive(actor);
                var envelope=PonyVisual.CameraBounds(actor);
                var cameraMinimum=camera.transform.position-new Vector3(camera.orthographicSize*camera.aspect,camera.orthographicSize,0);
                var cameraMaximum=camera.transform.position+new Vector3(camera.orthographicSize*camera.aspect,camera.orthographicSize,0);
                var sample=new Sample {stage=(int)stage,frame=Time.frameCount,seconds=Time.unscaledTime,deltaTime=Time.deltaTime,actor=actor.id,local=actor.id==Actor.id,
                    body=actor.Position,visual=view.transform.position,velocity=new Vector2(actor.vx,actor.vy),command=actor.id==Actor.id?lastCommand:default,commandJump=actor.id==Actor.id && lastJump,
                    anatomy=actor.anatomy,logicalPose=actor.pose,poseTime=actor.poseTime,drawnClip=view.DrawnClip,drawnFrame=view.DrawnFrame,drawnTime=view.DrawnTime,
                    climbing=actor.climbing,activeDescending=descending,ladder=actor.climbing?CurrentLadderId(actor):null,
                    predictedFeet=feet,predictedFootMinimum=footMinimum,hasSolidFloor=!float.IsPositiveInfinity(clearance),
                    minimumSolidClearance=float.IsPositiveInfinity(clearance)?0:clearance,solidFloor=supportId,solidTop=supportTop,
                    cameraCenter=camera.transform.position,cameraMinimum=cameraMinimum,cameraMaximum=cameraMaximum,
                    fullEnvelopeMinimum=envelope.min,fullEnvelopeMaximum=envelope.max,
                    envelopeLeft=envelope.min.x-cameraMinimum.x,envelopeRight=cameraMaximum.x-envelope.max.x,
                    envelopeBottom=envelope.min.y-cameraMinimum.y,envelopeTop=cameraMaximum.y-envelope.max.y};
                if(trace.Count>=20000) throw new InvalidOperationException("Превышен ограниченный объём лестничной трассы.");
                trace.Add(sample);
                if(sample.local && Mathf.Min(Mathf.Min(sample.envelopeLeft,sample.envelopeRight),Mathf.Min(sample.envelopeBottom,sample.envelopeTop))<.149f)
                    throw new InvalidOperationException($"Камера обрезала текущую общую границу поз: actor={actor.id}, stage={stage}, frame={sample.frame}, camera={sample.cameraCenter}, envelope={envelope}, margins={sample.envelopeLeft}/{sample.envelopeRight}/{sample.envelopeBottom}/{sample.envelopeTop}.");
                var witness=witnesses[actor.id];witness.drawnSamples++;
                if(held)
                {
                    witness.heldSamples++;
                    if(sample.hasSolidFloor && (witness.heldMinimum==null || clearance<witness.heldMinimum.minimumSolidClearance)) witness.heldMinimum=sample;
                    if(sample.hasSolidFloor && clearance< -RoomSupport.ContactTolerance-.00001f)
                        throw new InvalidOperationException($"Нарисованный хват пересёк твёрдый пол: actor={actor.id}, local={sample.local}, stage={stage}, ladder={sample.ladder}, drawn={view.DrawnClip}/{view.DrawnFrame}@{view.DrawnTime}, bodyY={actor.y}, footY={footMinimum}, floor={supportId}@{supportTop}, clearance={clearance}, tolerance={RoomSupport.ContactTolerance}.");
                }
                if(descending)
                {
                    witness.descendingSamples++;
                    if(sample.hasSolidFloor && (witness.minimum==null || clearance<witness.minimum.minimumSolidClearance)) witness.minimum=sample;
                    if(actor.id==Actor.id && Descending)
                    {if(stage==Stage.DescendShort) shortDescendingSamples++;else highDescendingSamples++;}
                    if(sample.hasSolidFloor && clearance< -RoomSupport.ContactTolerance-.00001f)
                        throw new InvalidOperationException($"Нарисованный спуск пересёк твёрдый пол: actor={actor.id}, local={sample.local}, ladder={sample.ladder}, drawn={view.DrawnClip}/{view.DrawnFrame}@{view.DrawnTime}, bodyY={actor.y}, footY={footMinimum}, floor={supportId}@{supportTop}, clearance={clearance}, tolerance={RoomSupport.ContactTolerance}.");
                }
            }
        }
        private string CurrentLadderId(ActorState actor)
        {
            var selected=Array.Find(room.ladders,item=>Mathf.Abs(actor.x-item.x-item.movementOffset)<.65f && actor.y>=item.bottom-.1f && actor.y<item.top+.2f);
            if(selected==ladder) return ladderId;
            throw new InvalidOperationException($"Наблюдаемый хват вышел за выбранную лестницу: actor={actor.id}, body={actor.Position}, expected={ladderId}, selectedIndex={Array.IndexOf(room.ladders,selected)}.");
        }

        private void CaptureFrame(int phase,string name)
        {
            var local=trace.Last(sample=>sample.local);
            NetworkSceneRecord.Save(runtime,phase);
            string path=runtime.Options.Report+".ladder-"+name+".png";
            NetworkFrameCapture.Save(Camera.main,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);
            captures.Add(new Capture {phase=phase,name=name,sample=local});
            File.WriteAllText(runtime.Options.Report+".phase-"+phase+".contact.json",StateJson.ToJson(captures[^1],true));
        }
        private void Observed()
        {
            reported=true;barriers.Add(new Barrier {stage=(int)stage,position=Actor.Position,frame=Time.frameCount,seconds=Time.unscaledTime});
            NetworkClient.Send(new LadderContactObserved {stage=(int)stage,observer=Actor.id,success=true,detail=Context()});
        }
        private string Context()=> $"stage={stage}, server={serverStage}, actor={Actor?.id}, body={Actor?.Position}, vy={Actor?.vy}, climbing={Actor?.climbing}, ladder={ladderId}, floor={lowerY}, targets={shortY}/{highY}, commands={movementCommands}/{upCommands}/{downCommands}, descSamples={shortDescendingSamples}/{highDescendingSamples}, captures={captures.Count}, deadline={deadline}";
        private void Fail(string reason)
        {
            if(finished) return;
            string detail=reason+" "+Context();
            if(NetworkClient.isConnected) NetworkClient.Send(new LadderContactObserved {stage=(int)stage,observer=Actor?.id,success=false,detail=detail});
            if(Host && NetworkServer.active) NetworkServer.SendToAll(new LadderContactFinish {success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            if(success && (mountedSequences.Count!=3 || !shortAscended || !shortLanded || !shortDescendingCapture || shortDescendingSamples==0 ||
                repeatNearTop && (!highAscended || !highLanded || !highDescendingCapture || highDescendingSamples==0) ||
                witnesses.Count!=runtime.Options.ProbePeers || witnesses.Values.Any(value=>value.descendingSamples==0 || value.minimum==null || value.heldSamples==0 || value.heldMinimum==null)))
            {success=false;detail="Неполное независимое доказательство контакта лестницы. "+detail;}
            finished=true;
            if(!success && prepared)
            {
                try {NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".ladder-failed.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);}
                catch(Exception error) {failureCaptureError=error.Message;Debug.LogWarning("Не удалось снять ошибку лестницы: "+error.Message,this);}
            }
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,context=Context(),role=runtime.Options.Role,
                observer=Actor?.id,world=runtime.Session.World?.worldId,level=Actor?.level,width=1920,height=1080,peers=runtime.Options.ProbePeers,
                start=startPosition,authoredSpawn=room==null?default:new Vector2(room.spawnX,room.spawnY),end=Actor?.Position??default,
                ladderId=ladderId,ladder=ladder,lowerSupport=lowerSupport,lowerY=lowerY,shortY=shortY,highY=highY,
                repeatNearTop=repeatNearTop,highRouteReason=highRouteReason,shortAscended=shortAscended,shortLanded=shortLanded,highAscended=highAscended,highLanded=highLanded,
                movementCommands=movementCommands,upCommands=upCommands,downCommands=downCommands,shortDescendingSamples=shortDescendingSamples,highDescendingSamples=highDescendingSamples,mountSequences=mountedSequences.Count,
                contactSources=contactSources,witnesses=witnesses.Values.ToArray(),captures=captures.ToArray(),barriers=barriers.ToArray(),trace=trace.ToArray(),failureCaptureError=failureCaptureError,
                contactTolerance=RoomSupport.ContactTolerance,limit="Контакты — предсказанные минимумы видимых опорных частей из общего baked source; не анализ PNG каждого кадра. Idle не проверяется на все пиксели копыт. Камера записана с полным envelope, без утверждения идеального следования. Remote — отдельное локальное представление реплики. ActorState не хранит ID лестницы: наблюдаемый хват сопоставляется с authored hierarchy path и runtime geometry; это диагностический ID, не persistent gameplay ID."},true));
            exitSuccess=success;Invoke(nameof(Exit),1.5f);
        }
        private bool exitSuccess;
        private void Exit()=>Application.Quit(exitSuccess?0:2);

        [Serializable] private sealed class Barrier {public int stage,frame;public float seconds;public Vector2 position;}
        [Serializable] private sealed class Capture {public int phase;public string name;public Sample sample;}
        [Serializable] private sealed class Witness {public string actor;public bool local;public int drawnSamples,descendingSamples,heldSamples;public Sample minimum,heldMinimum;}
        [Serializable] private sealed class ContactSource {public string anatomy,renderSourceHash;public int schema,alphaMinimum;public string[] declaredShapes;}
        [Serializable] private sealed class Sample
        {
            public int stage,frame,drawnFrame;public float seconds,deltaTime,poseTime,drawnTime,predictedFootMinimum,minimumSolidClearance,solidTop;
            public string actor,anatomy,logicalPose,drawnClip,ladder,solidFloor;public bool local,climbing,activeDescending,hasSolidFloor,commandJump;
            public Vector2 body,velocity,command;public Vector2[] predictedFeet;public Vector3 visual,cameraCenter,cameraMinimum,cameraMaximum,fullEnvelopeMinimum,fullEnvelopeMaximum;
            public float envelopeLeft,envelopeRight,envelopeBottom,envelopeTop;
        }
        [Serializable] private sealed class Report
        {
            public bool success,repeatNearTop,shortAscended,shortLanded,highAscended,highLanded;
            public string detail,context,role,observer,world,level,ladderId,highRouteReason,limit,failureCaptureError;
            public int width,height,peers,movementCommands,upCommands,downCommands,shortDescendingSamples,highDescendingSamples,mountSequences;
            public float lowerY,shortY,highY,contactTolerance;public Vector2 start,authoredSpawn,end;public RoomLadder ladder;public RoomSurface lowerSupport;
            public ContactSource[] contactSources;
            public Witness[] witnesses;public Capture[] captures;public Barrier[] barriers;public Sample[] trace;
        }
    }
}
