using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct SurfaceAudioReady : NetworkMessage {public int phase;}
    public struct SurfaceAudioPhase : NetworkMessage {public int phase;public string source;}
    public struct SurfaceAudioRelease : NetworkMessage {public int phase;}
    public struct SurfaceAudioObserved : NetworkMessage {public int phase;public bool success;public string detail;}
    public struct SurfaceAudioFinish : NetworkMessage {public bool success;public string detail;}

    // Opt-in fixtures change only each player's starting position. Travel, aiming,
    // firing, sweeps, terrain contacts and audio remain the production paths.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkSurfaceAudioProbe : MonoBehaviour
    {
        private sealed class Fixture
        {
            public readonly string Level,Texture,Clip,MeleeWeapon;
            public readonly Vector2 Position,Aim;
            public readonly Rect Bounds;
            public readonly float Gain;
            public Fixture(string level,string texture,string clip,Vector2 position,Vector2 aim,Rect bounds,float gain,string meleeWeapon)
            {Level=level;Texture=texture;Clip=clip;Position=position;Aim=aim;Bounds=bounds;Gain=gain;MeleeWeapon=meleeWeapon;}
        }
        // TrainingRoom is nested at +38.4 in the saved playable hub. Factory18
        // is nested at +38.4 in the historical connected factory (LegacyFactoryFixture). Wood is a
        // three-cell column, not a prop or a synthetic material test rectangle.
        // The wood fixture deliberately uses .15 m of the existing shelf at
        // 41.6..44.8. Concrete is an exposed office floor; the enclosed rock
        // inside Factory46 is not claimed as tested by these six phases.
        // The wood phase uses a horizontal spear thrust. The observed native
        // crowbar arc contacts wood and then the connected exterior concrete
        // floor: those are two legitimate material sounds, not duplicates.
        private static readonly Fixture[] Fixtures={
            new("hub","TrainingRoom/steel","hit-metal",new Vector2(59.2f,1.6f),new Vector2(59.2f,1.2f),new Rect(38.4f,.8f,38.4f,.8f),.4f,"crowbar"),
            new("factory:0","Factory18/8_tWood","hit-wood",new Vector2(45.15f,.8f),new Vector2(46.6f,1.6f),new Rect(46.4f,.8f,.8f,2.4f),.5f,"spear"),
            new("factory:0","Factory53/24_tConcreteD","hit-concrete",new Vector2(99.6f,7.2f),new Vector2(99.6f,6.8f),new Rect(98.4f,6.4f,2.4f,.8f),.5f,"crowbar")};
        [Serializable] private sealed class ProjectileEntry {public string kind,key,clip;public float gain;}
        [Serializable] private sealed class ProjectileCatalog {public ProjectileEntry[] entries;}
        [Serializable] private sealed class MeleeEntry {public string weapon,clip;public string[] surfaces;public float gain;}
        [Serializable] private sealed class MeleeCatalog {public MeleeEntry[] entries;}
        [Serializable] private sealed class Voice
        {public int id,samples,channels,frequency;public string clip;public float volume,expectedVolume;public bool muted,playing;}
        [Serializable] private sealed class Witness
        {
            public int phase,baseline,after,initialRounds,finalRounds,width,height;
            public string source,observer,level,texture,weapon,receipt,clipResource,geometry;
            public Vector2 fixture,aim,contact,listenerAnchor,listenerBody;public Rect targetBounds;
            public bool captured;public float quietSeconds;public Voice[] voices;
        }
        [Serializable] private sealed class Report
        {public bool success,batchMuted;public string detail,context,world,observer;public float listenerVolume;public Witness[] proofs;}
        private PrototypeRuntime runtime;
        private Fixture current;
        private AudioClip clip;
        private Witness witness;
        private int phase,serverPhase,baseline,lastAudio;
        private float deadline,phaseAt,evidenceAt=-1,placedAt,oldListenerVolume;
        private string source,worldId,contents,geometry;
        private bool finished,configured,placed,travelRequested,ready,released,action,captured,reported,capturePending;
        private readonly List<Witness> proofs=new();
        private readonly HashSet<int> voices=new();
        private readonly HashSet<string> receipts=new(StringComparer.Ordinal);
        private readonly Dictionary<int,HashSet<int>> readiness=new(),observations=new();
        private readonly HashSet<int> releases=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Session.LocalActorId==source;
        private bool Projectile=>phase%2==1;
        private string Weapon=>Projectile?"pistol":current.MeleeWeapon;
        private int Count=>Projectile?runtime.ImpactAudio.Played:runtime.MeleeAudio.SurfacePlayed;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+125;
            Require(value.Hud!=null && (value.Options.Smoke=="surface-audio-frames" || value.Options.Smoke.StartsWith("surface-audio-frames-",StringComparison.Ordinal)) && !string.IsNullOrEmpty(value.Options.Report) &&
                !string.IsNullOrEmpty(value.Options.SaveDirectory) && value.Options.ProbePeers is 1 or 2,
                "Нужны surface-audio-frames, HUD, отчёт, отдельное сохранение и 1–2 участника.");
            oldListenerVolume=AudioListener.volume;AudioListener.volume=0;
            if(Host)
            {
                Require(value.Session.Saves.SelectedSnapshot()==null,"Существующий мир не заменяется проверкой.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            ValidateMappings();
            value.CommandSource=Command;
            value.Combat.Projectiles.ContactObserved+=ProjectileContact;
            value.Combat.SurfaceContactObserved+=MeleeContact;
            value.Session.Travel.Replied+=reply=>{if(!reply.accepted && phase>0) Fail("Переход отклонён: "+reply.reason);};
            value.Session.ClientReady+=RegisterClient;
            value.Session.ServerReady+=RegisterServer;
        }
        private void ValidateMappings()
        {
            var projectileAsset=GameAssets.Load<TextAsset>("Presentation/projectile-audio");
            var meleeAsset=GameAssets.Load<TextAsset>("Presentation/melee-impact-audio");
            Require(projectileAsset!=null && meleeAsset!=null,"Нет каталогов ударных звуков.");
            var p=JsonUtility.FromJson<ProjectileCatalog>(projectileAsset.text);
            var m=JsonUtility.FromJson<MeleeCatalog>(meleeAsset.text);
            Require(p?.entries!=null && m?.entries!=null,"Нет записей ударных звуков.");
            foreach(var f in Fixtures)
            {
                var pe=p.entries.Where(e=>e.kind=="surface" && e.key==f.Texture).ToArray();
                var me=m.entries.Where(e=>e.weapon==f.MeleeWeapon && e.surfaces?.Contains(f.Texture)==true).ToArray();
                Require(pe.Length==1 && me.Length==1 && pe[0].clip=="Original/Audio/"+f.Clip && me[0].clip==pe[0].clip &&
                    Mathf.Abs(pe[0].gain-f.Gain)<.00001f && Mathf.Abs(me[0].gain-f.Gain)<.00001f,
                    "Нет единственного точного назначения pistol/"+f.MeleeWeapon+": texture="+f.Texture);
                var audio=GameAssets.Load<AudioClip>(pe[0].clip);
                Require(audio!=null && audio.samples>0 && audio.channels>0 && audio.frequency>0,"Нет непустого импортированного WAV: "+pe[0].clip);
            }
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<SurfaceAudioPhase>(m=>Begin(m.phase,m.source));
            NetworkClient.RegisterHandler<SurfaceAudioRelease>(m=>{if(m.phase==phase)released=true;});
            NetworkClient.RegisterHandler<SurfaceAudioFinish>(m=>Finish(m.success,m.detail));
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<SurfaceAudioReady>((connection,m)=>
            {
                if(m.phase!=serverPhase || runtime.Session.ConnectedActor(connection)==null) return;
                if(!readiness.TryGetValue(m.phase,out var peers))readiness.Add(m.phase,peers=new());
                peers.Add(connection.connectionId);
                if(peers.Count!=runtime.Options.ProbePeers)return;
                if(m.phase==0)Advance(1);
                else if(releases.Add(m.phase))NetworkServer.SendToAll(new SurfaceAudioRelease {phase=m.phase});
            });
            NetworkServer.RegisterHandler<SurfaceAudioObserved>((connection,m)=>
            {
                if(m.phase!=serverPhase || runtime.Session.ConnectedActor(connection)==null)return;
                if(!m.success){NetworkServer.SendToAll(new SurfaceAudioFinish {success=false,detail=m.detail});return;}
                if(!observations.TryGetValue(m.phase,out var peers))observations.Add(m.phase,peers=new());
                peers.Add(connection.connectionId);
                if(peers.Count!=runtime.Options.ProbePeers)return;
                if(m.phase==6)NetworkServer.SendToAll(new SurfaceAudioFinish {success=true,detail="Шесть обычных контактов pistol/crowbar/spear с реальными steel/wood/concrete; каждый участник подтвердил точные голоса и кадры."});
                else Advance(m.phase+1);
            });
        }
        private void Advance(int next)
        {serverPhase=next;NetworkServer.SendToAll(new SurfaceAudioPhase {phase=next,source=runtime.Session.LocalActorId});}
        private void Begin(int next,string sender)
        {
            Require(next is >=1 and <=6 && next==phase+1,"Нарушен порядок фаз: "+next+" после "+phase);
            phase=next;source=sender;current=Fixtures[(phase-1)/2];phaseAt=Time.unscaledTime;evidenceAt=-1;
            placed=travelRequested=ready=released=action=captured=reported=capturePending=false;
            clip=GameAssets.Load<AudioClip>("Original/Audio/"+current.Clip);
            receipts.Clear();voices.Clear();
            foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>())voices.Add(voice.GetInstanceID());
            baseline=lastAudio=Count;
            witness=new Witness {phase=phase,source=source,observer=runtime.Session.LocalActorId,level=current.Level,texture=current.Texture,
                weapon=Weapon,clipResource="Original/Audio/"+current.Clip,targetBounds=current.Bounds,aim=current.Aim,baseline=baseline};
            Debug.Log($"Ударный звук поверхности: phase={phase}, texture={current.Texture}, weapon={Weapon}, source={source}.");
        }
        private PlayerCommand Command()
        {
            if(finished || phase==0 || !placed || runtime.Session.LocalActor==null)return default;
            var command=new PlayerCommand {hasAim=true,aim=current.Aim};
            if(Source)
            {
                command.weapon=Weapon;
                if(released && !action){action=true;command.fire=true;}
            }
            return command;
        }
        private void Update()
        {
            if(finished)return;
            try
            {
                Require(Time.unscaledTime<=deadline && (phase==0 || Time.unscaledTime-phaseAt<=20),"Истёк срок ожидания фазы/контакта.");
                var session=runtime.Session;var actor=session.LocalActor;
                if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers)return;
                if(Host && !configured){configured=true;session.SetFriendlyFire(false);}
                if(session.World.friendlyFire)return;
                if(worldId==null)worldId=session.World.worldId;
                Require(session.World.worldId==worldId && !actor.down && runtime.Audio.Volume>0 && AudioListener.volume==0,
                    "Изменился мир/состояние игрока или громкость проверки.");
                Require(Screen.width==1920 && Screen.height==1080,"Кадры требуют фактический viewport1920×1080.");
                Require(WorldRooms.Definition(session.World,"factory:0")==LegacyFactoryFixture.DefinitionId,"Нужен исторический мир со старым собранным заводом (LegacyFactoryFixture).");
                if(phase==0){if(!ready){ready=true;NetworkClient.Send(new SurfaceAudioReady {phase=0});}return;}
                if(actor.level!=current.Level)
                {
                    if(!travelRequested){travelRequested=true;if(current.Level=="hub")session.Travel.RequestHub();else session.Travel.RequestFactory();}
                    return;
                }
                if(session.Travel.Pending)return;
                Require(!session.Pause.IsPaused(actor.level),"Уровень неожиданно приостановлен.");
                if(!placed)PlaceFixture(actor);
                if(!ready && Time.unscaledTime-placedAt>=.5f)
                {
                    var sender=session.World.Find(source);
                    if(sender==null || sender.level!=current.Level || sender.weapon!=Weapon || sender.attackBusy || sender.attackCooldown>0)return;
                    Require(Vector2.Distance(actor.Position,witness.fixture)<.025f && Mathf.Abs(actor.vx)+Mathf.Abs(actor.vy)<.01f,
                        "Начальная позиция не осталась на реальной опоре.");
                    ready=true;NetworkClient.Send(new SurfaceAudioReady {phase=phase});
                }
            }
            catch(Exception error){Fail(error.Message);}
        }
        private void PlaceFixture(ActorState actor)
        {
            var room=runtime.Geometry.Get(current.Level);var hull=Anatomy.Movement(actor);
            var position=current.Position+(Host?Vector2.zero:Vector2.left*.15f);
            Require(ActorTerrainMotion.IsClear(room,hull,position) && ActorTerrainMotion.Supported(room,hull,position),
                "Нет свободной опоры для явной fixture: texture="+current.Texture+", position="+position);
            var real=room.platforms.Where(s=>s.texture==current.Texture && !s.shotsPassThrough && !s.oneWay && s.slopeDirection==0 &&
                Mathf.Abs(s.x-current.Bounds.x)<.0001f && Mathf.Abs(s.width-current.Bounds.width)<.0001f &&
                s.y>=current.Bounds.yMin-.0001f && s.y+s.height<=current.Bounds.yMax+.0001f).ToArray();
            Require(real.Length==(current.Texture=="Factory18/8_tWood"?3:1) && Mathf.Abs(real.Sum(s=>s.height)-current.Bounds.height)<.0001f,
                "Не совпал точный набор authored solids: texture="+current.Texture+", bounds="+current.Bounds);
            foreach(var prop in WorldProps.All(runtime.Session.World).Where(p=>p.level==current.Level && p.hasPhysicalBody))
                Require(!Overlap(new Rect(position.x-hull.width*.5f,position.y,hull.width,hull.height),
                    new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)),"Fixture пересекает настоящий груз: "+prop.id);
            Require(actor.inventory.Count(i=>i.definition=="pistol" && i.count==1 && i.weaponState!=null)==1 &&
                actor.inventory.Count(i=>i.definition==current.MeleeWeapon && i.count==1 && i.weaponState!=null)==1,
                "Нужен обычный стартовый pistol/"+current.MeleeWeapon+"; проверка предметы не добавляет.");
            geometry=StateJson.ToJson(room.platforms);contents=ContainerContents();
            actor.Position=position;actor.vx=actor.vy=0;actor.revision++;runtime.Session.PublishActor(actor);
            witness.fixture=position;witness.geometry=Remains.Persistence.RecoverySnapshot.Fingerprint(geometry);
            witness.initialRounds=ProbeWeaponSelection.Magazine(actor,"pistol").rounds;
            Require(!Projectile || witness.initialRounds>0,"Стартовый пистолет пуст; проверка патроны не добавляет.");
            placed=true;placedAt=Time.unscaledTime;
            Debug.Log($"Явная начальная позиция surface-audio: actor={actor.id}, phase={phase}, position={position}; surfaces/props/NPC не изменены.");
        }
        private void ProjectileContact(ProjectileContact contact)
        {
            if(finished || !Projectile || !released || !placed)return;
            var launch=runtime.Combat.Projectiles.Flights.FirstOrDefault(f=>f.state.launch.id==contact.projectile)?.state.launch;
            if(launch?.attacker!=source || contact.weapon!=Weapon || contact.level!=current.Level)return;
            if(contact.target!=null){Fail("Обычный выстрел попал в актёра вместо выбранного solid: "+contact.target);return;}
            Observe(contact.projectile,contact.surfaceTexture,contact.position);
        }
        private void MeleeContact(AttackEvent attack,string texture,Vector2 position)
        {
            if(finished || Projectile || !released || !placed || attack.attacker!=source || attack.weapon!=Weapon || attack.level!=current.Level)return;
            Observe(attack.id,texture,position);
        }
        private void Observe(string receipt,string texture,Vector2 position)
        {
            try
            {
                int delta=Count-lastAudio;lastAudio=Count;
                var fresh=runtime.Audio.GetComponentsInChildren<AudioSource>().Where(v=>voices.Add(v.GetInstanceID()) && v.clip==clip).ToArray();
                Require(texture==current.Texture && position.x>=current.Bounds.xMin-.001f && position.x<=current.Bounds.xMax+.001f &&
                    position.y>=current.Bounds.yMin-.001f && position.y<=current.Bounds.yMax+.001f,
                    "Контакт с другим authored solid: receipt="+receipt+", texture="+texture+", position="+position);
                if(!receipts.Add(receipt)){Require(delta==0 && fresh.Length==0,"Повтор контакта создал новый голос: "+receipt);return;}
                Require(receipts.Count==1 && delta==1 && fresh.Length==1,"Контакт не создал единственный нужный AudioSource: receipt="+receipt+", delta="+delta+", voices="+fresh.Length);
                var listener=runtime.Session.LocalActor;var listenerBody=listener.BodyPosition;
                float expected=Mathf.Clamp01(runtime.Audio.Volume)*current.Gain*Remains.Presentation.GameplayAudio.Attenuation(Vector2.Distance(position,listenerBody));
                var voice=fresh[0];
                Require(voice.volume>0 && Mathf.Abs(voice.volume-expected)<.0001f && voice.clip.samples>0,"Реальная громкость/импорт не соответствуют contact mapping: "+receipt);
                witness.receipt=receipt;witness.contact=position;witness.listenerAnchor=listener.Position;witness.listenerBody=listenerBody;
                witness.voices=new[]{new Voice {id=voice.GetInstanceID(),clip=voice.clip.name,
                    samples=voice.clip.samples,channels=voice.clip.channels,frequency=voice.clip.frequency,volume=voice.volume,expectedVolume=expected,muted=voice.mute,playing=voice.isPlaying}};
                evidenceAt=Time.unscaledTime;capturePending=true;
            }
            catch(Exception error){Fail(error.Message);}
        }
        private void LateUpdate()
        {
            if(finished || phase==0 || !placed)return;
            try
            {
                if(capturePending)
                {
                    capturePending=false;NetworkSceneRecord.Save(runtime,phase);
                    NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+$".phase-{phase}.surface.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                    captured=true;witness.captured=true;witness.width=1920;witness.height=1080;
                }
                if(evidenceAt<0 || !captured || reported || Time.unscaledTime-evidenceAt<.75f)return;
                Require(Count-baseline==1 && receipts.Count==1,"Нарушена дедупликация контакта/голоса.");
                Require(ContainerContents()==contents,"Проверка ударного звука изменила общие предметы.");
                Require(StateJson.ToJson(runtime.Geometry.Get(current.Level).platforms)==geometry,"Изменилась реальная геометрия во время контакта.");
                witness.finalRounds=ProbeWeaponSelection.Magazine(runtime.Session.LocalActor,"pistol").rounds;
                Require(witness.finalRounds==witness.initialRounds-(Projectile && Source?1:0),"Расход патрона не совпал с одним обычным fire только источника.");
                witness.after=Count;witness.quietSeconds=Time.unscaledTime-evidenceAt;
                File.WriteAllText(runtime.Options.Report+$".phase-{phase}.audio.json",StateJson.ToJson(witness,true));
                proofs.Add(witness);reported=true;NetworkClient.Send(new SurfaceAudioObserved {phase=phase,success=true,detail=current.Texture+"/"+Weapon});
            }
            catch(Exception error){Fail(error.Message);}
        }
        private string ContainerContents()=>StateJson.ToJson(runtime.Session.World.containers.OrderBy(c=>c.id,StringComparer.Ordinal).Select(c=>new {c.id,c.items}).ToArray());
        private static bool Overlap(Rect a,Rect b)=>Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.0001f && Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.0001f;
        private static void Require(bool valid,string reason){if(!valid)throw new InvalidOperationException("Ударный звук поверхности: "+reason);}
        private string Context()=>$"phase={phase}, source={source}, level={runtime.Session.LocalActor?.level}, body={runtime.Session.LocalActor?.Position}, texture={current?.Texture}, ready={ready}, release={released}, fire={action}, receipts={receipts.Count}, voices={(phase>0?Count:0)}/{baseline}";
        private void Fail(string reason)
        {
            if(finished)return;
            string detail=reason+" "+Context();
            if(NetworkClient.active)NetworkClient.Send(new SurfaceAudioObserved {phase=phase,success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished)return;
            if(success && proofs.Count!=6){success=false;detail="Нет шести локальных доказательств; "+detail;}
            finished=true;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,context=Context(),world=worldId,
                observer=runtime.Session.LocalActorId,listenerVolume=AudioListener.volume,batchMuted=Application.isBatchMode,proofs=proofs.ToArray()},true));
            Debug.Log("Ударные звуки реальных поверхностей: "+success+", "+detail);StartCoroutine(Quit(success));
        }
        private IEnumerator Quit(bool success){yield return new WaitForSecondsRealtime(1.5f);Application.Quit(success?0:1);}
        private void OnDestroy()
        {
            AudioListener.volume=oldListenerVolume;
            if(runtime==null)return;
            runtime.Combat.Projectiles.ContactObserved-=ProjectileContact;runtime.Combat.SurfaceContactObserved-=MeleeContact;
        }
    }
}
