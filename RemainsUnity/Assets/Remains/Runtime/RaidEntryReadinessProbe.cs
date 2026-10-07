using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Runtime
{
    // Passive observation of a copied real save. The only input is ordinary UI
    // consent, plus the already-ready ally's existing factory travel command.
    [DefaultExecutionOrder(10000)]
    public sealed class RaidEntryReadinessProbe : MonoBehaviour
    {
        [Serializable] private sealed class EventRecord
        {
            public string phase,token,world,actor,level,profileSha256,authoredDefinition,expectedLayout,stagedLayout,lightLayout;
            public double seconds;public int frame;
            public bool worldAbsent,localActorAbsent,pending,prepared,connected,ready;
            public float health;public bool down;public int generation,lifecycle;
        }
        [Serializable] private sealed class DamageRecord
        {
            public string attack,attacker,weapon,level,actor,owner;
            public double seconds;public int frame,generation,lifecycle;
            public float amount,health;public bool down,pending,presentationReady,afterPrepared,afterFirstDraw;
        }
        [Serializable] private sealed class FrameRecord
        {
            public string png,sha256,scene,actor,clip,level;
            public double seconds;public int frame,width,height,drawnFrame,drawnParts,visibleRenderers;
            public int foregroundId,backgroundId;public long revision;
            public Vector3 minimum,maximum;public Vector3[] viewport;
            public float health;public bool down,presentationReady,shaderPairVerified;
        }
        [Serializable] private sealed class ArchiveRecord
        {
            public string id,world,path,sha256;public long revision;
            public ActorState actor;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1,participants,frameRate,pendingFrames;
            public bool success,existingSaveOnly=true,syntheticGameState=false,originalRuntimeVerified=false;
            public bool selectedCopyVerified,profileUnchangedWhilePending,preparedBeforeAdmission,admitted,firstDrawVerified;
            public bool actualKnockdown,partyRecoveryVerified,allyReady,offlineBodyDamageObserved;
            public string mode,detail,character,targetActor,sourceWorld,sourceSnapshot,sourceArchive,sourceSha256,profileBeforePending;
            public ActorState sourceActor,admittedActor,finalActor;
            public double firstPreparedSeconds=-1,admissionSeconds=-1,knockdownSeconds=-1,recoverySeconds=-1;
            public List<EventRecord> events=new();public List<DamageRecord> damage=new();
            public FrameRecord firstFrame;public ArchiveRecord persisted;
        }
        private PrototypeRuntime runtime;private Report report;private RecoverySnapshot selected;
        private WorldState frozenSource;private ActorState frozenActor;private string pendingProfile,token,expectedLayoutId,expectedDefinition;
        private RoomLayout requestedRoom;
        private double start,deadline;private bool finished,preparing,firstDraw,knockedDown,recovered,allyTravelRequested;
        private PonyVisual view;private MeshRenderer[] renderers;private Canvas canvas;
        private readonly Dictionary<string,AttackEvent> attacks=new(StringComparer.Ordinal);
        private readonly HashSet<int> clicked=new();
        private double lastOfflineHealth=double.NaN;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;start=Time.realtimeSinceStartupAsDouble;deadline=start+90;
            string smoke=value.Options.Smoke;
            string mode=smoke.EndsWith("-ally",StringComparison.Ordinal)?"ally":smoke.EndsWith("-incoming",StringComparison.Ordinal)?"incoming":"solo";
            if(string.IsNullOrWhiteSpace(value.Options.SaveDirectory) || string.IsNullOrWhiteSpace(value.Options.Report) ||
                value.Options.ProbePeers!=(mode=="solo"?1:2) || value.Options.Role!=(mode=="incoming"?"client":"host"))
                throw new InvalidOperationException("Проверке входа нужны явная изолированная копия, отчёт и согласованные роль/число участников.");
            selected=value.Session.Saves.SelectedSnapshot()??throw new InvalidOperationException("Проверка входа не создаёт мир: выбранная сохранённая копия отсутствует.");
            frozenSource=selected.CreateLiveWorld();
            frozenActor=frozenSource.actors.Single(actor=>actor.player && actor.character=="session-host").Copy();
            if(frozenActor.health!=1 || frozenActor.down || frozenActor.level!="factory:0" || frozenActor.connected)
                throw new InvalidOperationException("Исходное тело должно оставаться отключённым живым игроком с 1 HP на заводе: "+frozenActor.id);
            string sourceArchive=Path.Combine(value.Session.Saves.WorldDirectoryPath,"Worlds",selected.WorldId,"Snapshots",selected.Id+".json");
            if(!File.Exists(sourceArchive) || File.ReadAllText(sourceArchive)!=selected.Json)
                throw new InvalidOperationException("Нет точного неизменяемого архива выбранной копии: "+sourceArchive);
            var profile=StateJson.FromJson<ActorState>(value.Session.Saves.ReadCharacterSource());
            if(profile==null || profile.character!=value.Options.Character || profile.health<=0 || profile.down ||
                (mode!="ally" && (profile.id!=frozenActor.id || profile.health!=1)))
                throw new InvalidOperationException("Изолированный профиль не соответствует выбранному персонажу/реальному исходному телу.");
            report=new Report {mode=mode,participants=value.Options.ProbePeers,frameRate=value.Options.ProbeFrameRate,
                character=value.Options.Character,targetActor=frozenActor.id,sourceWorld=selected.WorldId,sourceSnapshot=selected.Id,
                sourceArchive=sourceArchive,sourceSha256=RecoverySnapshot.Fingerprint(File.ReadAllText(sourceArchive)),
                sourceActor=frozenActor,selectedCopyVerified=true};
            canvas=value.Hud?.GetComponent<Canvas>();
            if(canvas==null || value.Hud.CharacterContent==null || value.Hud.NetworkLobby==null)
                throw new InvalidOperationException("Нет штатного Canvas/интерфейса сравнения для входа.");
            if(mode!="incoming") value.Session.ConfigureHostStartup(SessionStartupIntent.Continue(selected));
            value.CommandSource=()=>default;
            value.Session.EntryPreparation.Requested+=Requested;
            value.Session.EntryPreparation.ClientAdmitted+=Admitted;
            value.Session.EntryPreparation.Cancelled+=Cancelled;
            value.EntryPresentationPrepared+=Prepared;
            value.Session.Changed+=Changed;
            value.Session.AttackReceived+=AttackReceived;
            value.Combat.DamageApplied+=DamageApplied;
            value.Session.RecoveryPersisted+=Persisted;
        }
        private IEnumerator Start()=>Drive(Run());
        private IEnumerator Run()
        {
            while(!finished)
            {
                // Real Unity UI input only; no call bypassing compatibility consent.
                var target=runtime.Hud.CharacterContent.ConfirmRect??runtime.Hud.NetworkLobby.ContentConfirmRect;
                if(target!=null && clicked.Add(target.GetInstanceID()))
                {
                    var button=target.GetComponent<Button>();
                    if(button==null || !button.interactable) throw new InvalidOperationException("Сравнение контента не предоставило действующую штатную кнопку.");
                    AddEvent("compatibility-consent",null,null);
                    yield return UiPointerProbe.Click(target);
                }
                if(report.mode!="ally" && knockedDown && (report.mode!="solo" || recovered) && report.persisted!=null && firstDraw)
                {Finish(true,"Реальный вход/кадр/урон и неизменяемая запись подтверждены.");yield break;}
                if(report.mode=="ally" && report.allyReady)
                {
                    string incoming=Path.Combine(Path.GetDirectoryName(runtime.Options.Report),"session-client-1-report.json");
                    if(File.Exists(incoming))
                    {
                        var peer=StateJson.FromJson<Report>(File.ReadAllText(incoming));
                        if(peer?.success!=true || peer.sourceWorld!=report.sourceWorld || peer.targetActor!=report.targetActor)
                            throw new InvalidOperationException("Входящий участник не подтвердил тот же мир/тело.");
                        if(report.persisted!=null) {Finish(true,"Готовый союзник и входящий участник подтверждены.");yield break;}
                    }
                }
                yield return new WaitForSecondsRealtime(.1f);
            }
        }
        private void Requested(EntryPreparationContext context)
        {
            Safe(()=>
            {
                if(context.WorldId!=report.sourceWorld || context.CharacterId!=report.character ||
                    (report.mode!="ally" && (context.ActorId!=report.targetActor || context.Level!=frozenActor.level)))
                    throw new InvalidOperationException("Подготовка относится к другому миру/персонажу/уровню.");
                RequirePending(context);
                expectedDefinition=WorldRooms.Definition(context.ViewWorld,context.Level);
                expectedLayoutId=RoomAuthoring.Load(context.Level,context.ViewWorld).CreateLayout(false).id;
                requestedRoom=runtime.EntryPreparationRoom;
                if(string.IsNullOrWhiteSpace(expectedLayoutId) || requestedRoom==null || requestedRoom.id!=expectedLayoutId)
                    throw new InvalidOperationException($"Подготовка выбрала другую authored комнату: token={context.Token}, level={context.Level}, definition={expectedDefinition}, expectedLayout={expectedLayoutId}, stagedLayout={requestedRoom?.id}.");
                token=context.Token;preparing=true;pendingProfile=runtime.Session.Saves.ReadCharacterSource();
                report.profileBeforePending=RecoverySnapshot.Fingerprint(pendingProfile);
                AddEvent("requested",context,context.ViewWorld.Find(context.ActorId));
            });
        }
        private void Prepared(EntryPreparationContext context)
        {
            Safe(()=>
            {
                RequirePending(context);
                var staged=runtime.EntryPreparationRoom;var lighting=runtime.EntryPreparationLighting;
                string actualProfile=runtime.Session.Saves.ReadCharacterSource();
                string actualProfileHash=actualProfile==null?"<нет>":RecoverySnapshot.Fingerprint(actualProfile);
                bool sameRoom=ReferenceEquals(staged,requestedRoom),sameLightRoom=lighting!=null && ReferenceEquals(lighting.Room,staged);
                if(!preparing || context.Token!=token || !runtime.EntryRoomPrepared || staged?.id!=expectedLayoutId || !sameRoom || !sameLightRoom ||
                    !lighting.IsReady || lighting.IsUpdating || actualProfile!=pendingProfile)
                    throw new InvalidOperationException($"Подготовленная локация/профиль не соответствует ожидающему телу: token={context.Token}, expectedToken={token}, preparing={preparing}, level={context.Level}, definition={expectedDefinition}, expectedLayout={expectedLayoutId}, stagedLayout={staged?.id}, lightLayout={lighting?.Room?.id}, sameStagedRoom={sameRoom}, sameLightRoom={sameLightRoom}, prepared={runtime.EntryRoomPrepared}, lightReady={lighting?.IsReady}, updating={lighting?.IsUpdating}, expectedProfileHash={report.profileBeforePending}, actualProfileHash={actualProfileHash}.");
                report.profileUnchangedWhilePending=true;report.firstPreparedSeconds=Elapsed;
                AddEvent("prepared",context,context.ViewWorld.Find(context.ActorId));
            });
        }
        private void RequirePending(EntryPreparationContext context)
        {
            var preview=context.ViewWorld.Find(context.ActorId);
            if(runtime.Session.World!=null || runtime.Session.LocalActor!=null || !runtime.Session.EntryPreparation.ClientPending || preview==null || preview.connected)
                throw new InvalidOperationException($"Игровое тело/мир появились до готовности: token={context.Token}, world={runtime.Session.World?.worldId}, localActor={runtime.Session.LocalActor?.id}, pending={runtime.Session.EntryPreparation.ClientPending}, preview={preview?.id}, previewConnected={preview?.connected}.");
            var actual=runtime.Session.ServerWorld?.Find(context.ActorId);
            if(actual?.connected==true) throw new InvalidOperationException("Сервер подключил ожидающее тело до admission: "+actual.id);
        }
        private void Admitted(EntryPreparationContext context,WorldState world)
        {
            Safe(()=>
            {
                var actor=world.Find(context.ActorId);
                if(context.Token!=token || report.firstPreparedSeconds<0 || !report.profileUnchangedWhilePending ||
                    runtime.Session.EntryPreparation.ClientPending || actor==null || !actor.connected || actor.character!=report.character)
                    throw new InvalidOperationException("Admission не следует за точной подготовкой/сохранённым профилем.");
                if(report.mode=="solo" && (actor.health!=1 || actor.down || actor.level!=frozenActor.level ||
                    Vector2.Distance(actor.Position,frozenActor.Position)>.0001f))
                    throw new InvalidOperationException("Соло тело повреждено/смещено до admission: "+StateJson.ToJson(actor));
                if(File.ReadAllText(report.sourceArchive)!=selected.Json)
                    throw new InvalidOperationException("Исходный immutable архив изменился во время входа.");
                report.admitted=true;report.admittedActor=actor.Copy();report.admissionSeconds=Elapsed;
                report.preparedBeforeAdmission=report.firstPreparedSeconds<=report.admissionSeconds;
                if(report.mode=="incoming" && actor.health<frozenActor.health)
                {
                    if(!actor.resolvedAttacks.Except(frozenActor.resolvedAttacks).Any())
                        throw new InvalidOperationException("Урон отключённому телу не имеет нового фактического attack receipt.");
                    report.offlineBodyDamageObserved=true;
                }
                preparing=false;AddEvent("admitted",context,actor);
                if(actor.id==report.targetActor) ObserveOutcome(actor);
            });
        }
        private void Cancelled() {if(!finished) Finish(false,"Подготовка входа отменена до ожидаемого admission.");}
        private void AttackReceived(AttackEvent attack) {if(!finished) attacks[attack.id]=attack;}
        private void DamageApplied(DamageResult result)
        {
            if(finished || result.actor!=report.targetActor) return;
            Safe(()=>
            {
                var actor=runtime.Session.World?.Find(result.actor);if(actor==null) throw new InvalidOperationException("Событие урона потеряло фактическое тело.");
                attacks.TryGetValue(result.attack,out var attack);
                var damage=new DamageRecord {attack=result.attack,attacker=attack?.attacker,weapon=attack?.weapon,actor=result.actor,owner=actor.owner,
                    level=result.level,amount=result.amount,health=actor.health,down=actor.down,generation=actor.generation,lifecycle=actor.lifecycleSequence,
                    seconds=Elapsed,frame=Time.frameCount,pending=preparing,presentationReady=runtime.LevelPresentationReady,
                    afterPrepared=report.firstPreparedSeconds>=0,afterFirstDraw=firstDraw};
                report.damage.Add(damage);
                if(report.mode=="solo" && (!damage.afterPrepared || !damage.presentationReady))
                    throw new InvalidOperationException("Соло NPC повредил входящее тело до готовности presentation: "+result.attack);
                if(report.mode=="ally" && !actor.connected) report.offlineBodyDamageObserved=true;
                ObserveOutcome(actor);
            });
        }
        private void Changed()
        {
            if(finished || runtime.Session.World==null) return;
            Safe(()=>
            {
                var actor=runtime.Session.World.Find(report.targetActor);
                if(report.mode=="ally" && actor!=null && !actor.connected && !double.IsNaN(lastOfflineHealth) && actor.health<lastOfflineHealth)
                    report.offlineBodyDamageObserved=true;
                if(actor!=null) {lastOfflineHealth=actor.health;ObserveOutcome(actor);}
            });
        }
        private void ObserveOutcome(ActorState actor)
        {
            if(actor.down && actor.health<=0 && (report.mode=="ally" || report.admitted))
            {
                if(!knockedDown) {knockedDown=true;report.actualKnockdown=true;report.knockdownSeconds=Elapsed;AddEvent("knockdown",null,actor);}
            }
            if(knockedDown && actor.level=="hub" && actor.health==1 && !actor.down)
            {
                if(!recovered) {recovered=true;report.partyRecoveryVerified=true;report.recoverySeconds=Elapsed;AddEvent("party-recovery",null,actor);}
            }
        }
        private void Persisted(RecoverySnapshot snapshot)
        {
            if(finished || !knockedDown || (report.mode=="solo" && !recovered)) return;
            Safe(()=>
            {
                var saved=snapshot.CreateLiveWorld().Find(report.targetActor);
                if(saved==null || (report.mode=="solo"?saved.level!="hub" || saved.health!=1 || saved.down:!saved.down || saved.health>0)) return;
                var hits=report.damage.Where(hit=>hit.afterPrepared && hit.actor==saved.id).Select(hit=>hit.attack).ToArray();
                if(report.mode=="solo" && hits.Length==0 || hits.Any(id=>!saved.resolvedAttacks.Contains(id))) return;
                if(report.mode=="incoming" && hits.Length==0 && (!report.offlineBodyDamageObserved ||
                    !saved.resolvedAttacks.Except(frozenActor.resolvedAttacks).Any())) return;
                string path=Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
                if(!File.Exists(path) || File.ReadAllText(path)!=snapshot.Json)
                    throw new InvalidOperationException("Durable событие не подтверждено собственным immutable архивом: "+snapshot.Id);
                report.persisted=new ArchiveRecord {id=snapshot.Id,world=snapshot.WorldId,path=path,sha256=RecoverySnapshot.Fingerprint(File.ReadAllText(path)),revision=snapshot.Revision,actor=saved.Copy()};
            });
        }
        private void LateUpdate()
        {
            if(finished || report==null) return;
            Safe(()=>
            {
                if(Time.realtimeSinceStartupAsDouble>deadline)
                    throw new InvalidOperationException($"Таймаут реального входа: mode={report.mode}, pendingFrames={report.pendingFrames}, prepared={report.firstPreparedSeconds}, admitted={report.admitted}, draw={firstDraw}, KO={knockedDown}, recovery={recovered}, archive={report.persisted?.id}.");
                if(preparing) report.pendingFrames++;
                if(!report.admitted || runtime.Session.LocalActor==null) return;
                if(report.mode=="ally")
                {
                    if(!allyTravelRequested && runtime.LevelPresentationReady && !runtime.IsLevelPreparing && runtime.Session.LocalActor.level=="hub")
                    {allyTravelRequested=true;runtime.Session.Travel.RequestFactory();}
                    if(runtime.Session.LocalActor.level!="factory:0") return;
                }
                if(!firstDraw && TryFrame(out var frame))
                {
                    if(report.mode=="ally" && (runtime.Session.LocalActor.health<=0 || runtime.Session.LocalActor.down))
                        throw new InvalidOperationException("Союзник потерял живой активный контекст до готового кадра; ожидание гостя не начинается.");
                    report.firstFrame=frame;firstDraw=report.firstDrawVerified=true;
                    NetworkSceneRecord.Save(runtime,1);
                    frame.scene=runtime.Options.Report+".phase-1.scene.json";
                    frame.png=runtime.Options.Report+".first-ready.png";
                    NetworkFrameCapture.Save(runtime.WorldCamera,frame.png,canvas,1920,1080);
                    frame.sha256=RoomLightingGpuEvidence.ByteHash(File.ReadAllBytes(frame.png));
                    AddEvent("first-drawn-frame",null,runtime.Session.LocalActor);
                    if(report.mode=="ally")
                    {
                        report.allyReady=true;
                        File.WriteAllText(Path.Combine(Path.GetDirectoryName(runtime.Options.Report),"ally-ready.json"),StateJson.ToJson(new ReadySignal {world=report.sourceWorld,actor=runtime.Session.LocalActor.id,seconds=Elapsed,frame=Time.frameCount},true));
                    }
                }
            });
        }
        [Serializable] private sealed class ReadySignal {public string world,actor;public double seconds;public int frame;}
        private bool TryFrame(out FrameRecord frame)
        {
            frame=null;var actor=runtime.Session.LocalActor;var camera=runtime.WorldCamera;var light=runtime.ActiveRoomLighting;
            if(actor==null || !runtime.LevelPresentationReady || runtime.IsLevelPreparing || runtime.Hud.RoomPresentationPending ||
                light==null || !light.IsReady || light.IsUpdating || camera==null || !camera.enabled || !camera.gameObject.activeInHierarchy) return false;
            var room=runtime.Geometry.Get(actor.level);long revision=runtime.Geometry.OcclusionRevision(actor.level);
            RoomLightingGpuEvidence.EnsurePublished(light,room,revision,light.ForegroundMap,light.BackgroundMap,light.Bounds);
            var actual=runtime.FindActorVisual(actor.id) as PonyVisual;
            if(actual==null || !actual.gameObject.activeInHierarchy || actual.DrawnFrame<0 || actual.DrawnPartCount<=0 || actual.DrawnClip!=actor.pose || !actual.TryGetDrawnBounds(out var bounds)) return false;
            if(view!=actual) {view=actual;renderers=actual.GetComponentsInChildren<MeshRenderer>(true);}
            int visible=renderers.Count(renderer=>renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy && (camera.cullingMask&(1<<renderer.gameObject.layer))!=0);
            if(visible==0) return false;
            var corners=new[] {new Vector3(bounds.min.x,bounds.min.y,bounds.center.z),new Vector3(bounds.min.x,bounds.max.y,bounds.center.z),new Vector3(bounds.max.x,bounds.min.y,bounds.center.z),new Vector3(bounds.max.x,bounds.max.y,bounds.center.z)};
            for(int i=0;i<corners.Length;i++)
            {
                corners[i]=camera.WorldToViewportPoint(corners[i]);var p=corners[i];
                if(!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || p.x<0 || p.x>1 || p.y<0 || p.y>1 || p.z<camera.nearClipPlane || p.z>camera.farClipPlane) return false;
            }
            frame=new FrameRecord {actor=actor.id,level=actor.level,clip=actual.DrawnClip,drawnFrame=actual.DrawnFrame,drawnParts=actual.DrawnPartCount,
                visibleRenderers=visible,seconds=Elapsed,frame=Time.frameCount,width=1920,height=1080,minimum=bounds.min,maximum=bounds.max,viewport=corners,
                foregroundId=light.ForegroundMap.GetInstanceID(),backgroundId=light.BackgroundMap.GetInstanceID(),revision=revision,
                health=actor.health,down=actor.down,presentationReady=true,shaderPairVerified=true};return true;
        }
        private double Elapsed=>Time.realtimeSinceStartupAsDouble-start;
        private void AddEvent(string phase,EntryPreparationContext context,ActorState actor)
        {
            report.events.Add(new EventRecord {phase=phase,seconds=Elapsed,frame=Time.frameCount,token=context?.Token??token,world=context?.WorldId??runtime.Session.World?.worldId,
                actor=actor?.id,level=actor?.level??context?.Level,health=actor?.health??0,down=actor?.down??false,connected=actor?.connected??false,
                generation=actor?.generation??0,lifecycle=actor?.lifecycleSequence??0,worldAbsent=runtime.Session.World==null,localActorAbsent=runtime.Session.LocalActor==null,
                pending=runtime.Session.EntryPreparation.ClientPending,prepared=runtime.EntryRoomPrepared,ready=runtime.LevelPresentationReady,
                authoredDefinition=expectedDefinition,expectedLayout=expectedLayoutId,stagedLayout=runtime.EntryPreparationRoom?.id,lightLayout=runtime.EntryPreparationLighting?.Room?.id,
                profileSha256=phase=="requested" || phase=="prepared"?RecoverySnapshot.Fingerprint(runtime.Session.Saves.ReadCharacterSource()):null});
        }
        private IEnumerator Drive(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            try
            {
                while(stack.Count>0 && !finished)
                {
                    object next=null;bool moved=false;
                    try {moved=stack.Peek().MoveNext();if(moved) next=stack.Peek().Current;}
                    catch(Exception error) {Finish(false,error.ToString());}
                    if(finished) yield break;
                    if(!moved) {(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(next is IEnumerator nested) stack.Push(nested);else yield return next;
                }
            }
            finally {while(stack.Count>0) (stack.Pop() as IDisposable)?.Dispose();}
        }
        private void Safe(Action action) {if(finished) return;try {action();}catch(Exception error) {Finish(false,error.ToString());}}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;report.success=success;report.detail=detail;
            report.finalActor=runtime.Session.World?.Find(report.targetActor)?.Copy();
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));
            if(success) Debug.Log("Проверка реального входа в рейд завершена: "+report.mode);else Debug.LogError("Проверка реального входа в рейд: "+detail);
            Application.Quit(success?0:3);
        }
        private void OnDestroy()
        {
            if(runtime?.Session==null) return;
            runtime.Session.EntryPreparation.Requested-=Requested;runtime.Session.EntryPreparation.ClientAdmitted-=Admitted;
            runtime.Session.EntryPreparation.Cancelled-=Cancelled;runtime.EntryPresentationPrepared-=Prepared;
            runtime.Session.Changed-=Changed;runtime.Session.AttackReceived-=AttackReceived;runtime.Combat.DamageApplied-=DamageApplied;
            runtime.Session.RecoveryPersisted-=Persisted;
        }
    }
}
