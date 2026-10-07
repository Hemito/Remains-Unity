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
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ShotSpreadReady : NetworkMessage { public string actor,world; }
    public struct ShotSpreadBegin : NetworkMessage { public string actor,owner,world; public int generation; }
    public struct ShotSpreadManifest : NetworkMessage { public string json; }
    public struct ShotSpreadObserved : NetworkMessage { public string actor,hash; }
    public struct ShotSpreadFinish : NetworkMessage { public bool success; public string detail; }
    public struct ShotSpreadFailure : NetworkMessage { public string detail; }

    // Opt-in diagnostic: normal new characters, weapon selection/reload/fire only.
    // In a pair the client fires, so evidence crosses the real client->host path.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkShotSpreadProbe : MonoBehaviour
    {
        private const int ShotsPerWeapon=12;
        private const float AngleTolerance=.0001f,MuzzleTolerance=.001f;
        [Serializable] public sealed class Launch
        {
            public string id,attacker,owner,weapon,level;
            public float originX,originY,endX,endY,speed;
        }
        [Serializable] public sealed class Sample
        {
            public Launch launch;
            public string item;
            public int generation,lifecycle,frame,renderFrame,roundsAfter;
            public long selection;
            public float aimX,aimY,deviationDegrees,halfAngleDegrees,muzzleX,muzzleY,muzzleError;
            public float baseAimX,baseAimY,renderPoseMuzzleX,renderPoseMuzzleY;
            public float rendererMuzzleX,rendererMuzzleY,rendererMuzzleError;
            public bool rendererObserved;
        }
        [Serializable] public sealed class Balance
        {
            public string weapon,item,ammo;
            public int initialRounds,finalRounds,initialReserve,finalReserve,reloads;
            public long initialRevision,finalRevision;
        }
        [Serializable] public sealed class Manifest
        {
            public int schema=1,generation,shotsPerWeapon=ShotsPerWeapon;
            public string world,actor,owner,contentFingerprint;
            public float initialHealth,finalHealth;
            public int initialUses,finalUses;
            public Sample[] samples;
            public Balance[] balances;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1,participants,frameRate,receivedCount,frameWidth,frameHeight;
            public bool success,source,replicaVerified,captured,characterSaved,originalRuntimeVerified;
            public string detail,actor,sourceActor,world,manifestSha256,framePath,saveDirectory,characterDirectory,worldDirectory;
            public Manifest evidence;
            public Launch[] received;
            public int pistolNegative,pistolPositive,energyNegative,energyPositive;
            public string limitation="Проверен самостоятельный профиль Unity ±2.5°. Исходные навыки, износ, отдача и original runtime не воспроизводятся этой проверкой.";
        }
        private PrototypeRuntime runtime;
        private InteractionVisuals visuals;
        private string sourceActor,sourceOwner,world,manifestJson,manifestHash,serverManifestHash,pendingManifest,preparedWeapon;
        private int generation,phase,preparedFrame;
        private float deadline,initialHealth;
        private int initialUses;
        private bool readySent,begun,serverBegun,manifestSent,replicaVerified,captured,ackSent,finished;
        private readonly Dictionary<string,Launch> received=new(StringComparer.Ordinal);
        private readonly List<Sample> samples=new();
        private readonly List<Sample> pendingRenderer=new();
        private readonly List<Balance> balances=new();
        private readonly HashSet<int> ready=new(),acknowledged=new();
        private Manifest evidence;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Session.LocalActorId==sourceActor;
        private ActorState Actor=>runtime.Session.LocalActor;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+90;
            Require(value.Options.Smoke?.StartsWith("shot-spread-frames-",StringComparison.Ordinal)==true &&
                value.Options.ProbePeers is 1 or 2 && !string.IsNullOrEmpty(value.Options.SaveDirectory) &&
                !string.IsNullOrEmpty(value.Options.Report) && value.Hud!=null,
                "Нужны shot-spread-frames, отдельное сохранение/отчёт, HUD и 1–2 участника.");
            visuals=FindFirstObjectByType<InteractionVisuals>();Require(visuals!=null,"Не создано представление оружия.");
            value.CommandSource=Command;
            value.Session.AttackReceived+=Observe;
            value.Combat.Reload.Started+=ReloadStarted;
            value.Session.ServerReady+=RegisterServer;
            value.Session.ClientReady+=RegisterClient;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<ShotSpreadBegin>(message=>Guard(()=>Begin(message)));
            NetworkClient.RegisterHandler<ShotSpreadManifest>(message=>Guard(()=>InstallManifest(message.json)));
            NetworkClient.RegisterHandler<ShotSpreadFinish>(message=>End(message.success,message.detail));
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<ShotSpreadReady>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                Require(actor!=null && actor.id==message.actor && actor.level=="hub" && message.world==runtime.Session.ServerWorld.worldId,
                    "Неверный подключённый участник барьера выстрелов.");
                ready.Add(connection.connectionId);
                if(ready.Count!=runtime.Options.ProbePeers || serverBegun) return;
                var players=runtime.Session.ServerWorld.actors.Where(a=>a.player && a.connected).ToArray();
                Require(players.Length==runtime.Options.ProbePeers && players.All(a=>a.level=="hub" && !a.down),"Барьер не содержит всех живых участников хаба.");
                var source=players.Single(a=>runtime.Options.ProbePeers==1 || a.id!=runtime.Session.LocalActorId);
                sourceActor=source.id;sourceOwner=source.owner;generation=source.generation;world=runtime.Session.ServerWorld.worldId;serverBegun=true;
                NetworkServer.SendToAll(new ShotSpreadBegin {actor=sourceActor,owner=sourceOwner,generation=generation,world=world});
            }));
            NetworkServer.RegisterHandler<ShotSpreadManifest>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                Require(actor?.id==sourceActor && actor.owner==sourceOwner && actor.generation==generation && actor.level=="hub" && !actor.down,
                    "Manifest отправлен другим владельцем/поколением.");
                Require(serverBegun && !string.IsNullOrEmpty(message.json) && message.json.Length<100000 && pendingManifest==null && serverManifestHash==null,
                    "Manifest повторён или превышает границу.");
                pendingManifest=message.json;
            }));
            NetworkServer.RegisterHandler<ShotSpreadObserved>((connection,message)=>Guard(()=>
            {
                var actor=runtime.Session.ConnectedActor(connection);
                Require(actor?.id==message.actor && serverManifestHash!=null && message.hash==serverManifestHash,"ACK не относится к подтверждённому manifest.");
                acknowledged.Add(connection.connectionId);
                if(acknowledged.Count==runtime.Options.ProbePeers)
                    NetworkServer.SendToAll(new ShotSpreadFinish {success=true,detail="24 обычных запуска, профиль, дуло, расход/перезарядка и точные реплики подтверждены."});
            }));
            NetworkServer.RegisterHandler<ShotSpreadFailure>((connection,message)=>
            {if(runtime.Session.ConnectedActor(connection)!=null) Fail(message.detail);});
        }
        private void Begin(ShotSpreadBegin message)
        {
            Require(!begun && Actor!=null && message.world==runtime.Session.World.worldId && message.actor!=null,"Повторный/чужой запуск пробы.");
            sourceActor=message.actor;sourceOwner=message.owner;generation=message.generation;world=message.world;begun=true;
            if(!Source) return;
            initialHealth=Actor.health;initialUses=Actor.inventoryUses.Count;
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var item=Item(Actor,weapon);var definition=WeaponCatalog.Get(weapon);
                Require(definition.shotSpreadHalfAngleDegrees==2.5f && item.weaponState.rounds==definition.magazineSize &&
                    item.weaponState.rounds>=ShotsPerWeapon && InventoryJournal.Count(Actor,definition.ammo)>=ShotsPerWeapon,
                    "Нет обычного полного стартового магазина/резерва или профиля ±2.5°: "+weapon);
                balances.Add(new Balance {weapon=weapon,item=item.id,ammo=definition.ammo,initialRounds=item.weaponState.rounds,
                    initialReserve=InventoryJournal.Count(Actor,definition.ammo),initialRevision=item.weaponState.revision});
            }
        }
        private static ItemStack Item(ActorState actor,string weapon)=>actor.inventory.Single(i=>i.definition==weapon && i.weaponState!=null);
        private PlayerCommand Command()
        {
            if(finished || !begun || !Source || phase>=2 || Actor==null) return default;
            try
            {
                Require(Actor.level=="hub" && Actor.owner==sourceOwner && Actor.generation==generation && !Actor.down && Actor.health==initialHealth,
                    "Источник потерял исходное тело/здоровье.");
                string weapon=phase==0?"pistol":"energy";
                var item=Item(Actor,weapon);
                // Account for the authored barrel offset; a floor-height cursor is not a horizontal barrel ray.
                var command=new PlayerCommand {hasAim=true,aim=WeaponPose.RangedAnchor(Actor)+new Vector2(20,WeaponRigs.Get(weapon).muzzleY)};
                if(Actor.equippedWeaponItem!=item.id) {command.weaponItem=item.id;return command;}
                if(preparedWeapon!=weapon) {preparedWeapon=weapon;preparedFrame=Time.frameCount+2;return command;}
                if(Time.frameCount<preparedFrame || runtime.Combat.Reload.Active) return command;
                int count=samples.Count(s=>s.launch.weapon==weapon);
                if(count<ShotsPerWeapon)
                {
                    Require(item.weaponState.rounds>0,"Магазин закончился до 12 подтверждённых локальных запусков: "+weapon);
                    command.fire=true;
                }
                else if(item.weaponState.rounds<WeaponCatalog.Get(weapon).magazineSize) command.reload=true;
                return command;
            }
            catch(Exception error) {Fail(error.Message);return default;}
        }
        private void ReloadStarted(ActorState actor,string weapon)
        {if(!finished && Source && actor.id==sourceActor) Guard(()=>balances.Single(b=>b.weapon==weapon).reloads++);}
        private void Observe(AttackEvent attack)
        {
            if(finished || attack.attacker!=sourceActor) return;
            Guard(()=>
            {
                Require(begun && attack.sourceOwner==sourceOwner && attack.level=="hub" && attack.weapon is "pistol" or "energy",
                    "Неверный источник запуска: "+attack.id);
                var launch=Copy(attack);Require(!received.ContainsKey(launch.id),"Повтор AttackReceived: "+launch.id);
                Require(received.Count<2*ShotsPerWeapon,"Источник создал лишний выстрел.");received.Add(launch.id,launch);
                if(!Source) return;
                RangedShotPreparation.ValidateReceived(attack,runtime.Session.World.worldId);
                var captured=attack.rangedPose;
                var historic=new RangedWeaponPose(new Vector2(captured.rootX,captured.rootY),captured.angleDegrees,captured.mirrored,WeaponRigs.Get(attack.weapon));
                var aim=historic.Direction;var direction=(attack.End-attack.Origin).normalized;var muzzle=historic.Muzzle;
                var sample=new Sample {launch=launch,item=Actor.equippedWeaponItem,generation=Actor.generation,lifecycle=Actor.lifecycleSequence,
                    selection=Actor.weaponSelectionRevision,frame=Time.frameCount,roundsAfter=Item(Actor,attack.weapon).weaponState.rounds,
                    aimX=aim.x,aimY=aim.y,deviationDegrees=Signed(aim,direction),halfAngleDegrees=WeaponCatalog.Get(attack.weapon).shotSpreadHalfAngleDegrees,
                    muzzleX=muzzle.x,muzzleY=muzzle.y,muzzleError=Vector2.Distance(muzzle,attack.Origin),
                    baseAimX=Mathf.Cos(Actor.weaponAngle*Mathf.Deg2Rad),baseAimY=Mathf.Sin(Actor.weaponAngle*Mathf.Deg2Rad)};
                Require(Finite(sample.deviationDegrees) && Mathf.Abs(sample.deviationDegrees)<=sample.halfAngleDegrees+AngleTolerance && sample.muzzleError<=MuzzleTolerance,
                    $"Неверный угол/дуло: shot={attack.id}, angle={sample.deviationDegrees:R}, gap={sample.muzzleError:R}.");
                samples.Add(sample);pendingRenderer.Add(sample);
            });
        }
        private static Launch Copy(AttackEvent attack)=>new() {id=attack.id,attacker=attack.attacker,owner=attack.sourceOwner,weapon=attack.weapon,level=attack.level,
            originX=attack.originX,originY=attack.originY,endX=attack.endX,endY=attack.endY,speed=attack.speed};
        private static bool Same(Launch a,Launch b)=>a.id==b.id && a.attacker==b.attacker && a.owner==b.owner && a.weapon==b.weapon && a.level==b.level &&
            a.originX==b.originX && a.originY==b.originY && a.endX==b.endX && a.endY==b.endY && a.speed==b.speed;
        private static bool Finite(float value)=>float.IsFinite(value);
        private static float Signed(Vector2 aim,Vector2 direction)=>Mathf.Atan2(aim.x*direction.y-aim.y*direction.x,Vector2.Dot(aim,direction))*Mathf.Rad2Deg;
        private void Update()=>Guard(Step);
        private void Step()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Fail($"Таймаут: begun={begun}, phase={phase}, received={received.Count}, manifest={manifestSent}, captured={captured}, ACK={acknowledged.Count}.");return;}
            if(Actor==null || !NetworkClient.isConnected) return;
            if(!readySent && Actor.level=="hub" && !runtime.IsLevelPreparing && runtime.ActiveRoomLighting is {IsReady:true,IsUpdating:false} &&
                runtime.Session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {readySent=true;NetworkClient.Send(new ShotSpreadReady {actor=Actor.id,world=runtime.Session.World.worldId});}
            if(Source && begun && phase<2)
            {
                string weapon=phase==0?"pistol":"energy";var item=Item(Actor,weapon);
                if(samples.Count(s=>s.launch.weapon==weapon)==ShotsPerWeapon && !runtime.Combat.Reload.Active && item.weaponState.rounds==WeaponCatalog.Get(weapon).magazineSize)
                {
                    var balance=balances.Single(b=>b.weapon==weapon);balance.finalRounds=item.weaponState.rounds;
                    balance.finalReserve=InventoryJournal.Count(Actor,balance.ammo);balance.finalRevision=item.weaponState.revision;
                    Require(balance.reloads==1 && balance.initialReserve-balance.finalReserve==ShotsPerWeapon && balance.finalRevision-balance.initialRevision==ShotsPerWeapon+1,
                        "Расход/перезарядка не соответствует 12 выстрелам: "+weapon);phase++;
                }
            }
            if(Source && phase==2 && pendingRenderer.Count==0 && !manifestSent)
            {
                manifestSent=true;var manifest=new Manifest {world=world,actor=sourceActor,owner=sourceOwner,generation=generation,
                    contentFingerprint=WeaponCatalog.ModFingerprint,initialHealth=initialHealth,finalHealth=Actor.health,initialUses=initialUses,finalUses=Actor.inventoryUses.Count,
                    samples=samples.ToArray(),balances=balances.ToArray()};
                Validate(manifest);NetworkClient.Send(new ShotSpreadManifest {json=StateJson.ToJson(manifest)});
            }
            // The reliable owner manifest may arrive before the host's loopback AttackReceived.
            if(Host && pendingManifest!=null)
            {
                var manifest=StateJson.FromJson<Manifest>(pendingManifest);Validate(manifest);
                if(manifest.samples.All(s=>received.ContainsKey(s.launch.id)))
                {Compare(manifest);string json=pendingManifest;pendingManifest=null;serverManifestHash=Hash(json);NetworkServer.SendToAll(new ShotSpreadManifest {json=json});}
            }
            if(evidence!=null && !replicaVerified && evidence.samples.All(s=>received.ContainsKey(s.launch.id)))
            {Compare(evidence);replicaVerified=true;}
            if(replicaVerified && captured && !ackSent)
            {ackSent=true;NetworkClient.Send(new ShotSpreadObserved {actor=Actor.id,hash=manifestHash});}
        }
        private void LateUpdate()=>Guard(()=>
        {
            if(finished) return;
            foreach(var sample in pendingRenderer)
            {
                var view=visuals.FindWeapon(sourceActor);
                Require(Source && Actor.weaponSelectionRevision==sample.selection && Actor.equippedWeaponItem==sample.item && view!=null &&
                    view.Id==sample.launch.weapon && view.gameObject.activeInHierarchy && view.Artwork.enabled && view.Muzzle!=null,
                    "Не наблюдается рисунок того же выбранного оружия: "+sample.launch.id);
                Vector2 muzzle=view.Muzzle.position;sample.renderFrame=Time.frameCount;sample.rendererMuzzleX=muzzle.x;sample.rendererMuzzleY=muzzle.y;
                var currentPose=WeaponPose.SampleRanged(Actor);sample.renderPoseMuzzleX=currentPose.Muzzle.x;sample.renderPoseMuzzleY=currentPose.Muzzle.y;
                sample.rendererMuzzleError=Vector2.Distance(muzzle,currentPose.Muzzle);
                Require(sample.renderFrame==sample.frame && sample.rendererMuzzleError<=MuzzleTolerance,
                    $"Нарисованное дуло отличается: shot={sample.launch.id}, frame={sample.frame}/{sample.renderFrame}, gap={sample.rendererMuzzleError:R}.");
                sample.rendererObserved=true;
            }
            pendingRenderer.Clear();
            if(!replicaVerified || captured) return;
            Require(Camera.main!=null && runtime.Hud!=null && Actor.level=="hub","Нет настоящей камеры хаба для подтверждения.");
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".spread.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
            captured=true;
        });
        private void InstallManifest(string json)
        {
            Require(manifestJson==null,"Повтор server manifest.");evidence=StateJson.FromJson<Manifest>(json);Validate(evidence);
            manifestJson=json;manifestHash=Hash(json);File.WriteAllText(runtime.Options.Report+".manifest.json",json,new UTF8Encoding(false));
        }
        private void Validate(Manifest manifest)
        {
            Require(manifest!=null && manifest.schema==1 && manifest.world==world && manifest.actor==sourceActor && manifest.owner==sourceOwner &&
                manifest.generation==generation && manifest.contentFingerprint==WeaponCatalog.ModFingerprint && manifest.shotsPerWeapon==ShotsPerWeapon &&
                manifest.samples?.Length==2*ShotsPerWeapon && manifest.balances?.Length==2 && manifest.initialHealth==manifest.finalHealth &&
                manifest.finalUses-manifest.initialUses==2,"Manifest потерял контекст/расход.");
            Require(manifest.samples.Select(s=>s.launch.id).Distinct(StringComparer.Ordinal).Count()==2*ShotsPerWeapon,"Manifest содержит повторные IDs.");
            foreach(string weapon in new[]{"pistol","energy"})
            {
                var group=manifest.samples.Where(s=>s.launch.weapon==weapon).ToArray();var balance=manifest.balances.Single(b=>b.weapon==weapon);
                Require(group.Length==ShotsPerWeapon && group.Select(s=>s.selection).Distinct().Count()==1 && group.All(s=>s.item==balance.item),"Неверная серия/выбранный экземпляр: "+weapon);
                Require(group.Any(s=>Mathf.Abs(s.deviationDegrees)>.001f) && group.Select(s=>s.deviationDegrees).Distinct().Count()>1,
                    "Серия не доказывает ненулевой изменяющийся разброс: "+weapon);
                Require(balance.initialRounds==balance.finalRounds && balance.finalRounds==WeaponCatalog.Get(weapon).magazineSize && balance.reloads==1 &&
                    balance.initialReserve-balance.finalReserve==ShotsPerWeapon && balance.finalRevision-balance.initialRevision==ShotsPerWeapon+1,
                    "Manifest потерял баланс боезапаса: "+weapon);
                for(int index=0;index<group.Length;index++)
                {
                    var sample=group[index];
                    var shot=sample.launch;var direction=new Vector2(shot.endX-shot.originX,shot.endY-shot.originY).normalized;
                    var aim=new Vector2(sample.aimX,sample.aimY);float actual=Signed(aim,direction);
                    var origin=new Vector2(shot.originX,shot.originY);var definition=WeaponCatalog.Get(weapon);
                    Require(shot.attacker==sourceActor && shot.owner==sourceOwner && shot.level=="hub" && Finite(shot.speed) && shot.speed>0 &&
                        sample.generation==generation && sample.halfAngleDegrees==WeaponCatalog.Get(weapon).shotSpreadHalfAngleDegrees && sample.halfAngleDegrees==2.5f &&
                        sample.baseAimX>.99999f && Mathf.Abs(sample.baseAimY)<.00001f && sample.roundsAfter==balance.initialRounds-index-1 &&
                        Mathf.Abs(Vector2.Distance(origin,new Vector2(shot.endX,shot.endY))-definition.FlightRange(shot.speed))<=Mathf.Max(.001f,definition.FlightRange(shot.speed)*.00001f) &&
                        Finite(actual) && Mathf.Abs(actual-sample.deviationDegrees)<=AngleTolerance && Mathf.Abs(actual)<=sample.halfAngleDegrees+AngleTolerance &&
                        sample.muzzleError<=MuzzleTolerance && Vector2.Distance(origin,new Vector2(sample.muzzleX,sample.muzzleY))<=MuzzleTolerance &&
                        sample.rendererObserved && sample.rendererMuzzleError<=MuzzleTolerance &&
                        Vector2.Distance(new Vector2(sample.renderPoseMuzzleX,sample.renderPoseMuzzleY),new Vector2(sample.rendererMuzzleX,sample.rendererMuzzleY))<=MuzzleTolerance && sample.frame==sample.renderFrame,
                        "Не подтверждён actual sampled angle/renderer: "+shot.id);
                }
            }
        }
        private void Compare(Manifest manifest)
        {
            Require(received.Count==2*ShotsPerWeapon,"Неверное число действительно полученных событий.");
            foreach(var sample in manifest.samples)
                Require(received.TryGetValue(sample.launch.id,out var actual) && Same(sample.launch,actual),"Реплика изменила actual endpoints/speed: "+sample.launch.id);
        }
        private static string Hash(string text)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
        private void Guard(Action action) {if(finished)return;try {action();} catch(Exception error) {Fail(error.Message);}}
        private void Fail(string detail)
        {
            if(finished)return;
            if(NetworkServer.active) NetworkServer.SendToAll(new ShotSpreadFinish {success=false,detail=detail});
            else if(NetworkClient.isConnected) NetworkClient.Send(new ShotSpreadFailure {detail=detail});
            End(false,detail);
        }
        private void End(bool success,string detail)
        {
            if(finished)return;finished=true;bool saved=false;
            try
            {
                if(success)
                {
                    Require(replicaVerified && captured && ackSent,"Finish пришёл до локальных доказательств.");
                    runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();var character=runtime.Session.Saves.LoadCharacter();
                    Require(character?.id==Actor?.id,"Сохранён другой персонаж.");
                    if(Source) foreach(var balance in evidence.balances)
                        Require(Item(character,balance.weapon).id==balance.item && Item(character,balance.weapon).weaponState.rounds==balance.finalRounds &&
                            Item(character,balance.weapon).weaponState.revision==balance.finalRevision && InventoryJournal.Count(character,balance.ammo)==balance.finalReserve,
                            "Диск потерял действительный боезапас: "+balance.weapon);
                    saved=true;
                }
            }
            catch(Exception error) {success=false;detail+=" "+error.Message;}
            var report=new Report {success=success,detail=detail,source=Source,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,
                actor=Actor?.id,sourceActor=sourceActor,world=world,manifestSha256=manifestHash,evidence=evidence,received=received.Values.ToArray(),
                receivedCount=received.Count,replicaVerified=replicaVerified,captured=captured,frameWidth=captured?1920:0,frameHeight=captured?1080:0,
                framePath=runtime.Options.Report+".spread.png",characterSaved=saved,saveDirectory=runtime.Options.SaveDirectory,
                characterDirectory=runtime.Session.Saves.CharacterDirectoryPath,worldDirectory=runtime.Session.Saves.WorldDirectoryPath,
                pistolNegative=evidence?.samples.Count(s=>s.launch.weapon=="pistol" && s.deviationDegrees<0)??0,
                pistolPositive=evidence?.samples.Count(s=>s.launch.weapon=="pistol" && s.deviationDegrees>0)??0,
                energyNegative=evidence?.samples.Count(s=>s.launch.weapon=="energy" && s.deviationDegrees<0)??0,
                energyPositive=evidence?.samples.Count(s=>s.launch.weapon=="energy" && s.deviationDegrees>0)??0};
            try {File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true),new UTF8Encoding(false));}
            catch(Exception error) {success=false;Debug.LogError("Не удалось записать shot-spread report: "+error.Message);}
            if(success)Debug.Log("NetworkShotSpreadProbe PASS: "+detail);else Debug.LogError("NetworkShotSpreadProbe FAIL: "+detail);
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        private void OnDestroy()
        {
            if(runtime?.Session!=null) {runtime.Session.AttackReceived-=Observe;runtime.Session.ServerReady-=RegisterServer;runtime.Session.ClientReady-=RegisterClient;}
            if(runtime?.Combat!=null) runtime.Combat.Reload.Started-=ReloadStarted;
        }
    }
}
