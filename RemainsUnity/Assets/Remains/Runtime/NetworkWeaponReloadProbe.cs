using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct WeaponReloadProbeReady : NetworkMessage {public string actor,world;}
    public struct WeaponReloadProbeBegin : NetworkMessage {public string source,owner,world;public int generation,lifecycle;}
    public struct WeaponReloadProbeStage : NetworkMessage {public int phase;public ReloadSoundMessage reload;}
    public struct WeaponReloadProbeAck : NetworkMessage {public int phase;public string actor,token;}
    public struct WeaponReloadProbeResume : NetworkMessage {public int phase;}
    public struct WeaponReloadProbeManifest : NetworkMessage {public string json;}
    public struct WeaponReloadProbeDone : NetworkMessage {public bool success;public string detail;}

    // Actual ordinary reload/cancel with a normal manual pause barrier for real renderer captures.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkWeaponReloadProbe : MonoBehaviour
    {
        [Serializable] public sealed class Phase
        {
            public int phase,roundsBefore,roundsAfter,reserveBefore,reserveAfter,usesBefore,usesAfter;
            public string weapon,item,token,shot;public long selection;public bool completed;
        }
        [Serializable] public sealed class Capture
        {
            public int phase,frame,frameCount,generation,lifecycle,unityFrame,width=1920,height=1080;
            public string source,owner,weapon,item,token,sprite,path;public long selection;
            public float progress,muzzleX,muzzleY,actualMuzzleX,actualMuzzleY,muzzleError,pixelsPerUnit,pivotX,pivotY;
            public bool visible,paused;
        }
        [Serializable] public sealed class Manifest
        {public int schema=1,generation,lifecycle,shots;public string world,source,owner;public float healthBefore,healthAfter;public Phase[] phases;}
        [Serializable] public sealed class Readiness
        {
            public string reason,actor,item,weapon,obstacle;
            public int phase,commandCalls,fireRequests,reloadRequests,stableFrames;
            public float actorX,actorY,lift,raiseHeight,angle,reach,aimAlignment,anchorX,anchorY,muzzleX,muzzleY,
                obstacleX,obstacleY,obstacleWidth,obstacleHeight,cooldown,remaining;
            public bool grounded,climbing,crouched,paused,preparing,uiBlocked,travelPending,ammoReady,muzzleClear;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1,participants,frameRate,receivedStarts,receivedStops;
            public bool success,source,characterSaved,originalRuntimeVerified;
            public string detail,actor,world,manifestHash,frameDirectory,saveDirectory;
            public Manifest evidence;public Capture[] captures;
            public Readiness readiness;
            public string limitation="Обычные player reload/cancel, paused captures и действительные реплики Unity. NPC state и все82frames отдельно проверяются Editor suite; original runtime не проверен.";
        }
        private PrototypeRuntime runtime;
        private InteractionVisuals visuals;
        private string source,owner,world,manifestHash,pendingManifest,pendingReceivedManifest;
        private int generation,lifecycle,phase,serverPhase=-1,resumePhase=-1,shots,starts,stops,captureAfterFrame;
        private float deadline,healthBefore;
        private float beginTime,preparationTime,firstFireTime,firstReloadTime;
        private int preparingPhase=-1,stableFrames,commandCalls,fireRequests,reloadRequests;
        private readonly Readiness readiness=new();
        private bool readySent,begun,serverBegun,finished,manifestSent,manifestAck,pauseRequested;
        private int resumedServerPhase=-1;
        private Phase current;
        private WeaponReloadProbeStage? pendingStage;
        private Manifest evidence;
        private readonly List<Phase> phases=new();
        private readonly List<Capture> captures=new();
        private readonly HashSet<int> ready=new(),capturedPeers=new(),finishedPeers=new();
        private readonly Dictionary<string,ReloadSoundMessage> observed=new(StringComparer.Ordinal);
        private readonly HashSet<string> ended=new(StringComparer.Ordinal);
        private readonly Dictionary<string,WeaponAsset> assets=new(StringComparer.Ordinal);
        private readonly Dictionary<string,string> attacks=new(StringComparer.Ordinal);
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Session.LocalActorId==source;
        private ActorState Actor=>runtime.Session.LocalActor;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+110;
            Require(value.Options.Smoke?.StartsWith("weapon-reload-frames-",StringComparison.Ordinal)==true && value.Options.ProbePeers is 1 or 2 &&
                !string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report) && value.Hud!=null,
                "Нужны weapon-reload-frames, отдельное сохранение, HUD и1–2 участника.");
            visuals=FindFirstObjectByType<InteractionVisuals>();Require(visuals?.ReloadAnimation!=null,"Нет сервиса изображения перезарядки.");
            foreach(string weapon in new[]{"pistol","energy"})
            {var asset=Resources.Load<WeaponAsset>("Content/weapons/"+weapon);Require(asset?.ReloadAnimation!=null,"Нет кадров перезарядки: "+weapon);assets.Add(weapon,asset);}
            value.CommandSource=Command;value.Session.ServerReady+=RegisterServer;value.Session.ClientReady+=RegisterClient;
            value.Session.ReloadAudio.Sent+=Observe;value.Session.ReloadAudio.Received+=Observe;value.Session.AttackReceived+=Attack;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<WeaponReloadProbeBegin>(message=>Guard(()=>Begin(message)));
            NetworkClient.RegisterHandler<WeaponReloadProbeStage>(message=>Guard(()=>
            {Require(begun && !pendingStage.HasValue && message.phase==captures.Count,"Повтор/непоследовательный capture stage.");pendingStage=message;captureAfterFrame=Time.frameCount+2;}));
            NetworkClient.RegisterHandler<WeaponReloadProbeResume>(message=>Guard(()=>
            {Require(message.phase==captures.Count-1,"Resume пришёл без настоящего capture.");resumePhase=message.phase;if(Source) runtime.Session.Pause.SetManual(false);}));
            NetworkClient.RegisterHandler<WeaponReloadProbeManifest>(message=>Guard(()=>
            {Require(evidence==null && pendingReceivedManifest==null,"Повтор manifest.");pendingReceivedManifest=message.json;}));
            NetworkClient.RegisterHandler<WeaponReloadProbeDone>(message=>End(message.success,message.detail));
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<WeaponReloadProbeReady>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                Require(actor?.id==message.actor && actor.level=="hub" && message.world==runtime.Session.ServerWorld.worldId,"Чужой ready.");
                ready.Add(connection.connectionId);if(ready.Count!=runtime.Options.ProbePeers || serverBegun)return;
                var players=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected).ToArray();
                Require(players.Length==runtime.Options.ProbePeers && players.All(a=>a.level=="hub" && !a.down),"Не все участники готовы в хабе.");
                var selected=players.Single(a=>runtime.Options.ProbePeers==1 || a.id!=runtime.Session.LocalActorId);serverBegun=true;
                NetworkServer.SendToAll(new WeaponReloadProbeBegin {source=selected.id,owner=selected.owner,world=message.world,generation=selected.generation,lifecycle=selected.lifecycleSequence});
            }));
            NetworkServer.RegisterHandler<WeaponReloadProbeStage>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                Require(actor?.id==source && SessionReloadAudio.Matches(actor,message.reload) && message.reload.started && message.phase==serverPhase+1 && message.phase<4,
                    "Capture stage не относится к подтверждённому источнику/предмету/epoch.");
                serverPhase=message.phase;capturedPeers.Clear();NetworkServer.SendToAll(message);
            }));
            NetworkServer.RegisterHandler<WeaponReloadProbeAck>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);Require(actor?.id==message.actor,"ACK другого участника.");
                if(message.phase==4)
                {
                    Require(evidence!=null && message.token==manifestHash,"ACK чужого manifest.");finishedPeers.Add(connection.connectionId);
                    if(finishedPeers.Count==runtime.Options.ProbePeers) NetworkServer.SendToAll(new WeaponReloadProbeDone {success=true,detail=$"4 фактические перезарядки, 2 завершения/2 отмены, {runtime.Options.ProbePeers*4} кадров участников; магазины и запас сохранены."});
                    return;
                }
                Require(message.phase==serverPhase && observed.TryGetValue(message.token,out var start) && start.actor==source,"ACK чужого capture token.");
                capturedPeers.Add(connection.connectionId);
                if(capturedPeers.Count==runtime.Options.ProbePeers && resumedServerPhase!=message.phase)
                {resumedServerPhase=message.phase;NetworkServer.SendToAll(new WeaponReloadProbeResume {phase=message.phase});}
            }));
            NetworkServer.RegisterHandler<WeaponReloadProbeManifest>((connection,message)=>Guard(()=>
            {Require(runtime.Session.ConnectedActor(connection)?.id==source && pendingManifest==null,"Чужой/повторный manifest.");pendingManifest=message.json;}));
            NetworkServer.RegisterHandler<WeaponReloadProbeDone>((connection,message)=>
            {if(!message.success && runtime.Session.ConnectedActor(connection)!=null) Fail(message.detail);});
        }
        private void Begin(WeaponReloadProbeBegin message)
        {
            Require(!begun && Actor!=null && message.world==runtime.Session.World.worldId,"Повтор/чужой begin.");
            source=message.source;owner=message.owner;world=message.world;generation=message.generation;lifecycle=message.lifecycle;begun=true;
            beginTime=Time.unscaledTime;
            if(Source)
            {
                healthBefore=Actor.health;Require(Actor.inventoryUses.Count==0,"Персонаж не новый.");
                foreach(string weapon in new[]{"pistol","energy"})
                {var item=Item(Actor,weapon);var definition=WeaponCatalog.Get(weapon);Require(item.weaponState.rounds==definition.magazineSize && InventoryJournal.Count(Actor,definition.ammo)==3*definition.magazineSize,"Нет обычного стартового боезапаса.");}
            }
        }
        private static ItemStack Item(ActorState actor,string weapon)=>actor.inventory.Single(i=>i.definition==weapon && i.weaponState!=null);
        private PlayerCommand Command()
        {
            commandCalls++;
            if(finished || !begun || !Source || phase>=4 || Actor==null)return default;
            try
            {
                Require(Actor.level=="hub" && Actor.owner==owner && Actor.generation==generation && Actor.lifecycleSequence==lifecycle && !Actor.down && Actor.health==healthBefore,"Источник потерял исходное тело/контекст.");
                string weapon=phase<2?"pistol":"energy";var item=Item(Actor,weapon);
                var command=new PlayerCommand {hasAim=true,aim=WeaponPose.RangedAnchor(Actor)+new Vector2(20,4)};
                if(current==null)
                {
                    if(preparingPhase!=phase)
                    {preparingPhase=phase;preparationTime=Time.unscaledTime;stableFrames=0;firstFireTime=firstReloadTime=0;}
                    if(Actor.equippedWeaponItem!=item.id){command.weaponItem=item.id;return command;}
                    if(!PrepareShot(item))
                    {if(Time.unscaledTime-preparationTime>8) Fail("Не подготовлен обычный первый выстрел: "+ReadinessDetail());return command;}
                    current=new Phase {phase=phase,weapon=weapon,item=item.id,completed=phase%2==0,roundsBefore=item.weaponState.rounds,
                        reserveBefore=InventoryJournal.Count(Actor,WeaponCatalog.Get(weapon).ammo),usesBefore=Actor.inventoryUses.Count,selection=Actor.weaponSelectionRevision};
                }
                if(current.token==null)
                {
                    if(item.weaponState.rounds==current.roundsBefore)
                    {
                        PrepareShot(item);command.fire=true;fireRequests++;
                        if(firstFireTime==0)firstFireTime=Time.unscaledTime;
                        if(Time.unscaledTime-firstFireTime>8) Fail("Обычная команда fire не дала actual AttackReceived/расход: "+ReadinessDetail());
                    }
                    else
                    {
                        Require(item.weaponState.rounds==current.roundsBefore-1,"Лишний расход до reload.");command.reload=true;reloadRequests++;
                        if(firstReloadTime==0)firstReloadTime=Time.unscaledTime;
                        if(Time.unscaledTime-firstReloadTime>8) Fail("Обычная команда reload не дала actual start: "+ReadinessDetail());
                    }
                }
                else if(resumePhase==phase && !runtime.Session.Pause.IsPaused("hub") && !current.completed && runtime.Combat.Reload.Active)
                    command.weaponItem=Item(Actor,weapon=="pistol"?"energy":"pistol").id;
                return command;
            }
            catch(Exception error){Fail(error.Message);return default;}
        }
        private bool PrepareShot(ItemStack item)
        {
            var actor=Actor;var room=runtime.Geometry.Get(actor.level);var anchor=WeaponPose.RangedAnchor(actor);var muzzle=WeaponPose.Muzzle(actor);
            var contact=ShotObstacles.FirstContact(room,anchor,muzzle,out var surface);
            readiness.phase=phase;readiness.actor=actor.id;readiness.item=item.id;readiness.weapon=actor.weapon;
            readiness.actorX=actor.x;readiness.actorY=actor.y;readiness.lift=actor.weaponLift;readiness.raiseHeight=WeaponRigs.RaiseHeight;
            readiness.angle=actor.weaponAngle;readiness.reach=actor.weaponReach;readiness.anchorX=anchor.x;readiness.anchorY=anchor.y;
            readiness.aimAlignment=Vector2.Dot(WeaponPose.Direction(actor),(anchor+new Vector2(20,4)-muzzle).normalized);
            readiness.muzzleX=muzzle.x;readiness.muzzleY=muzzle.y;readiness.obstacle=surface?.texture;
            readiness.obstacleX=surface?.x??0;readiness.obstacleY=surface?.y??0;
            readiness.obstacleWidth=surface?.width??0;readiness.obstacleHeight=surface?.height??0;
            readiness.muzzleClear=!contact.HasValue;readiness.climbing=actor.climbing;readiness.crouched=actor.crouched;
            readiness.grounded=ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position);
            readiness.paused=runtime.Session.Pause.IsPaused(actor.level);readiness.preparing=runtime.IsLevelPreparing;
            readiness.uiBlocked=runtime.Hud.BlocksGameplayInput;readiness.travelPending=runtime.Session.Travel.Pending;
            readiness.cooldown=runtime.Combat.ShotCooldownRemaining;readiness.remaining=runtime.Combat.Reload.Remaining;
            readiness.ammoReady=WeaponAmmunition.CanSpendShot(actor,WeaponCatalog.Get(item.definition),out string ammoReason);
            string reason=readiness.preparing?"локация готовится":readiness.uiBlocked?"UI блокирует команды":readiness.travelPending?"переход ожидает подтверждения":
                readiness.paused?"уровень на паузе":actor.equippedWeaponItem!=item.id?"выбран другой экземпляр":
                readiness.aimAlignment<.999999f?"обычная команда aim ещё не установила направление":contact.HasValue?"реальное дуло перекрыто":
                !readiness.ammoReady?ammoReason:readiness.cooldown>0?"cooldown ещё не завершён":runtime.Combat.Reload.Active?"перезарядка уже активна":null;
            if(reason==null)stableFrames++;else stableFrames=0;
            readiness.stableFrames=stableFrames;readiness.reason=reason??(stableFrames<2?"ожидание второго стабильного кадра":"готово");
            readiness.commandCalls=commandCalls;readiness.fireRequests=fireRequests;readiness.reloadRequests=reloadRequests;
            return reason==null && stableFrames>=2;
        }
        private string ReadinessDetail()
        {
            readiness.commandCalls=commandCalls;readiness.fireRequests=fireRequests;readiness.reloadRequests=reloadRequests;
            return StateJson.ToJson(readiness);
        }
        private void Attack(AttackEvent attack)
        {
            if(!begun || attack.attacker!=source)return;
            Guard(()=>
            {
                Require(attack.level=="hub" && attack.sourceOwner==owner && attack.weapon is "pistol" or "energy" && attacks.TryAdd(attack.id,attack.weapon),"Чужой/повторный actual shot.");
                if(Source){Require(current!=null && current.token==null && current.shot==null && attack.weapon==current.weapon,"Лишний выстрел во время reload.");current.shot=attack.id;}
                shots++;Require(shots<=4,"Более4 actual shots.");
            });
        }
        private void Observe(ReloadSoundMessage message)
        {
            if(finished || !begun || message.actor!=source)return;
            Guard(()=>
            {
                if(!message.started){Require(ended.Add(message.token),"Повтор stop.");stops++;return;}
                Require(observed.TryAdd(message.token,message),"Повтор start.");starts++;
                if(Source)
                {
                    Require(current!=null && current.token==null && message.weapon==current.weapon && message.weaponItem==current.item && message.selectionRevision==current.selection,"Start другого current phase.");
                    current.token=message.token;NetworkClient.Send(new WeaponReloadProbeStage {phase=phase,reload=message});
                }
            });
        }
        private void Update()=>Guard(Step);
        private void Step()
        {
            if(finished)return;
            if(Time.unscaledTime>deadline){Fail($"Таймаут reload: phase={phase}, captures={captures.Count}, starts/stops={starts}/{stops}, resume={resumePhase}; {ReadinessDetail()}");return;}
            if(Actor==null || !NetworkClient.isConnected)return;
            if(!readySent && Actor.level=="hub" && !runtime.IsLevelPreparing && runtime.ActiveRoomLighting is {IsReady:true,IsUpdating:false} &&
                runtime.Session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {readySent=true;NetworkClient.Send(new WeaponReloadProbeReady {actor=Actor.id,world=runtime.Session.World.worldId});}
            if(Source && begun && phase==0 && current==null && commandCalls==0 && Time.unscaledTime-beginTime>5)
            {PrepareShot(Item(Actor,"pistol"));Fail("Runtime не вызывает CommandSource для weapon-reload smoke: "+ReadinessDetail());return;}
            if(Source && current?.token!=null && !pauseRequested && runtime.Combat.Reload.Active &&
                WeaponReloadAnimation.Progress(WeaponCatalog.Get(current.weapon).reloadSeconds,runtime.Combat.Reload.Remaining,"native source")>=.35f)
            {pauseRequested=true;runtime.Session.Pause.SetManual(true);}
            if(Source && current?.token!=null && resumePhase==phase && !runtime.Session.Pause.IsPaused("hub") && !runtime.Combat.Reload.Active)
            {
                Require(ended.Contains(current.token) && current.shot!=null,"Нет confirmed shot/stop после actual reload.");var item=Item(Actor,current.weapon);
                current.roundsAfter=item.weaponState.rounds;current.reserveAfter=InventoryJournal.Count(Actor,WeaponCatalog.Get(current.weapon).ammo);current.usesAfter=Actor.inventoryUses.Count;
                ValidatePhase(current);phases.Add(current);current=null;pauseRequested=false;phase++;
            }
            if(Source && phase==4 && !manifestSent)
            {
                manifestSent=true;var manifest=new Manifest {world=world,source=source,owner=owner,generation=generation,lifecycle=lifecycle,shots=shots,
                    healthBefore=healthBefore,healthAfter=Actor.health,phases=phases.ToArray()};Validate(manifest);
                NetworkClient.Send(new WeaponReloadProbeManifest {json=StateJson.ToJson(manifest)});
            }
            if(Host && pendingManifest!=null && shots==4 && starts==4 && stops==4)
            {
                var manifest=StateJson.FromJson<Manifest>(pendingManifest);Validate(manifest);var body=runtime.Session.ServerWorld.Find(source);
                if(body!=null && manifest.phases.Where(p=>p.phase%2==1).All(p=>Item(body,p.weapon).weaponState.rounds==p.roundsAfter && InventoryJournal.Count(body,WeaponCatalog.Get(p.weapon).ammo)==p.reserveAfter) && body.inventoryUses.Count==2)
                {string json=pendingManifest;pendingManifest=null;NetworkServer.SendToAll(new WeaponReloadProbeManifest {json=json});}
            }
            if(pendingReceivedManifest!=null && shots==4 && starts==4 && stops==4)
            {
                var manifest=StateJson.FromJson<Manifest>(pendingReceivedManifest);Validate(manifest);
                Require(manifest.phases.All(p=>observed.ContainsKey(p.token) && ended.Contains(p.token)),"Нет actual received tokens из server manifest.");
                evidence=manifest;manifestHash=Hash(pendingReceivedManifest);
                File.WriteAllText(runtime.Options.Report+".manifest.json",pendingReceivedManifest,new UTF8Encoding(false));pendingReceivedManifest=null;
            }
            if(evidence!=null && !manifestAck && starts==4 && stops==4 && shots==4 && captures.Count==4 &&
                evidence.phases.All(p=>observed.ContainsKey(p.token) && ended.Contains(p.token)))
            {manifestAck=true;NetworkClient.Send(new WeaponReloadProbeAck {phase=4,actor=Actor.id,token=manifestHash});}
        }
        private void LateUpdate()=>Guard(()=>
        {
            if(finished || !pendingStage.HasValue || Time.frameCount<captureAfterFrame || Actor==null || !runtime.Session.Pause.IsPaused("hub"))return;
            var stage=pendingStage.Value;var body=runtime.Session.World.Find(source);
            if(body==null || !observed.ContainsKey(stage.reload.token) || !visuals.ReloadAnimation.TryGet(body,out var state))return;
            Require(state.Token==stage.reload.token && state.Item==stage.reload.weaponItem && state.Selection==stage.reload.selectionRevision && state.Generation==generation && state.Lifecycle==lifecycle,"Captured binding не принадлежит stage.");
            var view=visuals.FindWeapon(source);var asset=assets[state.Weapon];
            Require(view!=null && view.gameObject.activeInHierarchy && view.Artwork.enabled && view.Artwork.sprite==asset.ReloadAnimation.Frame(state.Frame) && view.Muzzle!=null,"Нет actual reload renderer кадра.");
            Vector2 actual=view.Muzzle.position,expected=WeaponPose.Muzzle(body);float error=Vector2.Distance(actual,expected);
            Require(error<=.0001f && Camera.main!=null,"Actual reload muzzle/камера не подтверждены.");
            string path=runtime.Options.Report+".reload-"+stage.phase+".png";
            NetworkFrameCapture.Save(Camera.main,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);
            captures.Add(new Capture {phase=stage.phase,source=source,owner=body.owner,weapon=state.Weapon,item=state.Item,token=state.Token,selection=state.Selection,
                generation=state.Generation,lifecycle=state.Lifecycle,frame=state.Frame,frameCount=state.FrameCount,progress=state.Progress,
                sprite=view.Artwork.sprite.name,pixelsPerUnit=view.Artwork.sprite.pixelsPerUnit,pivotX=view.Artwork.sprite.pivot.x,pivotY=view.Artwork.sprite.pivot.y,
                muzzleX=expected.x,muzzleY=expected.y,actualMuzzleX=actual.x,actualMuzzleY=actual.y,muzzleError=error,visible=true,paused=true,unityFrame=Time.frameCount,path=path});
            pendingStage=null;NetworkClient.Send(new WeaponReloadProbeAck {phase=stage.phase,actor=Actor.id,token=state.Token});
        });
        private void ValidatePhase(Phase p)
        {
            int spent=p.completed?1:0;
            Require(p.phase>=0 && p.phase<4 && p.weapon==(p.phase<2?"pistol":"energy") && p.completed==(p.phase%2==0) && p.item!=null && p.token!=null &&
                p.shot!=null && attacks.TryGetValue(p.shot,out string actualWeapon) && actualWeapon==p.weapon && p.roundsBefore==WeaponCatalog.Get(p.weapon).magazineSize &&
                p.roundsAfter==(p.completed?WeaponCatalog.Get(p.weapon).magazineSize:p.roundsBefore-1) && p.reserveBefore-p.reserveAfter==spent && p.usesAfter-p.usesBefore==spent,
                "Finish/cancel расходится с настоящим расходом: phase="+p.phase);
        }
        private void Validate(Manifest value)
        {
            Require(value?.schema==1 && value.world==world && value.source==source && value.owner==owner && value.generation==generation && value.lifecycle==lifecycle &&
                value.shots==4 && value.phases?.Length==4 && value.healthBefore==value.healthAfter && value.healthBefore>0,"Manifest потерял source/health/4phases.");
            Require(value.phases.Select(p=>p.token).Distinct(StringComparer.Ordinal).Count()==4,"Повтор phase token.");
            Require(value.phases.Select(p=>p.shot).Distinct(StringComparer.Ordinal).Count()==4,"Повтор actual phase shot.");
            for(int i=0;i<4;i++){Require(value.phases[i].phase==i,"Переставлены phases.");ValidatePhase(value.phases[i]);}
        }
        private static string Hash(string text){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        private void Guard(Action action){if(finished)return;try{action();}catch(Exception error){Fail(error.Message);}}
        private void Fail(string reason)
        {
            if(finished)return;
            if(NetworkServer.active)NetworkServer.SendToAll(new WeaponReloadProbeDone {success=false,detail=reason});
            else if(NetworkClient.isConnected)NetworkClient.Send(new WeaponReloadProbeDone {success=false,detail=reason});
            End(false,reason);
        }
        private void End(bool success,string detail)
        {
            if(finished)return;finished=true;bool saved=false;
            try
            {
                if(success)
                {
                    Require(manifestAck && captures.Count==4 && evidence!=null,"Finish до actual peer evidence.");
                    runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();var character=runtime.Session.Saves.LoadCharacter();
                    Require(character?.id==Actor.id,"На диске другой персонаж.");
                    if(Source)foreach(var p in evidence.phases.Where(p=>p.phase%2==1))
                        Require(Item(character,p.weapon).id==p.item && Item(character,p.weapon).weaponState.rounds==p.roundsAfter && InventoryJournal.Count(character,WeaponCatalog.Get(p.weapon).ammo)==p.reserveAfter && character.inventoryUses.Count==2,"Диск потерял actual расход/экземпляр.");
                    saved=true;
                }
            }
            catch(Exception error){success=false;detail+=" "+error.Message;}
            ReadinessDetail();
            var report=new Report {success=success,detail=detail,source=Source,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,
                actor=Actor?.id,world=world,evidence=evidence,captures=captures.ToArray(),receivedStarts=starts,receivedStops=stops,manifestHash=manifestHash,
                characterSaved=saved,saveDirectory=runtime.Options.SaveDirectory,frameDirectory=Path.GetDirectoryName(runtime.Options.Report),readiness=readiness};
            try{File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true),new UTF8Encoding(false));}
            catch(Exception error){success=false;Debug.LogError("Не записан reload report: "+error.Message);}
            if(success)Debug.Log("NetworkWeaponReloadProbe PASS: "+detail);else Debug.LogError("NetworkWeaponReloadProbe FAIL: "+detail);
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success){yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        private void OnDestroy()
        {
            if(runtime?.Session!=null)
            {runtime.Session.ServerReady-=RegisterServer;runtime.Session.ClientReady-=RegisterClient;runtime.Session.ReloadAudio.Sent-=Observe;runtime.Session.ReloadAudio.Received-=Observe;runtime.Session.AttackReceived-=Attack;}
        }
    }
}
