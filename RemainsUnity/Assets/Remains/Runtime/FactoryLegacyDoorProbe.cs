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
    public struct LegacyDoorFixtureMessage : NetworkMessage {public string json;}
    public struct LegacyDoorPhaseMessage : NetworkMessage {public int phase;}
    public struct LegacyDoorWitnessMessage : NetworkMessage {public string actor,evidence;public int generation,phase;}
    public struct LegacyDoorFinishMessage : NetworkMessage {public bool success;public string detail,snapshot,door,evidence;}
    public struct LegacyDoorDurableMessage : NetworkMessage {public string actor,snapshot;public int generation;}
    public struct LegacyDoorExitMessage : NetworkMessage { }

    // Only the initial fresh player positions are diagnostic fixtures. No door,
    // NPC, terrain, stocks, HP or damage formula is authored by this observer.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryLegacyDoorProbe : MonoBehaviour
    {
        public const string DoorId="factory:0:original:plant:18:JLgxVHRV2bDR9qHK";
        private const string Level="factory:0";
        [Serializable] public sealed class PlayerFixture
        {public string id,owner;public int generation,lifecycle,threats,blockedRays;public float x,y,health;}
        [Serializable] public sealed class Fixture
        {public string world,driver,initialDoorJson;public bool reload;public PlayerFixture[] players;public int initialRounds,initialReserve;public string item;}
        [Serializable] public sealed class Voice
        {public string state,clip;public int frame,nativeId,samples;public bool playing,muted;public float volume;}
        [Serializable] public sealed class Frame
        {public int phase,frame,width=1920,height=1080;public string state,path,scene,sprite,texture;public float pixelsPerUnit,pivotX,pivotY,rectWidth,rectHeight;public bool visible,presentationReady;public Vector3 minimum,maximum;public Vector3[] viewport;public DoorState door;public float actorX,actorY;}
        [Serializable] public sealed class Evidence
        {public string actor,item;public int initialRounds,initialReserve,finalRounds,finalReserve,interactions,damageReplies;public float traverseX;public AttackEvent[] launches;}
        [Serializable] public sealed class Receipt
        {public string snapshot,world,expectedProfileJson,persistedProfileJson;public long revision;public double preparedAt,persistedAt;public ActorState player;}
        [Serializable] public sealed class Report
        {
            public int schema=1,participants,frameRate,phase,captures;
            public bool success,reload,explicitInitialPositionFixture,stockOverrides,originalRuntimeVerified,geometryVerified,traversed,
                replicaVerified,archiveVerified,receiptCharacterVerified,canonicalAcknowledged,reinstallVerified,sourceBodyPreserved;
            public string detail,actor,world,door=DoorId,snapshot,snapshotArchive,saveDirectory,sourceSnapshot,sourceArchive,sourceArchiveSha256;
            public Fixture fixture;public DoorState result;public Evidence evidence;public Receipt receipt;public Frame[] frames;public Voice[] voices;
            public ActorState terminalPlayer,sourceBody;
            public string limitation="Девять исходных ID/HP проверяются отдельными Editor contracts; ordinary representative только деревянная door1/60HP. Начальная fresh позиция явно задана на настоящем полу, NPC/HP/запасы не заменены. Ресурсы и события muted голоса не доказывают звучание оригинала или threshold/замки. Reload не выполняет повторное разрушение.";
        }
        private PrototypeRuntime runtime;private DoorVisuals visuals;private Fixture fixture;private PlayerFixture baseline;
        private bool reload,traveled,installed,active,done,acted,finishPending,durableStarted,durable,commitStarted,canonicalPersisted;
        private int phase,reported=-1,captured=-1,interactions,damageReplies,commandFrame,preparedFrame;
        private float deadline,phaseAt,nextShot;private string lastNotice,finishSnapshot,finishDoor,finishEvidence,archivePath;
        private string sourceArchive,sourceSnapshot,sourceHash;private WorldState sourceWorld;
        private readonly Dictionary<string,AttackEvent> launches=new(StringComparer.Ordinal);
        private readonly Dictionary<string,Receipt> receipts=new(StringComparer.Ordinal);
        private readonly HashSet<string> witnesses=new(StringComparer.Ordinal),durablePeers=new(StringComparer.Ordinal);
        private readonly HashSet<int> acknowledgements=new(),voiceIds=new();
        private readonly List<Voice> voices=new();private readonly List<Frame> frames=new();
        private bool previousOpen,previousDestroyed;
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Driver=>fixture==null?null:runtime.Session.World?.Find(fixture.driver);
        private DoorState Door=>runtime.Session.World?.doors.Find(d=>d.id==DoorId);
        private bool Host=>runtime.Options.Role=="host";
        private bool IsDriver=>fixture!=null && Actor?.id==fixture.driver;
        private string Progress=>$"phase={phase}, actor={Actor?.id}/{Actor?.Position}/HP{Actor?.health}, door={Door?.open}/{Door?.integrity?.health}, shots={launches.Count}, frames={frames.Count}, peers={witnesses.Count}, notice={lastNotice}";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+160;reload=value.Options.Smoke?.StartsWith("legacy-doors-reload-frames-",StringComparison.Ordinal)==true;
            Require(value.Options.Smoke?.StartsWith("legacy-doors-",StringComparison.Ordinal)==true && value.Options.Smoke.Contains("-frames-") && value.Options.ProbePeers is 1 or 2 && value.Hud!=null && !string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report),"Нужны legacy-doors-frames-, HUD,1–2 peers и изолированные saves.");
            visuals=FindFirstObjectByType<DoorVisuals>();Require(visuals!=null,"Нет actual DoorVisuals.");
            if(reload)
            {
                var source=value.Session.Saves.SelectedSnapshot();Require(source!=null,"Reload не создаёт отсутствующий мир.");
                sourceSnapshot=source.Id;sourceWorld=source.CreateLiveWorld();sourceArchive=Archive(source.WorldId,source.Id);
                Require(File.Exists(sourceArchive),"Нет исходного immutable архива.");sourceHash=Hash(File.ReadAllBytes(sourceArchive));
                var door=sourceWorld.doors.Single(d=>d.id==DoorId);Require(door.Destroyed && door.integrity.maxHealth==60 && door.integrity.resolvedAttacks.Count>0,"Reload требует actual сохранённое разрушение60HP.");
            }
            else if(Host) {Require(value.Session.Saves.SelectedSnapshot()==null,"Fresh не заменяет существующий мир.");value.Session.Saves.SaveWorld(NewGameWorld.Create());}
            value.CommandSource=Command;
            value.Session.ClientReady+=ClientHandlers;value.Session.ServerReady+=ServerHandlers;
            value.Session.AttackReceived+=Attack;value.Session.Changed+=Sound;value.Session.Notice+=Notice;
            value.Session.Doors.Finished+=Interacted;value.Session.DoorDamage.Finished+=Damaged;
            value.Session.RecoveryReceiptPrepared+=ReceiptPrepared;value.Session.RecoveryReceiptPersisted+=ReceiptPersisted;
            value.Session.RecoveryPersisted+=CanonicalPersisted;value.Session.RecoveryAcknowledged+=Acknowledged;
        }
        private void ClientHandlers()
        {
            NetworkClient.RegisterHandler<LegacyDoorFixtureMessage>(m=>Guard(()=>
            {Require(fixture==null,"Повтор fixture.");fixture=StateJson.FromJson<Fixture>(m.json);Require(fixture.world==runtime.Session.World.worldId && fixture.reload==reload,"Fixture другого мира/режима.");baseline=fixture.players.Single(p=>p.id==Actor.id);phaseAt=Time.unscaledTime;}));
            NetworkClient.RegisterHandler<LegacyDoorPhaseMessage>(m=>Guard(()=>
            {Require(m.phase==phase+1,"Пропуск фазового барьера.");phase=m.phase;phaseAt=Time.unscaledTime;acted=false;}));
            NetworkClient.RegisterHandler<LegacyDoorFinishMessage>(m=>Guard(()=>
            {if(!m.success){Finish(false,m.detail);return;}finishSnapshot=m.snapshot;finishDoor=m.door;finishEvidence=m.evidence;finishPending=true;}));
            NetworkClient.RegisterHandler<LegacyDoorExitMessage>(_=>Guard(()=>Finish(true,"Actual E/fire/die/проход либо reload + immutable world/character receipt/ACK подтверждены.")));
        }
        private void ServerHandlers()
        {
            NetworkServer.RegisterHandler<LegacyDoorWitnessMessage>((c,m)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(c);Require(actor!=null && actor.id==m.actor && actor.generation==m.generation && fixture.players.Any(p=>p.id==actor.id && p.owner==actor.owner && p.generation==actor.generation),"Witness чужой эпохи/connection.");
                if(m.phase!=phase)return;
                if(m.phase==4 && actor.id==fixture.driver) {Require(!string.IsNullOrEmpty(m.evidence),"Нет расхода/запусков driver.");finishEvidence=m.evidence;}
                witnesses.Add(actor.id);
            }));
            NetworkServer.RegisterHandler<LegacyDoorDurableMessage>((c,m)=>Guard(()=>
            {var actor=runtime.Session.ConnectedActor(c);Require(actor!=null && actor.id==m.actor && actor.generation==m.generation && m.snapshot==finishSnapshot,"Чужой durable witness.");durablePeers.Add(actor.id);}));
        }
        private void Notice(string value) {if(!done)lastNotice=value;}
        private void Interacted(Remains.Networking.DoorReply reply)
        {if(done || !active || !IsDriver)return;Guard(()=>{Require(reply.accepted,"E отклонён: "+reply.reason);interactions++;});}
        private void Damaged(Remains.Networking.DoorDamageReply reply)
        {if(done || !active || !IsDriver || reply.door!=DoorId)return;Guard(()=>{Require(reply.accepted,"Damage отклонён: "+reply.reason);damageReplies++;});}
        private void Attack(AttackEvent attack)
        {
            if(done || fixture==null || attack.attacker!=fixture.driver)return;
            Guard(()=>{Require(!reload && attack.weapon=="pistol" && attack.level==Level,"Чужое/неожиданное оружие.");
                if(launches.TryGetValue(attack.id,out var old))Require(StateJson.ToJson(old)==StateJson.ToJson(attack),"Launch ID изменён.");else launches.Add(attack.id,StateJson.Copy(attack));});
        }
        private void Sound()
        {
            if(done || !active || Door==null)return;
            Guard(()=>
            {
                if(previousOpen==Door.open && previousDestroyed==Door.Destroyed)return;
                string label=Door.Destroyed?"die":Door.open?"open":"close";var view=visuals.Find(DoorId);Require(view!=null,"Нет actual view перехода.");
                var clip=Door.Destroyed?view.Definition.DestroySound:Door.open?view.Definition.OpenSound:view.Definition.CloseSound;
                var voicesNow=runtime.Audio.GetComponentsInChildren<AudioSource>().Where(s=>s.clip==clip && !voiceIds.Contains(s.GetInstanceID())).ToArray();
                Require(voicesNow.Length==1,"Нет единственного actual голоса "+label);var voice=voicesNow[0];
                Require(voice.isPlaying && voice.clip.samples>0 && voice.volume>0,"Actual голос пуст/не играет.");voiceIds.Add(voice.GetInstanceID());
                voices.Add(new Voice {state=label,clip=clip.name,nativeId=voice.GetInstanceID(),frame=Time.frameCount,samples=clip.samples,playing=voice.isPlaying,muted=voice.mute,volume=voice.volume});
                previousOpen=Door.open;previousDestroyed=Door.Destroyed;
            });
        }
        private PlayerCommand Command()
        {
            if(done || !active || !IsDriver || reload || finishPending || runtime.IsLevelPreparing)return default;
            try
            {
                commandFrame=Time.frameCount;var command=new PlayerCommand {hasAim=true,aim=Door.Bounds.center};
                if(phase is 1 or 2 && !acted)
                {var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,command.aim);Require(selected.kind==InteractionKind.Door && selected.id==DoorId,"E выбрал иной объект.");acted=true;command.interact=true;}
                if(phase==3 && !Door.Destroyed)
                {
                    var item=Pistol(Actor);if(Actor.equippedWeaponItem!=item.id){command.weaponItem=item.id;preparedFrame=Time.frameCount+3;return command;}
                    if(Time.frameCount<preparedFrame || Time.unscaledTime<nextShot || runtime.Combat.Reload.Active)return command;
                    Require(item.weaponState.rounds>0,"Обычный магазин исчерпан до60HP die.");RequireLaunch(Actor,runtime.Geometry.Get(Level));command.fire=true;nextShot=Time.unscaledTime+.8f;
                }
                if(phase==4 && 47.95f-Actor.x>.035f) {command.movement=Vector2.right*.2f;command.aim=Actor.BodyPosition+Vector2.right*5;}
                return command;
            }
            catch(Exception error){Fail(error.Message);return default;}
        }
        private void Update()=>Guard(Step);
        private void Step()
        {
            Require(Time.unscaledTime<=deadline,"Таймаут: "+Progress);
            if(Actor==null || !NetworkClient.isConnected || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers)return;
            if(!traveled){traveled=true;if(!reload)runtime.Session.Travel.RequestFactory();}
            if(Host && !installed && !runtime.Session.Travel.Pending && runtime.LevelPresentationReady && runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level))Setup();
            if(fixture==null || Actor.generation!=baseline.generation)return;
            Require(Actor.id==baseline.id && Actor.owner==baseline.owner && Actor.lifecycleSequence==baseline.lifecycle && Actor.level==Level && Actor.connected && !Actor.down && Actor.health>0,"Потеря actual player lifetime: "+Progress);
            if(!runtime.LevelPresentationReady || runtime.IsLevelPreparing || runtime.Session.Travel.Pending)return;
            if(!active){if(!Supported(Actor))return;active=true;previousOpen=Door.open;previousDestroyed=Door.Destroyed;phaseAt=Time.unscaledTime;}
            Require(runtime.Geometry.Get(Level).platforms.Any(s=>s.sourceDoorId==DoorId)==Door.Blocks,"Geometry не читает actual door Blocks.");
            if(finishPending)
            {
                if(!durableStarted){durableStarted=true;StartCoroutine(AwaitDurable());}
                if(Host && durable && durablePeers.Count==runtime.Options.ProbePeers && canonicalPersisted && acknowledgements.Count==runtime.Options.ProbePeers)
                    NetworkServer.SendToAll(new LegacyDoorExitMessage());
                return;
            }
            if(Ready() && captured==phase && reported!=phase)
            {reported=phase;NetworkClient.Send(new LegacyDoorWitnessMessage {actor=Actor.id,generation=Actor.generation,phase=phase,evidence=phase==4 && IsDriver?StateJson.ToJson(BuildEvidence()):null});}
            if(Host && witnesses.Count==runtime.Options.ProbePeers)
            {
                witnesses.Clear();
                if(reload || phase==4)Complete();else NetworkServer.SendToAll(new LegacyDoorPhaseMessage {phase=phase+1});
            }
        }
        private bool Ready()
        {
            if(reload)return Door.Destroyed && Supported(Actor);
            return phase switch {0=>!Door.open && Door.integrity.health==60 && Door.revision==0,1=>Door.open && !Door.Destroyed && Door.revision==1,
                2=>!Door.open && !Door.Destroyed && Door.revision==2,3=>Door.Destroyed && !Door.Blocks && !Door.CanInteract,
                4=>Driver!=null && Mathf.Abs(Driver.x-47.95f)<=.06f && Mathf.Abs(Driver.vx)<.01f && Supported(Driver),_=>false};
        }
        private void Setup()
        {
            var world=runtime.Session.ServerWorld;Require(WorldRooms.Definition(world,Level)==NewGameWorld.FactoryDefinitionId,"Нужен обычный мир, не legacy fixture.");
            var door=world.doors.Single(d=>d.id==DoorId);Require(door.definition=="door1" && door.integrity?.maxHealth==60 && Mathf.Abs(door.x-46.4f)<.0001f && Mathf.Abs(door.y-13.6f)<.0001f,"Не approved реальная деревянная дверь.");
            Require(reload?door.Destroyed:!door.open && door.integrity.health==60 && door.revision==0,"Начальное door состояние подменено.");
            var players=world.actors.Where(a=>a.player && a.connected).OrderBy(a=>a.id,StringComparer.Ordinal).ToArray();var room=runtime.Geometry.Get(Level);var positions=new List<PlayerFixture>();
            foreach(var player in players)
            {
                Vector2 position=reload?player.Position:new Vector2(positions.Count==0?44.9f:43.5f,13.6f);var hull=Anatomy.Movement(player);
                Require(ActorTerrainMotion.IsClear(room,hull,position) && ActorTerrainMotion.Supported(room,hull,position),"Fixture/reload не имеет actual свободной опоры: "+player.id+"/"+position);
                var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
                Require(!WorldProps.All(world).Any(p=>p.level==Level && p.hasPhysicalBody && Interior(body,new Rect(p.x-p.width*.5f,p.y-p.anchorHeight,p.width,p.height))) &&
                    !world.actors.Any(a=>!a.player && a.level==Level && Interior(body,InventorySource.BodyBounds(a))),"Fixture пересекает actual cargo/NPC.");
                var sample=player.Copy();sample.Position=position;
                if(!reload)
                {
                    Require(Cover(world,room,sample,out int threats,out int blocked),"Fixture не защищена actual ranged obstacles.");
                    if(positions.Count==0){Require(InteractionSelection.Select(world,sample,runtime.Session.Rules,door.Bounds.center).id==DoorId,"Нет exact E доступа.");WeaponInventory.Select(sample,Pistol(sample).id,out string reason);Require(reason==null,"Нет pistol: "+reason);WeaponPose.Aim(sample,door.Bounds.center,room);RequireLaunch(sample,room);}
                    player.Position=position;player.Velocity=Vector2.zero;player.climbing=player.crouched=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
                    positions.Add(new PlayerFixture {id=player.id,owner=player.owner,generation=player.generation,lifecycle=player.lifecycleSequence,x=position.x,y=position.y,health=player.health,threats=threats,blockedRays=blocked});
                }
                else
                {
                    var original=sourceWorld.Find(player.id);Require(original!=null && original.level==player.level && original.x==player.x && original.y==player.y && original.health==player.health && original.down==player.down && StateJson.ToJson(original.inventory)==StateJson.ToJson(player.inventory) && StateJson.ToJson(original.extensions)==StateJson.ToJson(player.extensions),"Reload изменил actual saved body/HP/inventory/unknown.");
                    positions.Add(new PlayerFixture {id=player.id,owner=player.owner,generation=player.generation,lifecycle=player.lifecycleSequence,x=player.x,y=player.y,health=player.health});
                }
            }
            var source=players[0];var pistol=Pistol(source);int reserve=InventoryJournal.Count(source,"p10");
            if(!reload)Require(pistol.weaponState.rounds==12 && reserve==36 && WeaponCatalog.Get("pistol").damage==11,"Не обычный12/36 старт и11damage.");
            var setup=new Fixture {world=world.worldId,driver=source.id,reload=reload,initialDoorJson=StateJson.ToJson(door),players=positions.ToArray(),item=pistol.id,initialRounds=pistol.weaponState.rounds,initialReserve=reserve};
            installed=true;runtime.Session.CommitWorld();NetworkServer.SendToAll(new LegacyDoorFixtureMessage {json=StateJson.ToJson(setup)});
        }
        private static bool Cover(WorldState world,RoomLayout room,ActorState actor,out int threats,out int blocked)
        {
            threats=blocked=0;var body=InventorySource.BodyBounds(actor);var points=new[]{body.center,new Vector2(body.xMin+.02f,body.yMin+.02f),new Vector2(body.xMax-.02f,body.yMin+.02f),new Vector2(body.xMin+.02f,body.yMax-.02f),new Vector2(body.xMax-.02f,body.yMax-.02f)};
            foreach(var npc in world.actors.Where(a=>!a.player && !a.down && a.health>0 && a.level==Level && !string.IsNullOrEmpty(a.weapon)))
            {if(WeaponCatalog.Get(npc.weapon).projectileSpeed<=0)continue;threats++;var aimed=npc.Copy();NpcWeaponAim.Apply(aimed,actor.BodyPosition+Vector2.up*.55f,room);foreach(var p in points){if(!ShotObstacles.FirstContact(room,WeaponPose.Muzzle(aimed),p,out var surface).HasValue || surface.oneWay || surface.shotsPassThrough)return false;blocked++;}}
            return threats>0;
        }
        private static bool Interior(Rect a,Rect b)=>a.xMax>b.xMin+.0001f && a.xMin<b.xMax-.0001f && a.yMax>b.yMin+.0001f && a.yMin<b.yMax-.0001f;
        private bool Supported(ActorState actor)=>!actor.climbing && Mathf.Abs(actor.vy)<.001f && Mathf.Abs(actor.y-13.6f)<.001f && ActorTerrainMotion.Supported(runtime.Geometry.Get(Level),Anatomy.Movement(actor),actor.Position);
        private static ItemStack Pistol(ActorState actor)=>actor.inventory.Single(i=>i.definition=="pistol" && i.weaponState!=null);
        private static void RequireLaunch(ActorState actor,RoomLayout room)
        {
            var origin=WeaponPose.Muzzle(actor);Require(!ShotObstacles.FirstContact(room,WeaponPose.RangedAnchor(actor),origin).HasValue,"Ствол внутри преграды.");
            var direction=WeaponPose.Direction(actor);float half=WeaponCatalog.Get("pistol").shotSpreadHalfAngleDegrees;
            foreach(float offset in new[]{-half,0,half}){float angle=Mathf.Atan2(direction.y,direction.x)+offset*Mathf.Deg2Rad;var end=origin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*WeaponCatalog.Get("pistol").range;Require(ShotObstacles.FirstContact(room,origin,end,out var surface).HasValue && surface.sourceDoorId==DoorId,"Actual spread не пересекает цель: "+offset);}
        }
        private void LateUpdate()=>Guard(()=>
        {
            if(!active || finishPending || captured==phase || !runtime.LevelPresentationReady || !Ready() || Time.unscaledTime-phaseAt<.35f)return;
            var view=visuals.Find(DoorId);var camera=Camera.main;Require(view!=null && camera!=null,"Нет actual renderer/camera.");
            var sprite=Door.Destroyed?view.Definition.Destroyed:Door.open?view.Definition.Opened:view.Definition.Closed;Require(view.Artwork.sprite==sprite,"Actual artwork не показывает canonical состояние.");
            var b=view.Artwork.bounds;float z=b.center.z;var corners=new[]{new Vector3(b.min.x,b.min.y,z),new Vector3(b.max.x,b.min.y,z),new Vector3(b.max.x,b.max.y,z),new Vector3(b.min.x,b.max.y,z)}.Select(camera.WorldToViewportPoint).ToArray();
            bool visible=view.Artwork.enabled && view.gameObject.activeInHierarchy && view.Artwork.color.a>0 && (camera.cullingMask&(1<<view.gameObject.layer))!=0 && corners.All(p=>p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1 && p.z>=camera.nearClipPlane && p.z<=camera.farClipPlane);
            if(!visible)return; // Bounded by the global deadline; production camera is never moved.
            string label=reload?"reloaded":phase switch {0=>"closed",1=>"open",2=>"closed-again",3=>"destroyed",4=>"traversed",_=>throw new InvalidOperationException("Неизвестная фаза.")};
            string path=runtime.Options.Report+".legacy-"+label+".png";NetworkFrameCapture.Save(camera,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);NetworkSceneRecord.Save(runtime,680+phase);
            var frame=new Frame {phase=phase,frame=Time.frameCount,state=label,path=path,scene=runtime.Options.Report+".phase-"+(680+phase)+".scene.json",sprite=sprite.name,texture=sprite.texture.name,pixelsPerUnit=sprite.pixelsPerUnit,pivotX=sprite.pivot.x,pivotY=sprite.pivot.y,rectWidth=sprite.rect.width,rectHeight=sprite.rect.height,visible=visible,presentationReady=runtime.LevelPresentationReady,minimum=b.min,maximum=b.max,viewport=corners,door=StateJson.Copy(Door),actorX=Actor.x,actorY=Actor.y};frames.Add(frame);captured=phase;File.WriteAllText(path+".json",StateJson.ToJson(frame,true));
        });
        private Evidence BuildEvidence()
        {
            var item=Pistol(Actor);var evidence=new Evidence {actor=Actor.id,item=item.id,initialRounds=fixture.initialRounds,initialReserve=fixture.initialReserve,finalRounds=item.weaponState.rounds,finalReserve=InventoryJournal.Count(Actor,"p10"),interactions=interactions,damageReplies=damageReplies,traverseX=Actor.x,launches=launches.Values.ToArray()};ValidateEvidence(evidence,Door);return evidence;
        }
        private void ValidateEvidence(Evidence evidence,DoorState door)
        {
            Require(evidence!=null && evidence.actor==fixture.driver && evidence.item==fixture.item && evidence.initialRounds==12 && evidence.initialReserve==36 && evidence.finalReserve==36 && evidence.interactions==2 && evidence.launches?.Length==6 && evidence.finalRounds==6 && evidence.damageReplies==6 && Mathf.Abs(evidence.traverseX-47.95f)<=.06f,"Не обычные два E/6pistolshots/расход/проход.");
            Require(door.Destroyed && door.integrity.maxHealth==60 && door.integrity.health==0 && door.integrity.resolvedAttacks.Count==6 && door.revision==8,"Destroyed door не следует из двух E/6damage.");
            foreach(var shot in evidence.launches)Require(launches.TryGetValue(shot.id,out var observed) && StateJson.ToJson(observed)==StateJson.ToJson(shot) && door.integrity.resolvedAttacks.Contains(shot.id) && shot.weapon=="pistol" && shot.attacker==fixture.driver && shot.level==Level,"Receipt/actual launch endpoints отличаются: "+shot.id);
        }
        private void Complete()
        {
            Require(!commitStarted,"Повтор final commit.");commitStarted=true;
            if(!reload)ValidateEvidence(StateJson.FromJson<Evidence>(finishEvidence),runtime.Session.ServerWorld.doors.Single(d=>d.id==DoorId));
            Action<string,long> prepared=(id,revision)=>finishSnapshot=id;runtime.Session.RecoveryPrepared+=prepared;
            try {runtime.Session.CommitWorld();}finally{runtime.Session.RecoveryPrepared-=prepared;}
            Require(finishSnapshot!=null,"Нет exact final snapshot.");finishDoor=StateJson.ToJson(runtime.Session.ServerWorld.doors.Single(d=>d.id==DoorId));
            NetworkServer.SendToAll(new LegacyDoorFinishMessage {success=true,snapshot=finishSnapshot,door=finishDoor,evidence=finishEvidence});
        }
        private void ReceiptPrepared(RecoverySnapshot snapshot,ActorState actor)
        {
            if(done || fixture==null || snapshot.WorldId!=fixture.world || !snapshot.CreateLiveWorld().doors.Any(d=>d.id==DoorId && d.Destroyed))return;
            Guard(()=>
            {
                // The diagnostic actor is already the same second typed copy used by
                // SaveStore. Do not introduce an extra Decimal→Double roundtrip.
                ContentProvenance.Record(ref actor.weaponContent,ref actor.weaponContentHistory);
                receipts[snapshot.Id]=new Receipt {snapshot=snapshot.Id,world=snapshot.WorldId,revision=snapshot.Revision,player=actor,expectedProfileJson=StateJson.ToJson(actor,true),preparedAt=Time.realtimeSinceStartupAsDouble};
                if(receipts.Count>16){var old=receipts.Keys.First(id=>id!=finishSnapshot);receipts.Remove(old);}
            });
        }
        private void ReceiptPersisted(RecoverySnapshot snapshot,string json)
        {if(done || !receipts.TryGetValue(snapshot.Id,out var receipt))return;Guard(()=>{Require(json==receipt.expectedProfileJson,"Exact character JSON receipt отличается от frozen actual capture.");receipt.persistedProfileJson=json;receipt.persistedAt=Time.realtimeSinceStartupAsDouble;});}
        private void CanonicalPersisted(RecoverySnapshot snapshot) {if(!done && Host && snapshot.Id==finishSnapshot)canonicalPersisted=true;}
        private void Acknowledged(int connection,string snapshot,long revision) {if(!done && Host && snapshot==finishSnapshot)acknowledgements.Add(connection);}
        private IEnumerator AwaitDurable()
        {
            while(!done && (!receipts.TryGetValue(finishSnapshot,out var receipt) || receipt.persistedProfileJson==null))yield return new WaitForSecondsRealtime(.1f);
            if(done)yield break;
            Guard(()=>
            {
                Require(StateJson.ToJson(Door)==finishDoor,"Live door отличается от final archive witness.");
                archivePath=Archive(fixture.world,finishSnapshot);Require(File.Exists(archivePath),"Нет собственного exact immutable archive после receipt callback.");
                var snapshot=new RecoverySnapshot(File.ReadAllText(archivePath));var saved=snapshot.CreateLiveWorld();
                Require(snapshot.Id==finishSnapshot && snapshot.WorldId==fixture.world && StateJson.ToJson(saved.doors.Single(d=>d.id==DoorId))==finishDoor,"Архив потерял exact door/receipts.");
                string json=StateJson.ToJson(saved);Require(!RoomContentInstaller.Apply(saved,RoomContentBatch.Read(RoomAuthoring.Load(Level,saved),Level)) && !DoorCapabilityMigration.Prepare(saved,RoomDoorCapabilityContent.Read(RoomAuthoring.Load(Level,saved),Level),()=>throw new InvalidOperationException("Повтор backup")) && StateJson.ToJson(saved)==json,"Reinstall/migration воскресили дверь/изменили saved state.");
                if(reload)Require(Hash(File.ReadAllBytes(sourceArchive))==sourceHash && StateJson.ToJson(sourceWorld.doors.Single(d=>d.id==DoorId))==finishDoor,"Reload source изменён/HP восстановлено.");
                else {ValidateEvidence(StateJson.FromJson<Evidence>(finishEvidence),Door);Require(voices.Count==3 && voices.Select(v=>v.state).SequenceEqual(new[]{"open","close","die"}),"Нет actual transition voices.");}
                Require(frames.Count==(reload?1:5),"Нет всех actual frames.");durable=true;NetworkClient.Send(new LegacyDoorDurableMessage {actor=Actor.id,generation=Actor.generation,snapshot=finishSnapshot});
            });
        }
        private string Archive(string world,string snapshot)=>Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",world,"Snapshots",snapshot+".json");
        private static string Hash(byte[] data) {using var hash=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(hash.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException("Прежняя деревянная дверь: "+reason);}
        private void Guard(Action action) {if(done)return;try{action();}catch(Exception error){Fail(error.Message);}}
        private void Fail(string reason) {if(done)return;if(Host && NetworkServer.active)NetworkServer.SendToAll(new LegacyDoorFinishMessage {success=false,detail=reason});Finish(false,reason);}
        private void Finish(bool success,string reason)
        {
            if(done)return;done=true;Detach();
            try{runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();}catch(Exception error){success=false;reason+="; сохранение: "+error.Message;}
            receipts.TryGetValue(finishSnapshot??"",out var receipt);
            var report=new Report {success=success,reload=reload,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,phase=phase,captures=frames.Count,explicitInitialPositionFixture=!reload && fixture!=null,stockOverrides=false,originalRuntimeVerified=false,geometryVerified=active,traversed=!reload && phase==4,replicaVerified=durable,archiveVerified=durable,receiptCharacterVerified=durable,canonicalAcknowledged=durable,reinstallVerified=durable,sourceBodyPreserved=reload && durable,detail=reason+"; "+Progress,actor=Actor?.id,world=fixture?.world,snapshot=finishSnapshot,snapshotArchive=archivePath,saveDirectory=runtime.Options.SaveDirectory,sourceSnapshot=sourceSnapshot,sourceArchive=sourceArchive,sourceArchiveSha256=sourceHash,fixture=fixture,result=Door,evidence=finishEvidence==null?null:StateJson.FromJson<Evidence>(finishEvidence),receipt=receipt,frames=frames.ToArray(),voices=voices.ToArray(),terminalPlayer=Actor?.Copy(),sourceBody=sourceWorld?.Find(Actor?.id)};
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));exitCode=success?0:1;Invoke(nameof(Exit),.75f);
        }
        private int exitCode;private void Exit()=>Application.Quit(exitCode);
        private void Detach()
        {
            if(runtime==null || runtime.Session==null)return;var s=runtime.Session;s.ClientReady-=ClientHandlers;s.ServerReady-=ServerHandlers;s.AttackReceived-=Attack;s.Changed-=Sound;s.Notice-=Notice;s.Doors.Finished-=Interacted;s.DoorDamage.Finished-=Damaged;s.RecoveryReceiptPrepared-=ReceiptPrepared;s.RecoveryReceiptPersisted-=ReceiptPersisted;s.RecoveryPersisted-=CanonicalPersisted;s.RecoveryAcknowledged-=Acknowledged;
        }
        private void OnDestroy()=>Detach();
    }
}
