using System;
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
    public struct WarehouseMetalFixtureMessage : NetworkMessage {public string json;}
    public struct WarehouseMetalPhaseMessage : NetworkMessage {public int phase;}
    public struct WarehouseMetalObservedMessage : NetworkMessage {public string actor,evidence;public int generation,phase;}
    public struct WarehouseMetalFinishMessage : NetworkMessage {public bool success;public string detail,doorJson,snapshot,evidence;}

    // Opt-in isolated starting positions only. Stocks, damage, targets and every
    // subsequent action remain the ordinary production character/command path.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryWarehouseMetalDoorProbe : MonoBehaviour
    {
        private const string Level="factory:0";
        public const string DoorId=Level+":original:plant:40:rd8DsY9aYG3qkDra";
        private const string Definition="warehouse-metal-door";
        [Serializable] public sealed class PlayerBaseline {public string id,owner;public int generation,lifecycle;public float x,y,health,width,height;}
        [Serializable] public sealed class Balance
        {public string weapon,item,ammo;public int initialRounds,initialReserve,finalRounds,finalReserve,shots,reloads;public float damage;}
        [Serializable] public sealed class Fixture
        {public string world,driver,initialDoorJson,entrySide;public int rangedThreats,blockedIncomingRays;public PlayerBaseline[] players;public Balance[] balances;}
        [Serializable] public sealed class Launch
        {public string id,actor,owner,weapon,level;public float originX,originY,endX,endY,speed;}
        [Serializable] public sealed class DriverEvidence {public string actor,world;public Balance[] balances;public Launch[] launches;}
        [Serializable] public sealed class Voice
        {public string state,clip;public int frame,nativeId,samples;public bool playing,muted;public float volume;}
        [Serializable] public sealed class Frame
        {
            public int phase,frame,width,height;public string state,door,sprite,texture,path;
            public bool open,destroyed,blocks,canInteract,visible;
            public long revision;public float health,x,y,physicalX,physicalY,physicalWidth,physicalHeight,pixelsPerUnit,pivotX,pivotY;
            public Vector3 center,size;public Vector3[] viewport;
        }
        [Serializable] public sealed class Report
        {
            public int schema=1,participants,frameRate,captures,shots,acceptedInteractions,damageReplies,reloads;
            public bool success,driver,explicitInitialPositionFixture,stockOverrides,originalRuntimeVerified,traversed,returned,
                geometryVerified,replicaVerified,savedVerified,snapshotArchiveVerified,reinstallVerified,contentsPreserved;
            public string detail,actor,sourceActor,world,door,definition,snapshot,snapshotArchive,saveDirectory,characterDirectory,worldDirectory;
            public float health,x,y,traverseX,returnX;public Fixture fixture;public DriverEvidence evidence;public DoorState result;
            public Frame[] frames;public Voice[] voices;
            public string limitation="Изолированное начальное положение справа на настоящем нижнем полу, под solid concrete; входящие лучи/E/стволы проверены по текущей геометрии. Короткий обычный проход влево и возврат вправо. Дальнейшие действия и боеприпасы обычные; AI включён, потеря HP/чужое вмешательство — отказ. Исходные замки/структурное сопротивление пока pending; отдельный process reload запускает штатный warehouse-reload runner.";
        }
        private PrototypeRuntime runtime;
        private DoorVisuals visuals;
        private Fixture fixture;
        private PlayerBaseline baseline;
        private int phase,serverPhase=-1,reportedPhase=-1,capturedPhase=-1,preparedFrame,shots,acceptedInteractions,damageReplies,reloads;
        private float deadline,phaseAt,nextFireAt,lastShotAt;
        private float traverseX,returnX;
        private bool traveled,installed,fixtureActive,finished,acted,traversed,returned,geometryVerified,replicaVerified,
            savedVerified,snapshotArchiveVerified,reinstallVerified,contentsPreserved;
        private string preparedWeapon,lastNotice,finishDoor,finishSnapshot,finishEvidence,snapshotArchive;
        private bool finishPending;
        private readonly Dictionary<string,Launch> launches=new(StringComparer.Ordinal);
        private readonly List<Frame> frames=new();
        private readonly List<Voice> voices=new();
        private readonly HashSet<int> voiceIds=new();
        private readonly HashSet<string> serverWitnesses=new(StringComparer.Ordinal);
        private readonly Dictionary<string,int> reloadCounts=new(StringComparer.Ordinal);
        private bool soundBaseline,previousOpen,previousDestroyed;
        private string driverEvidence;
        private bool Host=>runtime.Options.Role=="host";
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Source=>fixture==null?null:runtime.Session.World.Find(fixture.driver);
        private bool Driver=>fixture!=null && Actor?.id==fixture.driver;
        private DoorState Door=>runtime.Session.World?.doors.Find(d=>d.id==DoorId);
        private string Progress=>$"phase={phase}, driver={fixture?.driver}, actor={Actor?.id}/{Actor?.Position}/HP{Actor?.health}, source={Source?.Position}, door={Door?.open}/{Door?.Destroyed}/{Door?.integrity?.health}, shots={shots}, captures={frames.Count}, peers={serverWitnesses.Count}, notice={lastNotice}";

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+180;
            Require(value.Options.Smoke?.StartsWith("warehouse-metal-doors-frames-",StringComparison.Ordinal)==true &&
                value.Options.ProbePeers is 1 or 2 && value.Hud!=null && !string.IsNullOrEmpty(value.Options.Report) && !string.IsNullOrEmpty(value.Options.SaveDirectory),
                "Проверка metal doors требует -frames-, HUD, 1–2 участника и отдельные сохранения.");
            visuals=FindFirstObjectByType<DoorVisuals>();Require(visuals!=null,"Нет представления дверей.");
            if(Host)
            {
                Require(value.Session.Saves.SelectedSnapshot()==null,"Проверка не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Session.AttackReceived+=OnAttack;
            value.Session.Changed+=ObserveSound;
            value.Session.Notice+=Notice;
            value.Combat.Reload.Started+=OnReload;
            value.Session.Doors.Finished+=OnInteraction;
            value.Session.DoorDamage.Finished+=OnDamage;
            value.Session.ClientReady+=RegisterClient;
            value.Session.ServerReady+=RegisterServer;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<WarehouseMetalFixtureMessage>(message=>Guard(()=>
            {
                Require(fixture==null,"Повтор начальной fixture.");fixture=StateJson.FromJson<Fixture>(message.json);
                Require(fixture.world==runtime.Session.World.worldId && fixture.entrySide=="right" && fixture.rangedThreats>0 && fixture.blockedIncomingRays==fixture.rangedThreats*5,"Fixture другого мира/без проверенной правой защиты.");
                baseline=fixture.players.Single(a=>a.id==runtime.Session.LocalActorId);phase=0;phaseAt=Time.unscaledTime;
            }));
            NetworkClient.RegisterHandler<WarehouseMetalPhaseMessage>(message=>Guard(()=>
            {
                Require(message.phase==phase+1,"Пропущен/повторён фазовый барьер: "+message.phase);
                phase=message.phase;phaseAt=Time.unscaledTime;acted=false;preparedWeapon=null;
            }));
            NetworkClient.RegisterHandler<WarehouseMetalFinishMessage>(message=>Guard(()=>
            {
                if(!message.success) {Finish(false,message.detail);return;}
                finishDoor=message.doorJson;finishSnapshot=message.snapshot;finishEvidence=message.evidence;finishPending=true;
            }));
        }
        private void RegisterServer()=>NetworkServer.RegisterHandler<WarehouseMetalObservedMessage>((connection,message)=>Guard(()=>
        {
            if(fixture==null || message.phase!=serverPhase) return;
            var actor=runtime.Session.ConnectedActor(connection);
            Require(actor!=null && actor.id==message.actor && actor.generation==message.generation &&
                fixture.players.Any(a=>a.id==actor.id && a.owner==actor.owner && a.generation==actor.generation),"Барьер другого участника/эпохи.");
            if(message.phase==5 && actor.id==fixture.driver)
            {Require(!string.IsNullOrEmpty(message.evidence),"Нет фактических запусков/расхода водителя.");driverEvidence=message.evidence;}
            serverWitnesses.Add(actor.id);
        }));
        private void Notice(string value)=>lastNotice=value;
        private void OnReload(ActorState actor,string weapon)
        {if(Driver && actor.id==fixture.driver){reloads++;reloadCounts[weapon]=reloadCounts.GetValueOrDefault(weapon)+1;}}
        private void OnInteraction(Remains.Networking.DoorReply reply)
        {if(!fixtureActive || !Driver) return;if(!reply.accepted) Fail("E отклонён: "+reply.reason);else acceptedInteractions++;}
        private void OnDamage(Remains.Networking.DoorDamageReply reply)
        {if(reply.door!=DoorId || !Driver) return;if(!reply.accepted) Fail("Контакт двери отклонён: "+reply.reason);else damageReplies++;}
        private void OnAttack(AttackEvent attack)
        {
            if(fixture==null || attack.attacker!=fixture.driver) return;
            Guard(()=>
            {
                Require(attack.weapon is "pistol" or "energy" && attack.level==Level,"Неожиданная атака водителя.");
                var launch=new Launch {id=attack.id,actor=attack.attacker,owner=attack.sourceOwner,weapon=attack.weapon,level=attack.level,
                    originX=attack.originX,originY=attack.originY,endX=attack.endX,endY=attack.endY,speed=attack.speed};
                if(launches.TryGetValue(attack.id,out var old)) {Require(StateJson.ToJson(old)==StateJson.ToJson(launch),"ID запуска изменил endpoints.");return;}
                launches.Add(attack.id,launch);shots++;lastShotAt=Time.unscaledTime;
            });
        }
        private void ObserveSound()
        {
            if(!fixtureActive || Door==null) return;
            Guard(()=>
            {
                var door=Door;
                if(!soundBaseline) {previousOpen=door.open;previousDestroyed=door.Destroyed;soundBaseline=true;return;}
                if(previousOpen==door.open && previousDestroyed==door.Destroyed) return;
                string label=door.Destroyed?"die":door.open?"open":"close";
                var view=visuals.Find(DoorId);Require(view!=null,"Переход двери не имеет actual view.");
                var clip=door.Destroyed?view.Definition.DestroySound:door.open?view.Definition.OpenSound:view.Definition.CloseSound;
                var candidates=runtime.Audio.GetComponentsInChildren<AudioSource>().Where(v=>v.clip==clip && !voiceIds.Contains(v.GetInstanceID())).ToArray();
                Require(candidates.Length==1,"Нет единственного нового реального голоса перехода: "+label);
                var source=candidates[0];Require(source.clip!=null && source.clip.samples>0 && source.isPlaying,"Голос перехода не воспроизводится: "+label);
                voiceIds.Add(source.GetInstanceID());voices.Add(new Voice {state=label,clip=clip.name,frame=Time.frameCount,nativeId=source.GetInstanceID(),samples=clip.samples,playing=source.isPlaying,muted=source.mute,volume=source.volume});
                previousOpen=door.open;previousDestroyed=door.Destroyed;
            });
        }
        private PlayerCommand Command()
        {
            if(finished || !fixtureActive || !Driver || Door==null || runtime.IsLevelPreparing) return default;
            try
            {
                var command=new PlayerCommand {hasAim=true,aim=Door.Bounds.center};
                if(phase is 1 or 4)
                {
                    if(!acted)
                    {
                        var selected=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,Door.InteractionBounds.center);
                        Require(selected.kind==InteractionKind.Door && selected.id==DoorId,"E выбрал другой объект: "+selected.kind+"/"+selected.id);
                        acted=true;command.interact=true;
                    }
                }
                else if(phase is 2 or 3)
                {
                    float target=phase==2?Door.Bounds.xMin-Anatomy.Movement(Actor).width*.5f-.3f:baseline.x;
                    float gap=target-Actor.x;
                    if(Mathf.Abs(gap)>.08f && !(Mathf.Abs(gap)<.4f && Mathf.Abs(Actor.vx)>.1f)) command.movement=Vector2.right*Mathf.Sign(gap);
                }
                else if(phase==5 && !Door.Destroyed)
                {
                    string weapon=Total(Actor,"pistol")>0?"pistol":"energy";var item=Item(Actor,weapon);
                    Require(Total(Actor,weapon)>0,"Обычный стартовый запас исчерпан до разрушения: "+Progress);
                    if(Actor.equippedWeaponItem!=item.id) {command.weaponItem=item.id;return command;}
                    if(preparedWeapon!=weapon) {preparedWeapon=weapon;preparedFrame=Time.frameCount+3;return command;}
                    if(Time.frameCount<preparedFrame || runtime.Combat.Reload.Active) return command;
                    if(item.weaponState.rounds==0) {command.reload=true;return command;}
                    if(Time.unscaledTime<nextFireAt) return command;
                    RequireClearLaunch(Actor,runtime.Geometry.Get(Level),Door,weapon);
                    command.fire=true;
                    var recoil=WeaponCatalog.Get(weapon).recoil;
                    nextFireAt=Time.unscaledTime+Mathf.Max(.65f,recoil==null?0:recoil.riseSeconds+Mathf.Max(recoil.positionReturnSeconds,recoil.angleReturnSeconds)+.05f);
                }
                return command;
            }
            catch(Exception error) {Fail(error.Message);return default;}
        }
        private void Update()=>Guard(Step);
        private void Step()
        {
            if(finished) return;
            Require(Time.unscaledTime<=deadline,"Таймаут: "+Progress);
            if(Actor==null || !NetworkClient.isConnected || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(!traveled) {traveled=true;runtime.Session.Travel.RequestFactory();}
            if(Host && !installed && !runtime.Session.Travel.Pending && !runtime.IsLevelPreparing &&
                runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level) &&
                runtime.Session.World.actors.Where(a=>a.player && a.connected).All(a=>a.level==Level)) Setup();
            if(fixture==null || baseline==null || Actor.generation!=baseline.generation && !fixtureActive) return;
            Require(Actor.id==baseline.id && Actor.generation==baseline.generation && Actor.lifecycleSequence==baseline.lifecycle && Actor.owner==baseline.owner &&
                Actor.level==Level && !Actor.down && Actor.health==baseline.health,"Участник потерял исходный живой контекст/HP: "+Progress);
            if(!fixtureActive)
            {
                if(!Supported(Actor)) return;
                fixtureActive=true;phaseAt=Time.unscaledTime;previousOpen=Door.open;previousDestroyed=Door.Destroyed;soundBaseline=true;
            }
            var door=Door;Require(door!=null,"Нет canonical lower door.");door.Validate();
            Require(runtime.Geometry.Get(Level).platforms.Any(s=>s.sourceDoorId==DoorId)==door.Blocks,"Collider не соответствует Blocks.");geometryVerified=true;
            if(finishPending) {TryFinish();return;}
            if(phase==2 && Source!=null && Source.x<door.Bounds.xMin-Anatomy.Movement(Source).width*.5f-.1f && Supported(Source)) {traversed=true;traverseX=Source.x;}
            if(phase==3 && traversed && Source!=null && Mathf.Abs(Source.x-fixture.players.Single(a=>a.id==fixture.driver).x)<.12f &&
                Mathf.Abs(Source.vx)<.1f && Supported(Source)) {returned=true;returnX=Source.x;}
            bool ready=phase switch {0=>!door.open && !door.Destroyed && door.revision==0,1=>door.open && !door.Destroyed,
                2=>traversed && Source!=null && Mathf.Abs(Source.vx)<.1f,3=>returned,4=>!door.open && !door.Destroyed && door.revision==2,
                5=>door.Destroyed && !door.Blocks && !door.CanInteract && door.integrity.health==0 &&
                    door.integrity.resolvedAttacks.Count>0 && door.integrity.resolvedAttacks.All(launches.ContainsKey),_=>false};
            if(ready && reportedPhase!=phase && (phase is 2 or 3 || capturedPhase==phase))
            {
                string evidence=phase==5 && Driver?StateJson.ToJson(BuildDriverEvidence()):null;
                reportedPhase=phase;NetworkClient.Send(new WarehouseMetalObservedMessage {actor=Actor.id,generation=Actor.generation,phase=phase,evidence=evidence});
            }
            if(Host && serverWitnesses.Count==runtime.Options.ProbePeers)
            {
                var canonical=runtime.Session.ServerWorld.doors.Single(d=>d.id==DoorId);
                bool current=phase switch {0=>!canonical.open && canonical.revision==0,1=>canonical.open,4=>!canonical.open && canonical.revision==2,5=>canonical.Destroyed,_=>true};
                if(!current) return;
                serverWitnesses.Clear();
                if(phase==5) CompleteServer();
                else {serverPhase=phase+1;NetworkServer.SendToAll(new WarehouseMetalPhaseMessage {phase=serverPhase});}
            }
            if(Driver && phase==5 && !door.Destroyed && Time.unscaledTime-phaseAt>10 && Time.unscaledTime-lastShotAt>6 && !runtime.Combat.Reload.Active)
                Fail("Обычная стрельба не продвигается: "+Progress);
        }
        private void Setup()
        {
            var world=runtime.Session.ServerWorld;var door=world.doors.Single(d=>d.id==DoorId);
            Require(door.definition==Definition && !door.open && !door.Destroyed && door.integrity is {health:1000,maxHealth:1000} && door.CanInteract &&
                Mathf.Abs(door.Bounds.center.x-144.4f)<.0001f && Mathf.Abs(door.y-.8f)<.0001f && door.width==.8f && door.height==1.6f,"Не совпадает authored lower metal door.");
            var players=world.actors.Where(a=>a.player && a.connected).ToArray();
            string driver=players.Single(a=>runtime.Options.ProbePeers==1 || a.id!=runtime.Session.LocalActorId).id;
            var positions=new List<PlayerBaseline>();var room=runtime.Geometry.Get(Level);
            foreach(var player in players.OrderBy(a=>a.id==driver?0:1))
            {
                Vector2 position=FindPlacement(world,room,door,player,player.id==driver,positions);
                player.Position=position;player.Velocity=Vector2.zero;player.climbing=player.crouched=false;player.pose="idle";player.poseTime=0;player.generation++;player.revision++;
                var hull=Anatomy.Movement(player);
                positions.Add(new PlayerBaseline {id=player.id,owner=player.owner,generation=player.generation,lifecycle=player.lifecycleSequence,x=position.x,y=position.y,health=player.health,width=hull.width,height=hull.height});
            }
            var source=players.Single(a=>a.id==driver);var balances=new List<Balance>();
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var definition=WeaponCatalog.Get(weapon);var item=Item(source,weapon);
                balances.Add(new Balance {weapon=weapon,item=item.id,ammo=definition.ammo,initialRounds=item.weaponState.rounds,
                    initialReserve=InventoryJournal.Count(source,definition.ammo),damage=definition.damage});
            }
            Require(balances[0].initialRounds==12 && balances[0].initialReserve==36 && balances[0].damage==11 &&
                balances[1].initialRounds==15 && balances[1].initialReserve==45 && balances[1].damage==8,"Не обычный текущий стартовый профиль48/60,11/8.");
            Require(balances.Sum(b=>(b.initialRounds+b.initialReserve)*b.damage)>=door.integrity.health,"Стартового запаса недостаточно для текущего явного damage rule.");
            Require(HasIncomingCover(world,room,source,out int threats,out int blocked),"Правая стартовая позиция потеряла защиту incoming rays до публикации.");
            fixture=new Fixture {world=world.worldId,driver=driver,initialDoorJson=StateJson.ToJson(door),entrySide="right",rangedThreats=threats,blockedIncomingRays=blocked,players=positions.ToArray(),balances=balances.ToArray()};
            installed=true;serverPhase=0;runtime.Session.CommitWorld();
            var serialized=StateJson.ToJson(fixture);fixture=null;
            NetworkServer.SendToAll(new WarehouseMetalFixtureMessage {json=serialized});
            Debug.Log($"Metal door probe: только начальное положение игроков справа под solid concrete; NPC threats={threats}, blocked rays={blocked}. Запасы48pistol/60energy обычные, дальнейшие E/проход влево/возврат вправо/fire/reload штатные; AI включён.");
        }
        private Vector2 FindPlacement(WorldState world,RoomLayout room,DoorState door,ActorState actor,bool driver,List<PlayerBaseline> placed)
        {
            for(float distance=1.4f;distance<=4;distance+=.2f)
            {
                var position=new Vector2(door.Bounds.xMax+distance,door.y);var hull=Anatomy.Movement(actor);var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
                if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position) ||
                    placed.Any(a=>Mathf.Abs(a.x-position.x)<1.3f) ||
                    WorldProps.All(world).Any(p=>p.level==Level && p.hasPhysicalBody && Interior(body,new Rect(p.x-p.width*.5f,p.y-p.anchorHeight,p.width,p.height))) ||
                    world.actors.Any(a=>!a.player && a.level==Level && Interior(body,InventorySource.BodyBounds(a)))) continue;
                var sample=StateJson.Copy(actor);sample.Position=position;sample.climbing=sample.crouched=false;sample.pose="idle";
                if(!HasIncomingCover(world,room,sample,out _,out _)) continue;
                if(driver)
                {
                    var selected=InteractionSelection.Select(world,sample,runtime.Session.Rules,door.InteractionBounds.center);
                    if(selected.kind!=InteractionKind.Door || selected.id!=DoorId) continue;
                    bool clear=true;
                    foreach(string weapon in new[]{"pistol","energy"})
                    {
                        if(!WeaponInventory.Select(sample,Item(sample,weapon).id,out string reason)) throw new InvalidOperationException(reason);
                        WeaponPose.Aim(sample,door.Bounds.center,room);
                        try {RequireClearLaunch(sample,room,door,weapon);} catch(InvalidOperationException) {clear=false;break;}
                    }
                    if(!clear) continue;
                }
                return position;
            }
            throw new InvalidOperationException("Нет реальной свободной правой lower-floor опоры с incoming cover/E/muzzle cone clearance: "+actor.id);
        }
        // Fixture-time evidence only, not a promise that AI cannot move afterwards.
        // Every subsequent damage/extra door transition still fails the scenario.
        private static bool HasIncomingCover(WorldState world,RoomLayout room,ActorState actor,out int threats,out int blocked)
        {
            threats=blocked=0;var body=InventorySource.BodyBounds(actor);const float inset=.02f;
            var points=new[]{body.center,new Vector2(body.xMin+inset,body.yMin+inset),new Vector2(body.xMax-inset,body.yMin+inset),
                new Vector2(body.xMin+inset,body.yMax-inset),new Vector2(body.xMax-inset,body.yMax-inset)};
            foreach(var enemy in world.actors)
            {
                if(enemy.player || enemy.down || enemy.health<=0 || enemy.level!=Level || string.IsNullOrEmpty(enemy.weapon) ||
                    WeaponCatalog.Get(enemy.weapon).projectileSpeed<=0) continue;
                threats++;var sample=StateJson.Copy(enemy);NpcWeaponAim.Apply(sample,actor.BodyPosition+Vector2.up*.55f,room);
                var origin=WeaponPose.Muzzle(sample);
                foreach(var target in points)
                {
                    if(!ShotObstacles.FirstContact(room,origin,target,out var cover).HasValue || cover.oneWay || cover.shotsPassThrough) return false;
                    blocked++;
                }
            }
            return threats>0;
        }
        private static bool Interior(Rect a,Rect b)=>a.xMax>b.xMin+.0001f && a.xMin<b.xMax-.0001f && a.yMax>b.yMin+.0001f && a.yMin<b.yMax-.0001f;
        private bool Supported(ActorState actor)=>!actor.climbing && Mathf.Abs(actor.y-Door.y)<.001f && Mathf.Abs(actor.vy)<.001f &&
            ActorTerrainMotion.Supported(runtime.Geometry.Get(Level),Anatomy.Movement(actor),actor.Position);
        private static ItemStack Item(ActorState actor,string weapon)=>actor.inventory.Single(i=>i.definition==weapon && i.weaponState!=null);
        private static int Total(ActorState actor,string weapon)=>Item(actor,weapon).weaponState.rounds+InventoryJournal.Count(actor,WeaponCatalog.Get(weapon).ammo);
        private static void RequireClearLaunch(ActorState actor,RoomLayout room,DoorState door,string weapon)
        {
            Vector2 origin=WeaponPose.Muzzle(actor);
            Require(!ShotObstacles.FirstContact(room,WeaponPose.RangedAnchor(actor),origin).HasValue,"Ствол пересекает преграду: "+weapon);
            Vector2 direction=WeaponPose.Direction(actor);float half=WeaponCatalog.Get(weapon).shotSpreadHalfAngleDegrees;
            foreach(float offset in new[]{-half,0,half})
            {
                float angle=Mathf.Atan2(direction.y,direction.x)+offset*Mathf.Deg2Rad;
                var end=origin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*WeaponCatalog.Get(weapon).range;
                Require(ShotObstacles.FirstContact(room,origin,end,out var surface).HasValue && surface.sourceDoorId==DoorId,"Actual spread cone не попадает в нужную дверь: "+weapon+"/"+offset);
            }
        }
        private void LateUpdate()=>Guard(()=>
        {
            if(finished || !fixtureActive || capturedPhase==phase || phase is 2 or 3 || runtime.IsLevelPreparing || Time.unscaledTime-phaseAt<1.5f) return;
            var door=Door;string label=phase==0?"closed":phase==1?"open":phase==4?"closed-again":phase==5?"destroyed":null;
            if(label==null || phase==1 && !door.open || phase==4 && (door.open || door.revision!=2) || phase==5 && !door.Destroyed) return;
            var view=visuals.Find(DoorId);var camera=Camera.main;Require(view!=null && camera!=null,"Нет actual door/camera для кадра.");
            var sprite=door.Destroyed?view.Definition.Destroyed:door.open?view.Definition.Opened:view.Definition.Closed;
            Require(view.Artwork.sprite==sprite && sprite!=null && sprite.pixelsPerUnit==50 && sprite.rect.size==new Vector2(40,80) && sprite.pivot==new Vector2(20,0),"Actual sprite/state/registration не совпали.");
            var bounds=view.Artwork.bounds;float z=bounds.center.z;
            var points=new[]{new Vector3(bounds.min.x,bounds.min.y,z),new Vector3(bounds.max.x,bounds.min.y,z),new Vector3(bounds.max.x,bounds.max.y,z),new Vector3(bounds.min.x,bounds.max.y,z)}.Select(camera.WorldToViewportPoint).ToArray();
            bool visible=view.Artwork.enabled && view.gameObject.activeInHierarchy && view.Artwork.color.a>0 &&
                (camera.cullingMask&(1<<view.Artwork.gameObject.layer))!=0 && points.All(p=>p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1 && p.z>=camera.nearClipPlane && p.z<=camera.farClipPlane);
            Require(visible,"Door sprite не полностью видим в native кадре.");
            var path=runtime.Options.Report+".metal-"+label+".png";
            NetworkFrameCapture.Save(camera,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);
            var frame=new Frame {phase=phase,frame=Time.frameCount,width=1920,height=1080,state=label,door=door.id,sprite=sprite.name,texture=sprite.texture.name,path=path,
                open=door.open,destroyed=door.Destroyed,blocks=door.Blocks,canInteract=door.CanInteract,visible=visible,revision=door.revision,health=door.integrity.health,
                x=Actor.x,y=Actor.y,physicalX=door.x,physicalY=door.y,physicalWidth=door.width,physicalHeight=door.height,pixelsPerUnit=sprite.pixelsPerUnit,pivotX=sprite.pivot.x,pivotY=sprite.pivot.y,center=bounds.center,size=bounds.size,viewport=points};
            frames.Add(frame);capturedPhase=phase;File.WriteAllText(runtime.Options.Report+".metal-"+label+".frame.json",StateJson.ToJson(frame,true));
        });
        private DriverEvidence BuildDriverEvidence()
        {
            var balances=StateJson.Copy(fixture.balances);
            foreach(var balance in balances)
            {
                balance.finalRounds=Item(Actor,balance.weapon).weaponState.rounds;balance.finalReserve=InventoryJournal.Count(Actor,balance.ammo);
                balance.shots=launches.Values.Count(a=>a.weapon==balance.weapon);balance.reloads=reloadCounts.GetValueOrDefault(balance.weapon);
                Require(balance.initialRounds+balance.initialReserve-balance.finalRounds-balance.finalReserve==balance.shots,"Расход патронов не соответствует реальным запускам: "+balance.weapon);
            }
            Require(acceptedInteractions==2,"Требовались ровно две обычные E операции.");
            return new DriverEvidence {actor=Actor.id,world=fixture.world,balances=balances,launches=launches.Values.ToArray()};
        }
        private void ValidateEvidence(DriverEvidence evidence,DoorState door)
        {
            Require(evidence!=null && evidence.actor==fixture.driver && evidence.world==fixture.world && evidence.balances?.Length==2 && evidence.launches!=null,"Нет полного source evidence.");
            Require(evidence.launches.Select(s=>s.id).Distinct().Count()==evidence.launches.Length,"Повтор IDs source launch.");
            foreach(var shot in evidence.launches) Require(launches.TryGetValue(shot.id,out var actual) && StateJson.ToJson(actual)==StateJson.ToJson(shot),"Peer launch/endpoint не совпал: "+shot.id);
            float total=0;
            foreach(var balance in evidence.balances)
            {
                var start=fixture.balances.Single(b=>b.weapon==balance.weapon);
                Require(balance.item==start.item && balance.ammo==start.ammo && balance.initialRounds==start.initialRounds && balance.initialReserve==start.initialReserve &&
                    balance.damage==start.damage && balance.finalRounds>=0 && balance.finalReserve>=0 &&
                    balance.initialRounds+balance.initialReserve-balance.finalRounds-balance.finalReserve==balance.shots &&
                    balance.shots==evidence.launches.Count(a=>a.weapon==balance.weapon),"Source изменил обычный профиль/расход.");
                total+=door.integrity.resolvedAttacks.Count(id=>evidence.launches.Any(s=>s.id==id && s.weapon==balance.weapon))*balance.damage;
            }
            Require(door.integrity.resolvedAttacks.All(id=>evidence.launches.Any(s=>s.id==id)) && door.integrity.health==0 && total>=1000,"Die не связан с actual source projectile receipts/damage.");
        }
        private void CompleteServer()
        {
            var session=runtime.Session;var door=session.ServerWorld.doors.Single(d=>d.id==DoorId);
            ValidateEvidence(StateJson.FromJson<DriverEvidence>(driverEvidence),door);
            session.CommitWorld();session.FlushSaveWrites();
            var saved=session.Saves.LoadWorld(false);Require(StateJson.ToJson(saved.doors.Single(d=>d.id==DoorId))==StateJson.ToJson(door),"Каноническая запись двери не durable.");
            var batch=RoomContentBatch.Read(RoomAuthoring.Load(Level,saved),Level);
            string before=StateJson.ToJson(saved);Require(!RoomContentInstaller.Apply(saved,batch) && StateJson.ToJson(saved)==before,"Повтор установки изменил savedworld/loot/door.");
            NetworkServer.SendToAll(new WarehouseMetalFinishMessage {success=true,detail="Обычный lower door E/проход/return/E/fire/reload подтвердился на всех участниках.",
                doorJson=StateJson.ToJson(door),snapshot=session.Saves.SelectedSnapshot().Id,evidence=driverEvidence});
        }
        private void TryFinish()
        {
            Require(StateJson.ToJson(Door)==finishDoor,"Финальная реплика двери отличается.");
            var evidence=StateJson.FromJson<DriverEvidence>(finishEvidence);ValidateEvidence(evidence,Door);replicaVerified=true;
            runtime.Session.FlushSaveWrites();var saved=runtime.Session.Saves.LoadWorld(false);
            if(saved==null || StateJson.ToJson(saved.doors.Single(d=>d.id==DoorId))!=finishDoor) return;
            Require(finishSnapshot?.Length==64 && finishSnapshot.All(Uri.IsHexDigit) && Guid.TryParseExact(fixture.world,"N",out _),"Некорректный ID финального архивного снимка.");
            string archive=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",fixture.world,"Snapshots",finishSnapshot+".json");
            if(!File.Exists(archive)) return;
            var immutable=new RecoverySnapshot(File.ReadAllText(archive));
            Require(immutable.Id==finishSnapshot && immutable.WorldId==fixture.world &&
                StateJson.ToJson(immutable.CreateLiveWorld().doors.Single(d=>d.id==DoorId))==finishDoor,"Собственный immutable archive не подтвердил world/door/receipts финального снимка.");
            snapshotArchive=archive;snapshotArchiveVerified=true;
            savedVerified=true;string before=StateJson.ToJson(saved),contents=StateJson.ToJson(saved.containers);
            Require(!RoomContentInstaller.Apply(saved,RoomContentBatch.Read(RoomAuthoring.Load(Level,saved),Level)) && StateJson.ToJson(saved)==before,"Reinstall воскресил дверь/изменил мир.");
            reinstallVerified=true;contentsPreserved=StateJson.ToJson(saved.containers)==contents;
            Require(frames.Count==4 && traversed && returned && voices.Count==3 && voices.Select(v=>v.state).SequenceEqual(new[]{"open","close","die"}),"Нет четырёх actual frames, прохода и трёх real voices.");
            Finish(true,"Actual states/audio/replica/disk/reinstall подтверждены.");
        }
        private void Guard(Action action) {if(finished) return;try {action();} catch(Exception error) {Fail(error.Message);}}
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
        private void Fail(string reason)
        {
            if(finished) return;
            if(Host && NetworkServer.active) NetworkServer.SendToAll(new WarehouseMetalFinishMessage {success=false,detail=reason+": "+Progress});
            Finish(false,reason);
        }
        private void Finish(bool success,string reason)
        {
            if(finished) return;finished=true;
            try {runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();}
            catch(Exception error) {success=false;reason+="; Сохранение проверки отклонено: "+error.Message;}
            var report=new Report {success=success,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,driver=Driver,
                explicitInitialPositionFixture=fixture!=null,stockOverrides=false,originalRuntimeVerified=false,detail=reason+": "+Progress,
                actor=Actor?.id,sourceActor=fixture?.driver,world=runtime.Session.World?.worldId,door=DoorId,definition=Door?.definition,snapshot=finishSnapshot,
                snapshotArchive=snapshotArchive,snapshotArchiveVerified=snapshotArchiveVerified,
                saveDirectory=runtime.Options.SaveDirectory,characterDirectory=runtime.Session.Saves.CharacterDirectoryPath,worldDirectory=runtime.Session.Saves.WorldDirectoryPath,
                fixture=fixture,evidence=finishEvidence==null?null:StateJson.FromJson<DriverEvidence>(finishEvidence),result=Door,
                captures=frames.Count,shots=shots,acceptedInteractions=acceptedInteractions,damageReplies=damageReplies,reloads=reloads,traversed=traversed,returned=returned,
                geometryVerified=geometryVerified,replicaVerified=replicaVerified,savedVerified=savedVerified,reinstallVerified=reinstallVerified,contentsPreserved=contentsPreserved,
                health=Actor?.health??0,x=Actor?.x??0,y=Actor?.y??0,traverseX=traverseX,returnX=returnX,frames=frames.ToArray(),voices=voices.ToArray()};
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));exitCode=success?0:1;Invoke(nameof(Exit),.5f);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        private void OnDestroy()
        {
            if(runtime==null || runtime.Session==null) return;
            runtime.Session.AttackReceived-=OnAttack;runtime.Session.Changed-=ObserveSound;runtime.Session.Notice-=Notice;
            runtime.Session.Doors.Finished-=OnInteraction;runtime.Session.DoorDamage.Finished-=OnDamage;
            runtime.Session.ClientReady-=RegisterClient;runtime.Session.ServerReady-=RegisterServer;
            if(runtime.Combat!=null) runtime.Combat.Reload.Started-=OnReload;
        }
    }
}
