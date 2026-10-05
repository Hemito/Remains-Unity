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
    public struct ContactAudioReady : NetworkMessage {public int phase;}
    public struct ContactAudioPhase : NetworkMessage {public int phase;public string source;}
    public struct ContactAudioRelease : NetworkMessage {public int phase;}
    public struct ContactAudioObserved : NetworkMessage {public int phase;public bool success;public string detail;}
    public struct ContactAudioFinish : NetworkMessage {public bool success;public string detail;}

    // Two explicit starting-position fixtures in the ordinary authored hub. Props are never injected or moved by the probe.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkContactAudioProbe : MonoBehaviour
    {
        private const string MetalTarget="training:original:WEqCysOP6BwxoT0g";
        private const string Wood="training:original:XukaLWGZalwkBlBU";
        private const string Light="training:crate:1";
        private const string Base="training:original:FhBo7HQlq39jvPDu";
        private PrototypeRuntime runtime;
        private int phase,serverPhase,baseline,lastObservedAudio,initialRounds;
        private float deadline,phaseAt,evidenceAt=-1;
        private bool fixture,configured,ready,released,action,captured,capturePending,reported,finished;
        private string source,contents,worldId;
        private Vector2 fixturePosition,crateStart,floorLift,stackLift;
        private AudioClip expectedClip;
        private Witness witness;
        private readonly List<Witness> proofs=new();
        private readonly HashSet<string> receipts=new();
        private readonly HashSet<int> voiceIds=new();
        private readonly Dictionary<int,HashSet<int>> participants=new(),observations=new();
        private readonly HashSet<int> releasedPhases=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Targets=>runtime.Options.Smoke.Contains("-targets-");
        private bool Source=>runtime.Session.LocalActorId==source;
        private bool Attack=>phase>0 && phase<=(Targets?3:2);
        private bool Projectile=>Targets && phase==1;
        private bool Held=>!Targets && phase is 3 or 5;
        private bool Landing=>!Targets && phase is 4 or 6;
        private string Weapon=>Projectile?"pistol":phase==(Targets?2:1)?"crowbar":"spear";
        private string TargetId=>Projectile?MetalTarget:Attack?Targets?Wood:Base:Light;
        private string Label=>Targets?phase==1?"pistol-metal":phase==2?"crowbar-wood":"spear-wood":
            phase==1?"crowbar-metal":phase==2?"spear-metal":phase==3?"floor-held":phase==4?"floor-landing":phase==5?"stack-held":"stack-landing";
        private PropState Prop(string id)=>WorldProps.Find(runtime.Session.World,id)??throw new InvalidOperationException("Нет авторского объекта: "+id);
        private int AudioCount=>Projectile?runtime.ImpactAudio.Played:Attack?runtime.MeleeAudio.PropPlayed:runtime.PropAudio.Landings;
        private Vector2 Lift=>phase is 5 or 6?stackLift:floorLift;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+110;phaseAt=Time.unscaledTime;
            if(value.Hud==null || !value.Options.Smoke.Contains("-frames-") || string.IsNullOrEmpty(value.Options.Report) || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Проверка контактов требует -frames-, игровой HUD, отдельные сохранения и отчёт.");
            if(!value.Options.Smoke.Contains("-targets-") && !value.Options.Smoke.Contains("-props-"))
                throw new InvalidOperationException("Выберите contact-audio-targets-frames или contact-audio-props-frames.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка контактов требует новый мир; существующий сейв не заменяется.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            value.CommandSource=Command;
            value.Combat.Projectiles.ContactObserved+=ProjectileContact;
            value.Combat.PropContactObserved+=PropContact;
            value.Session.Notice+=Notice;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ContactAudioPhase>(m=>Begin(m.phase,m.source));
                NetworkClient.RegisterHandler<ContactAudioRelease>(m=>{if(m.phase==phase) released=true;});
                NetworkClient.RegisterHandler<ContactAudioFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.ServerReady+=()=>
            {
                NetworkServer.RegisterHandler<ContactAudioReady>((connection,m)=>
                {
                    if(m.phase!=serverPhase || value.Session.ConnectedActor(connection)==null) return;
                    if(!participants.TryGetValue(m.phase,out var peers)) participants.Add(m.phase,peers=new());
                    peers.Add(connection.connectionId);
                    if(peers.Count!=value.Options.ProbePeers) return;
                    if(m.phase==0) Advance(1);
                    else if(releasedPhases.Add(m.phase)) NetworkServer.SendToAll(new ContactAudioRelease {phase=m.phase});
                });
                NetworkServer.RegisterHandler<ContactAudioObserved>((connection,m)=>
                {
                    if(m.phase!=serverPhase || value.Session.ConnectedActor(connection)==null) return;
                    if(!m.success) {NetworkServer.SendToAll(new ContactAudioFinish {success=false,detail=m.detail});return;}
                    if(!observations.TryGetValue(m.phase,out var peers)) observations.Add(m.phase,peers=new());
                    peers.Add(connection.connectionId);
                    if(peers.Count!=value.Options.ProbePeers) return;
                    if(m.phase==(Targets?3:6)) NetworkServer.SendToAll(new ContactAudioFinish {success=true,
                        detail="Обычные команды в авторском хабе; все участники независимо подтвердили события, реальные голоса, кадры и отсутствие повторов."});
                    else Advance(m.phase+1);
                });
            };
        }
        private void Advance(int next)
        {
            serverPhase=next;
            NetworkServer.SendToAll(new ContactAudioPhase {phase=next,source=runtime.Session.LocalActorId});
        }
        private void Begin(int next,string sender)
        {
            phase=next;source=sender;phaseAt=Time.unscaledTime;evidenceAt=-1;
            ready=released=action=captured=capturePending=reported=false;receipts.Clear();voiceIds.Clear();
            foreach(var voice in runtime.Audio.GetComponentsInChildren<AudioSource>()) voiceIds.Add(voice.GetInstanceID());
            expectedClip=Resources.Load<AudioClip>("Original/Audio/"+(Projectile || Attack && !Targets?"hit-metal":Attack?"hit-wood":"crate-metal-fall"));
            if(expectedClip==null || expectedClip.samples<=0) {Fail("Нет ожидаемого импортированного WAV для "+Label);return;}
            baseline=lastObservedAudio=AudioCount;
            witness=new Witness {phase=phase,label=Label,target=TargetId,expectedClip=expectedClip.name,baseline=baseline,source=source};
            if(Held || Landing)
            {
                var bottom=Prop(Base);stackLift=new Vector2(bottom.x,bottom.y-bottom.anchorHeight+bottom.height+.55f+Prop(Light).anchorHeight);
            }
            Debug.Log($"Проверка контактов: mode={(Targets?"targets":"props")}, phase={phase}, label={Label}, target={TargetId}, baseline={baseline}, deadline={deadline}.");
        }
        private void PrepareFixture()
        {
            var session=runtime.Session;var self=session.LocalActor;
            if(WorldRooms.Definition(session.World,"factory:0")!=NewGameWorld.FactoryDefinitionId)
                throw new InvalidOperationException("Контактная проверка получила legacy-мир вместо NewGameWorld; исключите contact-audio из LegacyPrototypeFixture.");
            if(Screen.width!=1920 || Screen.height!=1080) throw new InvalidOperationException($"Для scene/capture нужны 1920×1080, сейчас {Screen.width}×{Screen.height}.");
            if(self.level!="hub" || self.down || runtime.Audio.Volume<=0) throw new InvalidOperationException("Нужен активный персонаж в обычном хабе и ненулевая громкость эффектов.");
            foreach(string definition in new[]{"pistol","crowbar","spear"})
                if(self.inventory.Count(i=>i.definition==definition && i.count==1 && i.weaponState!=null)!=1)
                    throw new InvalidOperationException($"Неверный стартовый инвентарь: actor={self.id}, weapon={definition}. Ничего не добавлено проверкой.");
            initialRounds=ProbeWeaponSelection.Magazine(self,"pistol").rounds;
            if(initialRounds<=0 || TelekinesisSettings.Load().maximumMass!=30) throw new InvalidOperationException("Нужен заряженный стартовый пистолет и обычный предел телекинеза 30 кг.");
            var target=session.World.Find(MetalTarget);
            if(target?.actorType!="training-armored-dummy" || Prop(Wood).definition!="woodbox" || Prop(Light).definition!="mcrate1" || Prop(Base).definition!="mcrate2")
                throw new InvalidOperationException("Авторские устойчивые ID не разрешились в нужные материалы.");
            crateStart=Prop(Light).Position;floorLift=crateStart+Vector2.up*.75f;
            fixturePosition=Targets?target.Position+Vector2.left*1.2f:crateStart+Vector2.right*.95f;
            if(!Host) fixturePosition+=Vector2.left*.15f;
            var room=runtime.Geometry.Get("hub");
            if(!ActorTerrainMotion.IsClear(room,Anatomy.Movement(self),fixturePosition) || !ActorTerrainMotion.Supported(room,Anatomy.Movement(self),fixturePosition))
                throw new InvalidOperationException("Начальная fixture-позиция не свободна или не имеет опоры: "+fixturePosition);
            contents=Contents();worldId=session.World.worldId;
            self.Position=fixturePosition;self.vx=self.vy=0;self.revision++;session.PublishActor(self);fixture=true;
            // Give normal movement, camera and visual updates time to consume this explicit fixture.
            phaseAt=Time.unscaledTime;
            Debug.Log($"Начальная проверочная позиция у реальных объектов: actor={self.id}, position={fixturePosition}, mode={(Targets?"targets":"props")}. Полный маршрут до тира не проверяется; объекты не изменены.");
        }
        private Vector2 Destination()
        {
            if(!Attack) return fixturePosition;
            var target=Projectile?runtime.Session.World.Find(MetalTarget).Position:Prop(TargetId).Position;
            float offset=Projectile || Targets?1.2f:1.05f;
            return target+Vector2.left*(offset+(Host?0:.15f));
        }
        private PlayerCommand Command()
        {
            if(finished || !fixture || phase==0) return default;
            var self=runtime.Session.LocalActor;if(self==null) return default;
            var destination=Destination();float dx=destination.x-self.x;
            var command=new PlayerCommand {movement=Mathf.Abs(dx)>.06f?Vector2.right*Mathf.Sign(dx):Vector2.zero,hasAim=true};
            if(Attack)
            {
                if(Projectile && Source) command.movement.y=1;
                command.aim=Projectile?runtime.Session.World.Find(MetalTarget).Position+Vector2.up*.8f:Prop(TargetId).Center;
                if(Source) {command.weapon=Weapon;if(released && !action) {action=true;command.fire=true;}}
            }
            else if(Source)
            {
                if(Held)
                {
                    command.telekinesis=released;
                    command.aim=!action?Prop(Light).Center:Lift;
                    if(released) action=true;
                }
                else if(Landing) {command.telekinesis=!released;command.aim=Lift;}
            }
            return command;
        }
        private bool Prepared()
        {
            var self=runtime.Session.LocalActor;
            if(Vector2.Distance(self.Position,Destination())>.23f || Time.unscaledTime-phaseAt<.4f) return false;
            if(Attack)
            {
                var sender=runtime.Session.World.Find(source);
                return sender!=null && sender.weapon==Weapon && (!Projectile || sender.weaponLift>.7f) && !sender.attackBusy && sender.attackCooldown<=0;
            }
            if(Landing) return Prop(Light).held && Vector2.Distance(Prop(Light).Position,Lift)<.1f;
            return !Prop(Light).held;
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline || phase>0 && Time.unscaledTime-phaseAt>22) {Fail("Истёк срок ожидания событий/барьера.");return;}
                var session=runtime.Session;var self=session.LocalActor;if(self==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(Host && !configured) {configured=true;session.SetFriendlyFire(false);}
                if(session.World.friendlyFire) return;
                if(!fixture) PrepareFixture();
                if(session.World.worldId!=worldId || self.level!="hub" || session.Travel.Pending || self.down || session.Pause.IsPaused("hub"))
                    {Fail("Изменился мир/уровень или управление персонажем.");return;}
                if(phase==0)
                {
                    if(Time.unscaledTime-phaseAt<.6f || ready) return;
                    NetworkSceneRecord.Save(runtime,0);ready=true;NetworkClient.Send(new ContactAudioReady {phase=0});return;
                }
                if(!ready && Prepared()) {ready=true;NetworkClient.Send(new ContactAudioReady {phase=phase});}
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private void ProjectileContact(ProjectileContact contact)
        {
            if(finished || !Projectile || !released) return;
            int delta=AudioCount-lastObservedAudio;lastObservedAudio=AudioCount;
            var voices=NewVoices();
            if(contact.target!=MetalTarget || contact.targetType!="training-armored-dummy" || contact.weapon!="pistol") return;
            ObserveReceipt(contact.projectile+":"+contact.target,delta,voices,contact.position,contact.targetType);
        }
        private void PropContact(AttackEvent attack,string id,string definition,Vector2 position)
        {
            if(finished || !Attack || Projectile || !released) return;
            int delta=AudioCount-lastObservedAudio;lastObservedAudio=AudioCount;
            var voices=NewVoices();
            if(id!=TargetId || attack.weapon!=Weapon || attack.attacker!=source) return;
            ObserveReceipt(attack.id+":"+id,delta,voices,position,definition);
        }
        private void ObserveReceipt(string receipt,int delta,Voice[] voices,Vector2 position,string type)
        {
            if(!receipts.Add(receipt)) {if(delta!=0) Fail("Повторный контакт создал новый голос: "+receipt);return;}
            if(receipts.Count!=1 || delta!=1 || !voices.Any(v=>v.clip==expectedClip.name && v.volume>0))
                {Fail($"Контакт не создал один нужный голос: receipt={receipt}, delta={delta}, voices={StateJson.ToJson(voices)}.");return;}
            witness.receipt=receipt;witness.type=type;witness.contact=position;
            witness.voices=voices;evidenceAt=Time.unscaledTime;capturePending=true;
        }
        private Voice[] NewVoices()=>runtime.Audio.GetComponentsInChildren<AudioSource>().Where(v=>voiceIds.Add(v.GetInstanceID()))
            .Select(v=>new Voice {id=v.GetInstanceID(),clip=v.clip?.name,volume=v.volume,muted=v.mute,playing=v.isPlaying}).ToArray();
        private void LateUpdate()
        {
            if(finished || phase==0 || !fixture) return;
            try
            {
                if(released && (Held || Landing))
                {
                    var prop=Prop(Light);int count=AudioCount-baseline;var voices=NewVoices();
                    if(Held)
                    {
                        if(count!=0 || voices.Any(v=>v.clip==expectedClip.name)) {Fail("Удержание озвучено как падение.");return;}
                        if(prop.held && prop.owner==runtime.Session.World.Find(source).owner && Vector2.Distance(prop.Position,Lift)<.1f && evidenceAt<0)
                        {evidenceAt=Time.unscaledTime;witness.contact=prop.Position;witness.type=prop.definition;witness.voices=voices;capturePending=true;}
                    }
                    else
                    {
                        if(count>1) {Fail("Одно отпускание создало повторный landing/голос.");return;}
                        if(count==1 && evidenceAt<0)
                        {
                            var feet=prop.Position-Vector2.up*prop.anchorHeight;
                            bool stack=PropSupport.Supported(prop,feet,WorldProps.All(runtime.Session.World));
                            if(prop.held || stack!=(phase==6) || !voices.Any(v=>v.clip==expectedClip.name && v.volume>0))
                                {Fail("Landing не совпал с физической опорой или ожидаемым голосом.");return;}
                            evidenceAt=Time.unscaledTime;witness.contact=prop.Position;witness.type=prop.definition;witness.voices=voices;capturePending=true;
                        }
                        else if(evidenceAt>=0 && voices.Any(v=>v.clip==expectedClip.name)) {Fail("Повторное состояние создало голос падения.");return;}
                    }
                }
                if(capturePending)
                {
                    capturePending=false;NetworkSceneRecord.Save(runtime,phase);
                    NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+$".phase-{phase}.contact.png",runtime.Hud.GetComponent<Canvas>(),1920,1080);
                    captured=true;witness.captured=true;witness.width=1920;witness.height=1080;
                    witness.after=AudioCount;witness.captureTime=Time.unscaledTime;
                    File.WriteAllText(runtime.Options.Report+$".phase-{phase}.audio.json",StateJson.ToJson(witness,true));
                }
                if(evidenceAt<0 || !captured || reported || Time.unscaledTime-evidenceAt<.75f) return;
                if(Contents()!=contents) {Fail("Аудио-сценарий изменил содержимое авторских контейнеров.");return;}
                if(Attack && receipts.Count!=1 || Landing && AudioCount-baseline!=1 || Held && AudioCount!=baseline)
                    {Fail("Контакт исчез или повторился в окне наблюдения.");return;}
                if(Held && (!Prop(Light).held || Vector2.Distance(Prop(Light).Position,Lift)>.1f))
                    {Fail("Ящик не оставался удерживаемым до конца окна наблюдения.");return;}
                if(Projectile && ProbeWeaponSelection.Magazine(runtime.Session.LocalActor,"pistol").rounds!=initialRounds-(Source?1:0))
                    {Fail("Один fire не соответствует расходу одного патрона только отправителем.");return;}
                witness.after=AudioCount;witness.quietSeconds=Time.unscaledTime-evidenceAt;
                File.WriteAllText(runtime.Options.Report+$".phase-{phase}.audio.json",StateJson.ToJson(witness,true));
                proofs.Add(witness);reported=true;NetworkClient.Send(new ContactAudioObserved {phase=phase,success=true,detail=Label});
            }
            catch(Exception error) {Fail(error.Message);}
        }
        private string Contents()=>string.Join("|",runtime.Session.World.containers.Where(c=>c.id==Wood || c.id==Light || c.id==Base)
            .OrderBy(c=>c.id).Select(c=>c.id+":"+StateJson.ToJson(c.items)));
        private void Notice(string text) {if(phase>0 && !finished) Fail("Игровая команда отклонена: "+text);}
        private string Context()=> $"mode={(Targets?"targets":"props")}, phase={phase}/{(phase>0?Label:"fixture")}, target={(phase>0?TargetId:"none")}, player={runtime.Session.LocalActor?.Position}, prop={WorldProps.Find(runtime.Session.World,Light)?.Position}, ready={ready}, released={released}, action={action}, audio={(phase>0?AudioCount:0)}/{baseline}, receipts={receipts.Count}, evidence={evidenceAt}, deadline={deadline}";
        private void Fail(string reason)
        {
            if(finished) return;
            string detail=reason+" "+Context();
            if(NetworkClient.active) NetworkClient.Send(new ContactAudioObserved {phase=phase,success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            int expected=Targets?3:6;
            if(success && proofs.Count!=expected) {success=false;detail=$"Неполный локальный набор доказательств: count={proofs.Count}, expected={expected}. "+detail;}
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,context=Context(),
                mode=Targets?"targets":"props",world=worldId,observer=runtime.Session.LocalActorId,fixturePosition=fixturePosition,
                initialPistolRounds=initialRounds,proofs=proofs.ToArray(),batchMuted=Application.isBatchMode},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1.5f);Application.Quit(success?0:2);}
        private void OnDestroy()
        {
            if(runtime==null) return;
            runtime.Combat.Projectiles.ContactObserved-=ProjectileContact;runtime.Combat.PropContactObserved-=PropContact;runtime.Session.Notice-=Notice;
        }
        [Serializable] private sealed class Voice {public int id;public string clip;public float volume;public bool muted,playing;}
        [Serializable] private sealed class Witness
        {public int phase,baseline,after,width,height;public string label,source,target,type,expectedClip,receipt;public bool captured;public float quietSeconds,captureTime;public Vector2 contact;public Voice[] voices;}
        [Serializable] private sealed class Report
        {public bool success,batchMuted;public string detail,context,mode,world,observer;public Vector2 fixturePosition;public int initialPistolRounds;public Witness[] proofs;}
    }
}
