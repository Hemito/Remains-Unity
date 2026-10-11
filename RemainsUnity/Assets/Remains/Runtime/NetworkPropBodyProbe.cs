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
    public struct PropBodyPrepared : NetworkMessage
    {
        public string source,cargo,definition,owner,contents;public string[] previousAttacks;
        public int heldGeneration,initialGrenades;public float mass,initialHealth,damage,floor,liftX,liftY,playerX,playerY;
    }
    public struct PropBodyStage : NetworkMessage {public int stage;public string snapshot;public long revision;}
    public struct PropBodyObserved : NetworkMessage {public int stage;public bool success;public string detail;}
    public struct PropBodyFinished : NetworkMessage {public bool success;public string detail;}

    // Ordinary movement/map/Q/G commands. The only fixture mutation places this client's own player.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkPropBodyProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private PropBodyHubRoute route;
        private PropBodyProbeSetup setup;
        private PropBodyPrepared manifest;
        private RoomLayout cargoRoom;
        private AudioClip expectedClip;
        private int stage,serverStage,landingBaseline;
        private float deadline,heldSince=-1,evidenceSince=-1,heldY,releasedY;
        private bool fixture,mapClick,manifestSent,prepared,threw,reported,finished,released,fell,downCaptured;
        private string savedSnapshot,capture;
        private long savedRevision;
        private readonly List<string> grenades=new();
        private readonly List<Launch> launches=new();
        private readonly List<Voice> landingVoices=new();
        private readonly List<Proof> proofs=new();
        private readonly HashSet<int> voiceIds=new();
        private readonly Dictionary<int,HashSet<int>> observed=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Remote=>runtime.Options.Smoke.Contains("-remote-");
        private string SourceRole=>Remote?"client":"host";
        private bool SourceRoleHere=>runtime.Options.Role==SourceRole;
        private bool Frames=>runtime.Options.Smoke.Contains("-frames-");
        private bool Source=>prepared && runtime.Session.LocalActorId==manifest.source;
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Victim=>prepared?runtime.Session.World.Find(manifest.source):null;
        private ContainerState Cargo=>runtime.Session.World.containers.Find(c=>c.id==(prepared?manifest.cargo:setup?.Cargo.id));
        private Vector2 Lift=>prepared?new Vector2(manifest.liftX,manifest.liftY):setup.Lift;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+120;
            if(value.Options.ProbePeers!=2 || value.Session.DebugTravelAllowed || value.Hud==null ||
                string.IsNullOrEmpty(value.Options.SaveDirectory) || string.IsNullOrEmpty(value.Options.Report))
                throw new InvalidOperationException("Проверка физики тела требует двух участников, обычных переходов, HUD, отдельных сохранений и отчёта.");
            if(Frames && (Screen.width!=1920 || Screen.height!=1080))
                throw new InvalidOperationException($"Для кадров нужны 1920×1080, получено {Screen.width}×{Screen.height}.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            value.CommandSource=Command;value.Grenades.Created+=GrenadeCreated;value.Session.Notice+=Notice;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<PropBodyPrepared>(Begin);
                NetworkClient.RegisterHandler<PropBodyStage>(message=>
                {stage=message.stage;threw=reported=false;evidenceSince=-1;savedSnapshot=message.snapshot;savedRevision=message.revision;});
                NetworkClient.RegisterHandler<PropBodyFinished>(message=>Finish(message.success,message.detail));
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<PropBodyPrepared>(AcceptPrepared);
                NetworkServer.RegisterHandler<PropBodyObserved>((connection,message)=>
                {
                    if(value.Session.ConnectedActor(connection)==null) return;
                    if(!message.success) {NetworkServer.SendToAll(new PropBodyFinished {success=false,detail=message.detail});return;}
                    if(message.stage!=serverStage) return;
                    if(!observed.TryGetValue(serverStage,out var peers)) observed.Add(serverStage,peers=new());
                    if(!peers.Add(connection.connectionId) || peers.Count!=2) return;
                    if(serverStage==4)
                    {NetworkServer.SendToAll(new PropBodyFinished {success=true,detail="Реальные Q/G, два решения пострадавшего, свободная физика подключённого тела, один голос приземления у источника и сохранение обоими участниками."});return;}
                    serverStage++;
                    var next=new PropBodyStage {stage=serverStage};
                    if(serverStage==4)
                    {
                        value.Session.CommitWorld();
                        var snapshot=value.Session.Saves.SelectedSnapshot();next.snapshot=snapshot.Id;next.revision=snapshot.Revision;
                    }
                    NetworkServer.SendToAll(next);
                });
            };
        }
        private void AcceptPrepared(NetworkConnectionToClient connection,PropBodyPrepared message)
        {
            if(serverStage!=0 || finished) return;
            try
            {
                var sender=runtime.Session.ConnectedActor(connection);
                var rejection=PropBodyProbeSetup.ManifestRejection(runtime.Session.ServerWorld,sender,runtime.Session.LocalActorId,
                    Remote,message,TelekinesisSettings.Load().maximumMass,WeaponCatalog.Get("grenade").damage);
                if(rejection!=null) throw new InvalidOperationException(rejection);
                serverStage=1;NetworkServer.SendToAll(message,Channels.Reliable);
            }
            catch(Exception error)
            {NetworkServer.SendToAll(new PropBodyFinished {success=false,detail="Сервер отклонил подготовку probe: "+error.Message});}
        }
        private PlayerCommand Command()
        {
            if(finished || !SourceRoleHere || Actor==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=2) return default;
            if(!fixture)
            {
                if(Actor.level!="hub" || route==null) return default;
                return route.Command(runtime);
            }
            if(Actor.down || stage>=4) return default;
            var cargo=Cargo;
            if(cargo==null) return default;
            var command=new PlayerCommand {telekinesis=true,hasAim=true,aim=cargo.held?Lift:cargo.Center};
            if(prepared && stage is 2 or 3 && !threw)
            {
                threw=true;command.grenade=true;command.aim=Actor.Position+Vector2.down*.3f;
            }
            return command;
        }
        private IEnumerator ChooseFactory()
        {
            yield return UiPointerProbe.Click(runtime.Hud.WorldMap.FactoryRect);
            if(!runtime.Hud.WorldMap.CanTravel) {Fail("Карта не разрешает обычный вход в завод.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.WorldMap.TravelRect);
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) {Fail("Таймаут. "+Context());return;}
                if(Actor==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=2) return;
                if(!prepared)
                {
                    if(!SourceRoleHere || manifestSent) return;
                    if(!fixture)
                    {
                        if(Actor.level=="hub")
                        {
                            route??=new PropBodyHubRoute(runtime);
                            if(runtime.Hud.WorldMap.IsOpen && !mapClick) {mapClick=true;StartCoroutine(ChooseFactory());}
                            return;
                        }
                        if(Actor.level!="factory:0" || runtime.Session.Travel.Pending) return;
                        PrepareFixture();return;
                    }
                    var cargo=runtime.Session.World.containers.Single(c=>c.id==setup.Cargo.id);
                    if(!cargo.held || cargo.owner!=runtime.Session.LocalOwner || Vector2.Distance(cargo.Position,setup.Lift)>.08f)
                    {heldSince=-1;return;}
                    if(heldSince<0) heldSince=Time.unscaledTime;
                    if(Time.unscaledTime-heldSince<.75f) return;
                    if(cargo.generation!=setup.InitialGeneration+1) throw new InvalidOperationException("Один обычный захват дал неверную generation.");
                    if(Actor.health!=100 || Actor.down || Actor.resolvedAttacks.Count!=0)
                        throw new InvalidOperationException("Источник уже получил урон до первого броска. HP и атаки не исправлялись.");
                    NetworkClient.Send(new PropBodyPrepared {source=Actor.id,cargo=cargo.id,definition=cargo.definition,mass=cargo.mass,owner=cargo.owner,contents=setup.Contents,
                        previousAttacks=Actor.resolvedAttacks.ToArray(),heldGeneration=cargo.generation,initialGrenades=CountGrenades(Actor),
                        initialHealth=Actor.health,damage=WeaponCatalog.Get("grenade").damage,floor=setup.Floor,
                        liftX=setup.Lift.x,liftY=setup.Lift.y,playerX=setup.PlayerPosition.x,playerY=setup.PlayerPosition.y},Channels.Reliable);
                    // Both source roles use this reliable request and the same server validation.
                    manifestSent=true;return;
                }
                if(Cargo==null || Victim==null) return;
                foreach(var explosion in runtime.Session.World.explosions)
                    if(explosion.attack.attacker==manifest.source && explosion.attack.weapon=="grenade" && !grenades.Contains(explosion.attack.id)) grenades.Add(explosion.attack.id);
                ValidateCommon();
                if(stage==4 && !reported) VerifyPersistence();
            }
            catch(Exception error) {Fail(error.Message+" "+Context());}
        }
        private void PrepareFixture()
        {
            if(WorldRooms.Definition(runtime.Session.World,"factory:0")!=LegacyFactoryFixture.DefinitionId || Actor.health!=100 || Actor.down || CountGrenades(Actor)<2)
                throw new InvalidOperationException("Нужны обычный авторский завод и свежий активный персонаж со стартовыми гранатами. Предметы/HP не исправлялись.");
            cargoRoom=runtime.Geometry.Get("factory:0");setup=PropBodyProbeSetup.Select(runtime.Session.World,Actor,cargoRoom,runtime.Session.Rules.Telekinesis);
            Actor.Position=setup.PlayerPosition;Actor.vx=Actor.vy=0;Actor.revision++;runtime.Session.PublishActor(Actor);fixture=true;
            Debug.Log($"Проверочная позиция только своего игрока: actor={Actor.id}, cargo={setup.Cargo.id}, definition={setup.Cargo.definition}, mass={setup.Cargo.mass}, floor={setup.Floor}, position={Actor.Position}, lift={setup.Lift}. Ящик, лут, HP и owner не изменены.");
        }
        private void Begin(PropBodyPrepared message)
        {
            try
            {
                manifest=message;prepared=true;stage=1;reported=false;evidenceSince=-1;
                if(Cargo==null || Victim==null) throw new InvalidOperationException("Подготовленный актёр или авторский контейнер отсутствует в реальной реплике.");
                cargoRoom=runtime.Geometry.Get("factory:0");landingBaseline=runtime.PropAudio.Landings;
                foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>()) voiceIds.Add(voice.GetInstanceID());
                var asset=Resources.Load<TextAsset>("Presentation/prop-audio");
                if(asset==null) throw new InvalidOperationException("Нет настроек звуков физических объектов.");
                var settings=JsonUtility.FromJson<PropContactAudio.Settings>(asset.text);
                var entry=settings?.entries?.SingleOrDefault(e=>e.definition==Cargo.definition);
                expectedClip=entry==null?null:Resources.Load<AudioClip>(entry.clip);
                if(expectedClip==null || expectedClip.samples<=0) throw new InvalidOperationException("Нет реального звука приземления для "+Cargo.definition);
            }
            catch(Exception error) {Fail(error.Message+" "+Context());}
        }
        private void GrenadeCreated(Remains.Gameplay.GrenadeFlight flight)
        {
            if(!prepared || flight.state.attacker!=manifest.source || flight.state.level!="factory:0") return;
            if(!grenades.Contains(flight.state.id)) grenades.Add(flight.state.id);
            if(launches.Any(observation=>observation.id==flight.state.id)) return;
            launches.Add(new Launch {id=flight.state.id,stage=stage,time=Time.unscaledTime,sourceObserver=Source,
                remaining=flight.state.remaining,position=flight.state.Position,velocity=flight.state.Velocity});
        }
        private static int CountGrenades(ActorState actor)=>actor.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count);
        private void ValidateCommon()
        {
            var actor=Victim;
            if(actor.level!="factory:0" || !actor.connected || actor.owner!=manifest.owner ||
                runtime.Session.World.actors.Any(a=>a.player && a.connected && a.id!=actor.id && (a.level!="hub" || a.down)))
                throw new InvalidOperationException("Разделение уровней/активный союзник в хабе/владелец источника потеряны.");
            if(runtime.Session.Pause.IsPaused("factory:0")) throw new InvalidOperationException("Сценарий неожиданно остановлен.");
            if(PropBodyProbeSetup.Inventory(Cargo)!=manifest.contents || Cargo.owner!=manifest.owner || Cargo.definition!=manifest.definition || Cargo.mass!=manifest.mass)
                throw new InvalidOperationException("Изменились тип/масса, вещи, aliases или реальный владелец ящика.");
            if(actor.resolvedAttacks.Except(manifest.previousAttacks).Except(grenades).Any())
                throw new InvalidOperationException("Источник получил постороннюю атаку; нокдаун нельзя приписать двум своим гранатам.");
            if(grenades.Count>2) throw new InvalidOperationException("Обычные команды создали больше двух гранат.");
        }
        private bool Receipt(int count)
        {
            if(grenades.Count<count) return false;
            var actor=Victim;
            return grenades.Take(count).All(id=>actor.resolvedExplosions.Count(x=>x==id)==1 && actor.resolvedAttacks.Count(x=>x==id)==1) &&
                CountGrenades(actor)==manifest.initialGrenades-count && Mathf.Abs(actor.health-Mathf.Max(0,manifest.initialHealth-manifest.damage*count))<.001f;
        }
        private void LateUpdate()
        {
            if(finished || !prepared || Cargo==null || Victim==null || reported) return;
            try
            {
                foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>())
                    if(voiceIds.Add(voice.GetInstanceID()) && Source && voice.clip==expectedClip && voice.volume>0)
                        landingVoices.Add(new Voice {id=voice.GetInstanceID(),clip=voice.clip.name,volume=voice.volume,muted=voice.mute,playing=voice.isPlaying});
                bool valid=false;
                if(stage==1) valid=Cargo.held && Cargo.generation==manifest.heldGeneration && Vector2.Distance(Cargo.Position,Lift)<.1f && !Victim.down;
                if(stage==2) valid=Receipt(1) && !Victim.down && Cargo.held && Cargo.generation==manifest.heldGeneration && Vector2.Distance(Cargo.Position,Lift)<.1f;
                if(stage==3 && Receipt(2) && Victim.down && !Cargo.held)
                {
                    if(Cargo.generation!=manifest.heldGeneration+1) throw new InvalidOperationException("Очистка удержания изменила generation больше одного раза.");
                    if(!released) {released=true;releasedY=Cargo.y;capture="down";}
                    // Both peers actually observed the held position at the previous barrier.
                    // A remote first free pose may already be supported; compare the two real observations.
                    if(Cargo.y<heldY-.15f) fell=true;
                    valid=fell && PropBodyProbeSetup.Supported(Cargo,cargoRoom,runtime.Session.World) && Mathf.Abs(Cargo.y-Cargo.anchorHeight-manifest.floor)<.02f;
                    if(Source && valid) valid=runtime.PropAudio.Landings-landingBaseline==1 && landingVoices.Count==1;
                }
                if(stage<3 && Source && (runtime.PropAudio.Landings!=landingBaseline || landingVoices.Count>0))
                    throw new InvalidOperationException("Удержание озвучено как падение.");
                if(stage>=3 && Source && (runtime.PropAudio.Landings-landingBaseline>1 || landingVoices.Count>1))
                    throw new InvalidOperationException("Одна очистка удержания создала повторное приземление/голос.");
                if(valid && evidenceSince<0) evidenceSince=Time.unscaledTime;
                if(!valid) evidenceSince=-1;
                if(valid && Time.unscaledTime-evidenceSince>=.75f) CompleteStage();
                Capture();
            }
            catch(Exception error) {Fail(error.Message+" "+Context());}
        }
        private void CompleteStage()
        {
            if(reported) return;reported=true;
            if(stage==2) heldY=Cargo.y;
            var proof=new Proof {stage=stage,time=Time.unscaledTime,observerLevel=Actor.level,sourceHealth=Victim.health,sourceDown=Victim.down,
                owner=Cargo.owner,generation=Cargo.generation,sequence=Cargo.sequence,held=Cargo.held,position=Cargo.Position,velocity=new Vector2(Cargo.vx,Cargo.vy),
                grenadeIds=grenades.ToArray(),fallVerified=fell,landingCounter=runtime.PropAudio.Landings-landingBaseline,
                landingVoiceRequired=Source && stage>=3,landingVoices=landingVoices.ToArray(),snapshot=savedSnapshot,
                heldBeforeDownY=heldY,firstObservedFreeY=releasedY};
            proofs.Add(proof);File.WriteAllText(runtime.Options.Report+$".stage-{stage}.json",StateJson.ToJson(proof,true));
            capture="stage-"+stage;Capture();
            NetworkClient.Send(new PropBodyObserved {stage=stage,success=true,detail=Context()});
        }
        private void VerifyPersistence()
        {
            string path=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",runtime.Session.World.worldId,"Snapshots",savedSnapshot+".json");
            if(!File.Exists(path)) throw new InvalidOperationException("Не получен настоящий сохранённый снимок: "+path);
            var snapshot=new RecoverySnapshot(File.ReadAllText(path));var world=snapshot.CreateLiveWorld();
            var cargo=world.containers.Single(c=>c.id==manifest.cargo);var actor=world.Find(manifest.source);
            if(snapshot.Id!=savedSnapshot || snapshot.Revision!=savedRevision || cargo.owner!=manifest.owner || cargo.held ||
                cargo.definition!=manifest.definition || cargo.mass!=manifest.mass ||
                cargo.generation!=manifest.heldGeneration+1 || PropBodyProbeSetup.Inventory(cargo)!=manifest.contents ||
                Vector2.Distance(cargo.Position,Cargo.Position)>.02f || !actor.down || actor.health!=0 ||
                grenades.Count!=2 || !grenades.All(id=>actor.resolvedExplosions.Count(x=>x==id)==1 && actor.resolvedAttacks.Count(x=>x==id)==1) ||
                CountGrenades(actor)!=manifest.initialGrenades-2)
                throw new InvalidOperationException("Сохранение потеряло физику, владельца, generation, вещи или реальные решения пострадавшего.");
            CompleteStage();
        }
        private void Capture()
        {
            if(capture==null) return;
            string label=capture;capture=null;
            if(!Frames) return;
            NetworkSceneRecord.Save(runtime,label=="down"?30:stage);
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+label+".png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
            if(label=="down") downCaptured=true;
        }
        private void Notice(string text) {if(fixture && !finished) Fail("Игровая команда отклонена: "+text);}
        private string Context()=> $"stage={stage}, sourceRole={SourceRole}, source={manifest.source}, cargo={manifest.cargo}, observer={Actor?.level}, HP={Victim?.health}, down={Victim?.down}, owner={Cargo?.owner}, held={Cargo?.held}, generation={Cargo?.generation}/{manifest.heldGeneration}, position={Cargo?.Position}, grenades={string.Join(",",grenades)}, fall={fell}, voices={landingVoices.Count}";
        private void Fail(string reason)
        {
            if(finished) return;
            if(NetworkClient.isConnected) NetworkClient.Send(new PropBodyObserved {stage=stage,success=false,detail=reason});
            Finish(false,reason);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(success && (proofs.Count!=4 || !fell || launches.Count!=2 || Source && (landingVoices.Count!=1 || Frames && !downCaptured)))
            {success=false;detail="Неполный локальный набор доказательств. "+detail;}
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,context=Context(),
                source=Source,sourceRole=SourceRole,manifest=manifest,proofs=proofs.ToArray(),launches=launches.ToArray(),voices=landingVoices.ToArray(),
                batchMuted=Application.isBatchMode,downCaptured=downCaptured},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1.5f);Application.Quit(success?0:2);}
        private void OnDestroy()
        {if(runtime!=null) {runtime.Grenades.Created-=GrenadeCreated;runtime.Session.Notice-=Notice;}}
        [Serializable] private sealed class Voice {public int id;public string clip;public float volume;public bool muted,playing;}
        [Serializable] private sealed class Launch {public string id;public int stage;public float time,remaining;public bool sourceObserver;public Vector2 position,velocity;}
        [Serializable] private sealed class Proof
        {public int stage,generation,landingCounter;public uint sequence;public float time,sourceHealth,heldBeforeDownY,firstObservedFreeY;public bool sourceDown,held,fallVerified,landingVoiceRequired;public string owner,observerLevel,snapshot;public Vector2 position,velocity;public string[] grenadeIds;public Voice[] landingVoices;}
        [Serializable] private sealed class Report
        {public bool success,source,batchMuted,downCaptured;public string detail,context,sourceRole;public PropBodyPrepared manifest;public Proof[] proofs;public Launch[] launches;public Voice[] voices;}
    }
}
