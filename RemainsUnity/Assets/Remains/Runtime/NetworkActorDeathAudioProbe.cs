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
    public struct ActorDeathProbeFixture : NetworkMessage {public string json;}
    public struct ActorDeathProbeReady : NetworkMessage {public string actor;}
    public struct ActorDeathProbeStart : NetworkMessage {public bool started;}
    public struct ActorDeathProbeObserve : NetworkMessage {public bool request;}
    public struct ActorDeathProbeAck : NetworkMessage {public string actor;public bool success;public string detail;}
    public struct ActorDeathProbeFinish : NetworkMessage {public bool success;public string detail,snapshot,npcJson;}

    // Only initial player placement is a fixture. NPCs, ammunition, health,
    // damage decisions and death feedback use the ordinary production paths.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkActorDeathAudioProbe : MonoBehaviour
    {
        [Serializable] public sealed class Placement {public string actor;public Vector2 position;}
        [Serializable] public sealed class Fixture
        {public string world,driver,target,type;public ActorState initialNpc;public Placement[] players;}
        [Serializable] public sealed class Balance
        {public string item,ammo;public int rounds,reserve,uses;public long revision;public float health;}
        [Serializable] public sealed class Voice
        {public int id,samples,channels,frequency;public string resource,clip;public bool muted,playing;public float volume,expectedVolume;public Vector2 listener,position;}
        [Serializable] public sealed class Report
        {
            public int schema=1,participants,frameRate,accepted,played,cues,snapshotChanges,repeatedSnapshots,shots,reloads;
            public bool success,resume,driver,explicitInitialPositionFixture,stockOverrides,originalRuntimeVerified,captured,archiveVerified;
            public string detail,type,target,actor,world,snapshot,archive,saveDirectory,characterDirectory,worldDirectory;
            public Fixture fixture;public Balance before,after;public ActorState finalNpc;public DamageResult[] outcomes;
            public AttackEvent[] attacks;public Voice[] voices;public string[] frames;
        }
        private PrototypeRuntime runtime;
        private Fixture fixture;
        private Balance before;
        private Voice voice;
        private readonly List<DamageResult> outcomes=new();
        private readonly Dictionary<string,AttackEvent> attacks=new(StringComparer.Ordinal);
        private readonly List<string> frames=new();
        private readonly HashSet<int> ready=new(),acknowledged=new(),voiceIds=new();
        private float deadline,placedAt,deathAt=-1,nextRepeat,resumeAt=-1;
        private int cues,changes,repeated,reloads;
        private bool travel,placed,readySent,started,fixtureSent,observeSent,observed,finished,captureDeath;
        private ActorDeathProbeFinish? pendingFinish;
        private string archive;
        private float listenerVolume;
        private bool Host=>runtime.Options.Role=="host";
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-",StringComparison.Ordinal);
        private string Type=>runtime.Options.Smoke.Contains("-radroach-",StringComparison.Ordinal)?"radroach":"raider";
        private string Target=>"factory:0:original:plant:18:"+(Type=="radroach"?"QyxFODdq6EPvuE6L":"Cf5jLZ7MxuNJWaNp");
        private ActorState Actor=>runtime.Session.LocalActor;
        private ActorState Npc=>runtime.Session.World?.Find(Target);
        private bool Driver=>Actor?.id==fixture?.driver;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+120;
            Require(value.Options.Smoke.StartsWith("actor-death-",StringComparison.Ordinal) && value.Options.Smoke.Contains("-frames-") &&
                value.Options.ProbePeers is 1 or 2 && (!Resume || value.Options.ProbePeers==1) && value.Hud!=null && value.DeathAudio!=null &&
                !string.IsNullOrEmpty(value.Options.Report) && !string.IsNullOrEmpty(value.Options.SaveDirectory),"Нужны HUD, аудиосервис, frames, отдельная папка и 1–2 участника (resume только solo).");
            listenerVolume=AudioListener.volume;AudioListener.volume=0;
            if(Host)
            {
                var saved=value.Session.Saves.SelectedSnapshot();
                Require(Resume?saved!=null:saved==null,"Неверный fresh/resume каталог; существующий мир не заменяется.");
                if(!Resume)value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
                else Require(saved.CreateLiveWorld().Find(Target) is {down:true,health:<=0},"Копия для resume не содержит побеждённую выбранную встречу.");
            }
            value.CommandSource=Command;
            value.Session.ClientReady+=RegisterClient;value.Session.ServerReady+=RegisterServer;
            value.Session.Changed+=Changed;value.Session.AttackReceived+=Attack;
            value.DeathAudio.DeathPlayed+=Death;value.DamageFeedback.Received+=Damage;
            value.Combat.Reload.Started+=Reload;
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<ActorDeathProbeFixture>(m=>Guard(()=>
            {if(fixture==null)fixture=StateJson.FromJson<Fixture>(m.json);else Require(StateJson.ToJson(fixture)==m.json,"Изменённая повторная fixture.");}));
            NetworkClient.RegisterHandler<ActorDeathProbeStart>(m=>started=m.started);
            NetworkClient.RegisterHandler<ActorDeathProbeObserve>(m=>{if(m.request)observeSent=true;});
            NetworkClient.RegisterHandler<ActorDeathProbeFinish>(m=>{if(!m.success)Finish(false,m.detail);else pendingFinish=m;});
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<ActorDeathProbeReady>((c,m)=>Guard(()=>
            {
                Require(runtime.Session.ConnectedActor(c)?.id==m.actor && fixture?.players.Any(p=>p.actor==m.actor)==true,"Чужое подтверждение готовности.");
                ready.Add(c.connectionId);if(ready.Count==runtime.Options.ProbePeers)NetworkServer.SendToAll(new ActorDeathProbeStart {started=true});
            }));
            NetworkServer.RegisterHandler<ActorDeathProbeAck>((c,m)=>Guard(()=>
            {
                Require(runtime.Session.ConnectedActor(c)?.id==m.actor,"Чужой audio ACK.");
                if(!m.success){Fail(m.detail);return;}
                acknowledged.Add(c.connectionId);
                if(acknowledged.Count!=runtime.Options.ProbePeers)return;
                var session=runtime.Session;session.CommitWorld();session.FlushSaveWrites();
                var snapshot=session.Saves.SelectedSnapshot();var npc=snapshot.CreateLiveWorld().Find(Target);
                Require(npc is {down:true,health:<=0},"Финальный immutable снимок потерял смерть.");
                NetworkServer.SendToAll(new ActorDeathProbeFinish {success=true,detail="Обычные выстрелы и один реальный голос смерти подтверждены на всех участниках.",snapshot=snapshot.Id,npcJson=StateJson.ToJson(npc)});
            }));
        }
        private PlayerCommand Command()
        {
            if(finished || Resume || !placed || Actor==null || Npc==null)return default;
            if(!Driver)return default;
            string region=Type=="radroach"?"thorax":"torso";
            if(!Anatomy.TryRegion(Npc,region,out var aim,out _)){Fail("Нет реальной области прицеливания: "+region);return default;}
            // Up is an ordinary held input, not a normalized opacity/height.
            // The authored lift is .8m and a real ceiling may lower it again.
            var command=new PlayerCommand {weapon="pistol",hasAim=true,aim=aim,movement=Vector2.up};
            if(!started)return command;
            if(!Npc.down && !Actor.down)
            {
                command.reload=Actor.inventory.Single(i=>i.definition=="pistol" && i.count==1 && i.weaponState!=null).weaponState.rounds==0;
                command.fire=!command.reload;
            }
            return command;
        }
        private void Update()=>Guard(Step);
        private void Step()
        {
            Require(Time.unscaledTime<=deadline,"Истёк срок: "+Context());
            if(Actor==null || !NetworkClient.isConnected)return;
            Require(runtime.Session.World.worldId!=null && runtime.Audio.Volume>0 && AudioListener.volume==0,"Нет мира/громкости либо нарушено беззвучное batch наблюдение.");
            Require(Screen.width==1920 && Screen.height==1080,"Нужен фактический viewport1920×1080.");
            if(Resume){StepResume();return;}
            if(!travel){travel=true;runtime.Session.Travel.RequestFactory();}
            if(Host && !fixtureSent && runtime.Session.ServerWorld.actors.Count(a=>a.player && a.connected && a.level=="factory:0")==runtime.Options.ProbePeers &&
                Actor.level=="factory:0" && !runtime.Session.Travel.Pending && !runtime.IsLevelPreparing)
                CreateFixture();
            if(fixture==null || Actor.level!="factory:0" || runtime.IsLevelPreparing || runtime.Session.Travel.Pending)return;
            Require(!Actor.down && Actor.health>0,"Игрок потерял живой контекст; HP не подменяется. "+Context());
            if(!placed)Place();
            if(!readySent && Time.unscaledTime-placedAt>Mathf.Max(.25f,WeaponRigs.RaiseDelay+.1f) && (!Driver || Actor.weapon=="pistol") &&
                runtime.ActiveRoomLighting is {IsReady:true,IsUpdating:false} && DrawnPlayerReady() && InView())
            {
                var room=runtime.Geometry.Get(Actor.level);
                Require(!Actor.climbing && ActorTerrainMotion.Supported(room,Anatomy.Movement(Actor),Actor.Position),"Начальное тело потеряло обычную опору.");
                if(Driver)Require(!ShotObstacles.FirstContact(room,WeaponPose.RangedAnchor(Actor),WeaponPose.Muzzle(Actor)).HasValue,
                    "Настоящее дуло перекрыто terrain: root="+WeaponPose.RangedAnchor(Actor)+", muzzle="+WeaponPose.Muzzle(Actor));
                Capture(1);readySent=true;NetworkClient.Send(new ActorDeathProbeReady {actor=Actor.id});
            }
            if(started && Driver && attacks.Count==0 && Time.unscaledTime-placedAt>8)
                throw new InvalidOperationException("Штатный fire не дал AttackReceived: "+Context()+", muzzle="+WeaponPose.Muzzle(Actor)+", origin="+WeaponPose.RangedAnchor(Actor));
            if(Host && started && runtime.Session.ServerWorld.Find(Target) is {down:true,health:<=0})
            {
                if(deathAt<0){deathAt=Time.unscaledTime;nextRepeat=deathAt+.35f;}
                if(repeated<3 && Time.unscaledTime>=nextRepeat){runtime.Session.CommitWorld();repeated++;nextRepeat=Time.unscaledTime+.35f;}
                if(repeated==3 && Time.unscaledTime-deathAt>=2 && !observeSent)
                {observeSent=true;NetworkServer.SendToAll(new ActorDeathProbeObserve {request=true});}
            }
            if(observeSent && !observed && Npc is {down:true,health:<=0} && cues==1 && frames.Count==2)
            {
                Require(runtime.DeathAudio.Played==1 && runtime.DeathAudio.Accepted==1 && voice!=null && changes>=3,"Повторные снимки изменили голоса либо не наблюдались: "+Context());
                Require(outcomes.Any(d=>d.confirmedNpcDeath && Npc.resolvedAttacks.Contains(d.attack) && attacks.ContainsKey(d.attack)),"Смерть не связана с реальным выстрелом/receipt.");
                if(Driver)ValidateBalance();
                observed=true;NetworkClient.Send(new ActorDeathProbeAck {actor=Actor.id,success=true});
            }
            if(pendingFinish.HasValue)CompleteArchive();
        }
        private void CreateFixture()
        {
            var world=runtime.Session.ServerWorld;var npc=world.Find(Target);
            Require(npc!=null && !npc.player && !npc.down && npc.health>0 && npc.actorType==Type && npc.level=="factory:0","Не найдена живая авторская встреча: "+Target);
            var players=world.actors.Where(a=>a.player && a.connected).OrderBy(a=>a.character,StringComparer.Ordinal).ToArray();
            string driver=players.Single(a=>runtime.Options.ProbePeers==1 || a.character=="session-client-1").id;
            var placements=new List<Placement>();
            foreach(var player in players.OrderBy(a=>a.id==driver?0:1))
                placements.Add(new Placement {actor=player.id,position=SelectFixturePosition(world,npc,player,driver,placements)});
            fixture=new Fixture {world=world.worldId,driver=driver,target=Target,type=Type,initialNpc=npc.Copy(),players=placements.ToArray()};
            fixtureSent=true;NetworkServer.SendToAll(new ActorDeathProbeFixture {json=StateJson.ToJson(fixture)});
        }
        private Vector2 SelectFixturePosition(WorldState world,ActorState npc,ActorState player,string driver,List<Placement> placements)
        {
            var room=runtime.Geometry.Get(npc.level);var hull=Anatomy.Movement(player);
            float start=Type=="radroach"?(player.id==driver?1.1f:2.2f):(player.id==driver?1.6f:2.7f);
            float direction=Type=="radroach"?1:-1;
            float minimum=(hull.width+Anatomy.Movement(npc).width)*.5f;
            var distances=Enumerable.Range(0,26).Select(step=>minimum+step*.2f).Append(start).Distinct().OrderBy(distance=>Mathf.Abs(distance-start));
            // Up raises the weapon through normal input, but also catches native ladders.
            // Select the initial floor fixture once; never move the NPC or suppress climbing.
            foreach(float side in new[]{direction,-direction})
            foreach(float distance in distances)
            {
                var position=npc.Position+Vector2.right*(side*distance);
                if(!ActorTerrainMotion.IsClear(room,hull,position) || !ActorTerrainMotion.Supported(room,hull,position) || !ClearOfLadder(room,position))continue;
                var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
                if(placements.Any(p=>Mathf.Abs(p.position.x-position.x)<1.1f))continue;
                if(WorldProps.All(world).Any(prop=>prop.level==npc.level && prop.hasPhysicalBody &&
                    body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height))))continue;
                return position;
            }
            throw new InvalidOperationException($"Нет свободной опоры вне хвата лестницы: actor={player.id}, target={npc.id}, position={npc.Position}, start={start}.");
        }
        private static bool ClearOfLadder(RoomLayout room,Vector2 position)=>!room.ladders.Any(l=>
            Mathf.Abs(position.x-l.x-l.movementOffset)<.8f && position.y>=l.bottom-.1f && position.y<l.top+.2f);
        private void Place()
        {
            Require(fixture.world==runtime.Session.World.worldId && fixture.target==Target && fixture.type==Type,"Изменились мир/тип/ID fixture.");
            var position=fixture.players.Single(p=>p.actor==Actor.id).position;var room=runtime.Geometry.Get(Actor.level);var hull=Anatomy.Movement(Actor);
            Require(ActorTerrainMotion.IsClear(room,hull,position) && ActorTerrainMotion.Supported(room,hull,position),"Нет реальной свободной опоры player fixture: "+position);
            Require(ClearOfLadder(room,position),"Начальная опора player fixture попала в хват лестницы: "+position);
            var body=new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height);
            foreach(var prop in WorldProps.All(runtime.Session.World))
                if(prop.level==Actor.level && prop.hasPhysicalBody)
                    Require(!body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),"Начальное тело пересекает груз: "+prop.id);
            before=ReadBalance();Require(before.rounds==12 && before.reserve==36,"Нужен обычный неизменённый fresh боезапас12+36; предметы не добавляются.");
            Actor.Position=position;Actor.Velocity=Vector2.zero;Actor.climbing=false;Actor.pose="idle";Actor.poseTime=0;Actor.revision++;
            runtime.Session.PublishActor(Actor);placed=true;placedAt=Time.unscaledTime;
            Debug.Log($"Явное начальное размещение audio-death: actor={Actor.id}, target={Target}, position={position}; NPC/HP/патроны/terrain не изменены.");
        }
        private Balance ReadBalance()
        {
            var item=Actor.inventory.Single(i=>i.definition=="pistol" && i.count==1 && i.weaponState!=null);
            return new Balance {item=item.id,ammo="p10",rounds=item.weaponState.rounds,reserve=InventoryJournal.Count(Actor,"p10"),revision=item.weaponState.revision,uses=Actor.inventoryUses.Count,health=Actor.health};
        }
        private void ValidateBalance()
        {
            var after=ReadBalance();Require(after.item==before.item && before.rounds+before.reserve-after.rounds-after.reserve==attacks.Count &&
                after.revision-before.revision==attacks.Count+reloads && after.health>0,"Фактический боезапас/перезарядка разошлись с AttackReceived: "+Context());
        }
        private void Changed(){if(Npc is {down:true,health:<=0})changes++;}
        private void Attack(AttackEvent attack)
        {
            if(fixture==null || attack.attacker!=fixture.driver || attack.weapon!="pistol")return;
            Guard(()=>{Require(attack.level=="factory:0" && !attacks.ContainsKey(attack.id),"Неверный/повторный запуск.");attacks.Add(attack.id,StateJson.Copy(attack));});
        }
        private void Reload(ActorState actor,string weapon){if(Driver && actor.id==fixture.driver && weapon=="pistol")reloads++;}
        private void Damage(DamageResult result){if(result.actor==Target)outcomes.Add(result);}
        private void Death(DamageResult result,string resource,AudioClip clip)=>Guard(()=>
        {
            if(result.actor!=Target)return;
            Require(!Resume && ++cues==1 && result.confirmedNpcDeath && result.actorType==Type && result.world==runtime.Session.World.worldId,"Повторный/исторический/чужой голос смерти.");
            string prefix="Original/Audio/ActorDeath/"+(Type=="raider"?"rm":"bloat");
            Require(resource.StartsWith(prefix,StringComparison.Ordinal) && clip!=null && clip.samples>0,"Голос не принадлежит точному банку: "+resource);
            var voices=runtime.Audio.GetComponentsInChildren<AudioSource>().Where(v=>v.clip==clip && voiceIds.Add(v.GetInstanceID())).ToArray();
            Require(voices.Length==1,"Нет единственного реального AudioSource выбранного clip.");
            var source=voices[0];var position=new Vector2(result.x,result.y);var listener=Actor.BodyPosition;
            float expected=Mathf.Clamp01(runtime.Audio.Volume)*GameplayAudio.Attenuation(Vector2.Distance(position,listener));
            Require(source.volume>0 && Mathf.Abs(source.volume-expected)<.0001f && source.mute==Application.isBatchMode,"Реальная громкость/беззвучный batch режим неверны.");
            voice=new Voice {id=source.GetInstanceID(),resource=resource,clip=clip.name,samples=clip.samples,channels=clip.channels,frequency=clip.frequency,
                volume=source.volume,expectedVolume=expected,muted=source.mute,playing=source.isPlaying,position=position,listener=listener};captureDeath=true;
        });
        private void LateUpdate()=>Guard(()=>
        {
            if(captureDeath && Npc is {down:true,health:<=0} && !runtime.IsLevelPreparing && DrawnPlayerReady() && InView())
            {Capture(2);captureDeath=false;}
        });
        private void Capture(int phase)
        {
            NetworkSceneRecord.Save(runtime,phase);
            string path=runtime.Options.Report+$".phase-{phase}.death.png";
            NetworkFrameCapture.Save(Camera.main,path,runtime.Hud.GetComponent<Canvas>(),1920,1080);frames.Add(path);
        }
        private bool InView()
        {
            if(Camera.main==null || Npc==null)return false;
            var point=Camera.main.WorldToViewportPoint(Npc.BodyPosition);
            return point.z>0 && point.x>.05f && point.x<.95f && point.y>.05f && point.y<.95f;
        }
        private bool DrawnPlayerReady()=>Actor!=null && runtime.FindActorVisual(Actor.id) is {} visual &&
            visual.gameObject.activeInHierarchy && visual.TryGetDrawnBounds(out _);
        private void CompleteArchive()
        {
            var message=pendingFinish.Value;
            archive=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",fixture.world,"Snapshots",message.snapshot+".json");
            if(!File.Exists(archive))return;
            var snapshot=new RecoverySnapshot(File.ReadAllText(archive));var npc=snapshot.CreateLiveWorld().Find(Target);
            Require(snapshot.Id==message.snapshot && snapshot.WorldId==fixture.world && StateJson.ToJson(npc)==message.npcJson,"Собственный immutable archive не подтверждает итоговое тело.");
            Finish(true,message.detail);
        }
        private void StepResume()
        {
            Require(Npc is {down:true,health:<=0} && Npc.actorType==Type,"Загрузка воскресила NPC или потеряла тип.");
            Require(runtime.DeathAudio.Accepted==0 && runtime.DeathAudio.Played==0 && cues==0,"Загрузка down baseline воспроизвела смерть.");
            if(runtime.IsLevelPreparing || runtime.ActiveRoomLighting is not {IsReady:true,IsUpdating:false})return;
            if(resumeAt<0)
            {
                if(!DrawnPlayerReady() || !InView())return;
                resumeAt=Time.unscaledTime;Capture(3);
            }
            if(Host && repeated<3 && Time.unscaledTime-resumeAt>(repeated+1)*.35f){runtime.Session.CommitWorld();repeated++;}
            if(Time.unscaledTime-resumeAt>=2 && repeated==3 && changes>=3)Finish(true,"Новый процесс загрузил down baseline; три обычных снимка не воспроизвели голос.");
        }
        private string Context()=>$"type={Type}, actor={Actor?.id}, HP={Actor?.health}, npcHP={Npc?.health}, pos={Actor?.Position}, climbing={Actor?.climbing}, lift={Actor?.weaponLift}, placed={placed}, started={started}, shots={attacks.Count}, reloads={reloads}, cue={cues}, changed={changes}, repeat={repeated}";
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException("Голос смерти NPC: "+reason);}
        private void Guard(Action action){if(finished)return;try{action();}catch(Exception error){Fail(error.Message);}}
        private void Fail(string detail)
        {
            if(finished)return;
            if(Host && NetworkServer.active)NetworkServer.SendToAll(new ActorDeathProbeFinish {success=false,detail=detail});
            else if(NetworkClient.isConnected)NetworkClient.Send(new ActorDeathProbeAck {actor=Actor?.id,success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished)return;finished=true;
            try{runtime.Session.SaveLocalCharacter();runtime.Session.FlushSaveWrites();}catch(Exception error){success=false;detail+="; "+error.Message;}
            var report=new Report {success=success,resume=Resume,driver=Driver,participants=runtime.Options.ProbePeers,frameRate=runtime.Options.ProbeFrameRate,
                explicitInitialPositionFixture=placed,detail=detail+"; "+Context(),type=Type,target=Target,actor=Actor?.id,world=runtime.Session.World?.worldId,
                fixture=fixture,before=before,after=Actor==null?null:ReadBalance(),finalNpc=Npc?.Copy(),accepted=runtime.DeathAudio.Accepted,played=runtime.DeathAudio.Played,
                cues=cues,snapshotChanges=changes,repeatedSnapshots=repeated,shots=attacks.Count,reloads=reloads,captured=frames.Count>0,archiveVerified=!Resume && archive!=null,
                snapshot=pendingFinish?.snapshot,archive=archive,saveDirectory=runtime.Options.SaveDirectory,characterDirectory=runtime.Session.Saves.CharacterDirectoryPath,
                worldDirectory=runtime.Session.Saves.WorldDirectoryPath,outcomes=outcomes.ToArray(),attacks=attacks.Values.ToArray(),voices=voice==null?Array.Empty<Voice>():new[]{voice},frames=frames.ToArray()};
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));
            if(success)Debug.Log("NetworkActorDeathAudioProbe PASS: "+report.detail);else Debug.LogError("NetworkActorDeathAudioProbe FAIL: "+report.detail);
            StartCoroutine(Quit(success));
        }
        private IEnumerator Quit(bool success){yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        private void OnDestroy()
        {
            AudioListener.volume=listenerVolume;if(runtime==null)return;
            runtime.Session.ClientReady-=RegisterClient;runtime.Session.ServerReady-=RegisterServer;runtime.Session.Changed-=Changed;runtime.Session.AttackReceived-=Attack;
            runtime.DeathAudio.DeathPlayed-=Death;runtime.DamageFeedback.Received-=Damage;runtime.Combat.Reload.Started-=Reload;
        }
    }
}
