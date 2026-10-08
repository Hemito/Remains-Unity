using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.UI;
using Proof=Remains.Runtime.PendingBodyDamageEvidence;

namespace Remains.Runtime
{
    public struct PendingBodyRequestedMessage : NetworkMessage
    {public string token,world,actor,character,level,fingerprint;}
    public struct PendingBodyProofPart : NetworkMessage
    {public int kind,total,offset;public string token,hash;public byte[] data;}
    public struct PendingBodyFinishMessage : NetworkMessage
    {public bool success;public string detail,token,world,snapshot,canonicalArchiveJson;public int connection;public long revision;}

    // One ordinary shot at an existing offline body during normal asynchronous entry.
    // Inputs, Ready, ownership, damage and persistence are all production paths.
    [DefaultExecutionOrder(10000)]
    public sealed class PendingBodyDamageProbe : MonoBehaviour
    {
        private const string TargetCharacter="session-host",AllyCharacter="session-client-1";
        private const int MaximumProofBytes=512*1024,ChunkBytes=8192;
        private PrototypeRuntime runtime;private PendingBodyDamageReport report;
        private RecoverySnapshot source;private ActorState sourceTarget,sourceAlly;
        private PendingBodyHostProof hostProof;private PendingBodyIncomingProof incoming=new();
        private Canvas canvas;private double started,deadline;private bool finished,selectedCommand,readyWritten,fireIssued,receiptSent;
        private string pistolItem,pendingProfile,token,expectedConnectedOwner;private long admittedRevision=-1;
        private int expectedConnectedGeneration,expectedConnectedLifecycle;
        private string beforeDamageJson;private double beforeDamageSeconds;private int beforeDamageFrame;
        private readonly HashSet<int> clicked=new();
        private readonly List<PendingBodyFrame> frames=new();
        private readonly Dictionary<string,RecoverySnapshot> persisted=new(StringComparer.Ordinal);
        private readonly Dictionary<string,(int connection,long revision)> acks=new(StringComparer.Ordinal);
        private readonly Dictionary<int,Assembly> assemblies=new();
        private readonly HashSet<int> receivedKinds=new();
        private bool Ally=>report.mode=="ally";
        private double Elapsed=>Time.realtimeSinceStartupAsDouble-started;
        private ActorState Target=>runtime.Session.World?.Find(report.target);
        private sealed class Assembly
        {public int total,offset;public string token,hash;public byte[] bytes;}

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;started=Time.realtimeSinceStartupAsDouble;deadline=started+100;
            string mode=value.Options.Smoke?.EndsWith("-ally",StringComparison.Ordinal)==true?"ally":"incoming";
            Proof.Require(value.Options.Smoke=="raid-entry-pending-frames-"+mode && value.Options.ProbePeers==2 &&
                value.Options.Role==(mode=="ally"?"host":"client") && !string.IsNullOrWhiteSpace(value.Options.SaveDirectory) &&
                !string.IsNullOrWhiteSpace(value.Options.Report),"Нужны точный smoke/роль, два участника и изолированная существующая копия.");
            source=value.Session.Saves.SelectedSnapshot()??throw new InvalidOperationException("Нет выбранной существующей копии для проверки offline body.");
            var original=source.CreateLiveWorld();
            sourceTarget=original.actors.Single(actor=>actor.player && actor.character==TargetCharacter);
            sourceAlly=original.actors.Single(actor=>actor.player && actor.character==AllyCharacter);
            Proof.Require(original.friendlyFire && original.friendlyFireDecision==FriendlyFireDecision.Victim &&
                sourceTarget.health==100 && sourceAlly.health==100 && !sourceTarget.connected && !sourceAlly.connected &&
                !sourceTarget.down && !sourceAlly.down && sourceTarget.level=="factory:0" && sourceAlly.level==sourceTarget.level,
                "Исходная пара должна иметь 100 HP, два отключённых живых тела и victim friendly fire на заводе; состояния не исправляются пробой.");
            var self=mode=="ally"?sourceAlly:sourceTarget;
            Proof.Require(value.Options.Character==self.character,"Выбран не тот существующий персонаж: "+value.Options.Character);
            string profileJson=value.Session.Saves.ReadCharacterSource();var profile=StateJson.FromJson<ActorState>(profileJson);
            Proof.Require(profile.id==self.id && profile.character==self.character && profile.health==100 && !profile.down,
                "Исходный профиль не соответствует существующему живому телу/100 HP.");
            var sourceArchive=Proof.Archive(value.Session.Saves,source,sourceTarget.id);
            report=new PendingBodyDamageReport {mode=mode,participants=2,frameRate=value.Options.ProbeFrameRate,world=source.WorldId,
                actor=self.id,target=sourceTarget.id,source=new PendingBodySource {world=source.WorldId,snapshot=source.Id,revision=source.Revision,
                    archive=sourceArchive.path,sha256=sourceArchive.sha256,targetJson=Proof.Json(sourceTarget),allyJson=Proof.Json(sourceAlly),
                    profileBeforeJson=profileJson,profileSha256=RecoverySnapshot.Fingerprint(profileJson)}};
            canvas=value.Hud?.GetComponent<Canvas>();Proof.Require(canvas!=null,"Нет live Canvas для GPU кадров.");
            if(Ally) value.Session.ConfigureHostStartup(SessionStartupIntent.Continue(source));
            value.CommandSource=Command;
            value.Session.ServerReady+=RegisterServer;value.Session.ClientReady+=RegisterClient;
            value.Session.EntryPreparation.Requested+=Requested;value.EntryPresentationPrepared+=Prepared;
            value.Session.EntryPreparation.ClientAdmitted+=Admitted;value.Session.EntryPreparation.ServerAdmitting+=ServerAdmitting;
            value.Session.EntryPreparation.Cancelled+=Cancelled;value.Session.AttackReceived+=AttackReceived;
            value.Combat.Projectiles.Created+=ProjectileCreated;value.Combat.Projectiles.ContactObserved+=Contact;
            value.Combat.ContactObserved+=BeforeDamage;value.Combat.DamageApplied+=DamageApplied;value.Session.RecoveryReceiptPrepared+=ReceiptPrepared;
            value.Session.RecoveryReceiptPersisted+=ReceiptPersisted;value.Session.RecoveryPersisted+=RecoveryPersisted;
            value.Session.RecoveryAcknowledged+=Acknowledged;
        }
        private void RegisterServer()
        {
            NetworkServer.RegisterHandler<PendingBodyRequestedMessage>((connection,message)=>Safe(()=>
            {
                Proof.Require(Ally,"Requested получен вне host mode.");
                bool hasPending=runtime.Session.EntryPreparation.TryGetServerPending(TargetCharacter,out var actual);
                Proof.Require(hasPending,"Запрос свидетельства не имеет настоящего pending reservation.");
                var identity=PendingBodyIdentity.Capture(actual);
                Proof.Require(connection.connectionId==identity.connection && message.token==identity.token && message.world==identity.world &&
                    message.actor==identity.actor && message.character==identity.character && message.level==identity.level && message.fingerprint==identity.fingerprint,
                    "Requested debug message не соответствует REAL connection/token/world/body: sender="+connection.connectionId+", reserved="+identity.connection);
                BindIdentity(identity);hostProof.requestedConnectionVerified=true;hostProof.requestedSenderConnection=connection.connectionId;
            }));
            NetworkServer.RegisterHandler<PendingBodyProofPart>((connection,message)=>Safe(()=>
            {
                Proof.Require(Ally && hostProof?.identity!=null && connection.connectionId==hostProof.identity.connection && message.kind==2,
                    "Incoming proof пришёл от другого connection/context.");
                string json=Assemble(message);if(json==null) return;
                var proof=StateJson.FromJson<PendingBodyIncomingProof>(json);ValidateIncoming(proof);
                incoming=proof;report.incoming=incoming;
            }));
            NetworkServer.RegisterHandler<PendingBodyFinishMessage>((connection,message)=>
            {if(!message.success) Fail("Входящий участник отказал: connection="+connection.connectionId+"; "+message.detail);});
        }
        private void RegisterClient()
        {
            NetworkClient.RegisterHandler<PendingBodyProofPart>(message=>Safe(()=>
            {
                if(Ally) return;
                Proof.Require(message.kind==1,"Получен неправильный вид host boundary proof.");
                string json=Assemble(message);if(json==null) return;
                var proof=StateJson.FromJson<PendingBodyHostProof>(json);
                Proof.Require(incoming.requested!=null && proof.identity.token==token && proof.requestedConnectionVerified &&
                    proof.requestedSenderConnection==proof.identity.connection,"Host proof не связан с Requested входящего участника.");
                Proof.RequireCanonical(proof,report.target);hostProof=proof;report.hostProof=hostProof;
            }));
            NetworkClient.RegisterHandler<PendingBodyFinishMessage>(message=>
            {
                if(Ally || finished) return;
                if(message.success) Safe(()=>
                {
                    Proof.Require(receiptSent && incoming.frame!=null && message.token==token && message.world==report.world &&
                        message.snapshot==incoming.receipt.snapshot && message.revision==incoming.receipt.revision && message.connection==hostProof.identity.connection,
                        "Host завершил проверку до actual frame/receipt либо с другим token/snapshot/ACK.");
                    report.canonicalArchive=StateJson.FromJson<PendingBodyArchive>(message.canonicalArchiveJson);
                    Proof.Require(report.canonicalArchive.snapshot==message.snapshot && report.canonicalArchive.revision==message.revision &&
                        report.canonicalArchive.world==report.world,"Terminal canonical archive не соответствует ожидаемому receipt.");
                    Proof.Same(incoming.admitted.welcomedJson,report.canonicalArchive.actorJson,"incoming Welcome → terminal host archive witness");
                    report.acknowledgedSnapshot=message.snapshot;report.acknowledgedRevision=message.revision;report.acknowledgedConnection=message.connection;
                    Finish(true,message.detail);
                });
                else Finish(false,message.detail);
            });
        }
        private PlayerCommand Command()
        {
            if(finished || !Ally || !runtime.LevelPresentationReady || runtime.Session.LocalActor is not {} actor) return default;
            var command=new PlayerCommand();var target=Target;
            if(target==null) return command;
            if(!Anatomy.TryRegion(target,"torso",out var aim,out _)) {Fail("Нет actual passive torso aim: "+target.id);return command;}
            command.hasAim=true;command.aim=aim;
            if(!selectedCommand)
            {
                var item=actor.inventory.SingleOrDefault(item=>item.definition=="pistol" && item.count==1);
                if(item?.weaponState==null || item.weaponState.rounds<=0) {Fail("Нет существующего заряженного pistol item.");return command;}
                pistolItem=item.id;command.weaponItem=pistolItem;selectedCommand=true;return command;
            }
            if(readyWritten && !fireIssued && runtime.Session.EntryPreparation.TryGetServerPending(TargetCharacter,out var reservation))
            {
                Safe(()=>
                {
                    BindIdentity(PendingBodyIdentity.Capture(reservation));
                    var readyTarget=StateJson.FromJson<ActorState>(report.allyReady.targetJson);
                    Proof.Require(!target.connected && target.owner==runtime.Session.LocalOwner && target.generation==readyTarget.generation &&
                        target.lifecycleSequence==readyTarget.lifecycleSequence && target.health==100 && !target.down,
                        "Offline body сменило owner/epoch/control до единственного обычного fire: "+Proof.Json(target));
                    RequireCurrentRay(actor,target,aim);
                    hostProof.shot.beforeTargetJson=Proof.Json(target);hostProof.shot.beforeSourceJson=Proof.Json(actor);
                    hostProof.shot.commandSeconds=Elapsed;hostProof.shot.commandFrame=Time.frameCount;hostProof.shot.ordinaryFireCommands=1;
                    fireIssued=true;command.fire=true;
                });
            }
            return command;
        }
        private void BindIdentity(PendingBodyIdentity identity)
        {
            Proof.Require(identity.world==report.world && identity.actor==report.target && identity.character==TargetCharacter && identity.level==sourceTarget.level,
                "Pending reservation относится к другому существующему телу.");
            if(hostProof==null) hostProof=new PendingBodyHostProof {identity=identity,shot=new PendingBodyShot()};
            else Proof.Require(hostProof.identity.Matches(identity),"Геометрический retry сменил token во время выбранного одиночного shot; не задерживаем Ready/не стреляем повторно.");
            report.hostProof=hostProof;
        }
        private void RequireCurrentRay(ActorState actor,ActorState target,Vector2 aim)
        {
            Proof.Require(actor.id==sourceAlly.id && actor.connected && !actor.down && actor.health>0 && actor.level==target.level &&
                actor.equippedWeaponItem==pistolItem && actor.weapon=="pistol" && WeaponInventory.Selected(actor)?.weaponState?.rounds>0,
                "Союзник не имеет actual selected pistol/control.");
            var pose=WeaponPose.SampleRanged(actor);
            var first=Proof.FirstActor(runtime,actor,target,pose.Muzzle,pose.Muzzle+pose.Direction*WeaponCatalog.Get("pistol").range,out string door);
            Proof.Require(first==target.id,"Текущий production ray не попадает первым в offline body: first="+first+", target="+target.id+", sourceDoor="+door+", muzzle="+pose.Muzzle+", aim="+aim);
        }
        private IEnumerator Start()
        {
            while(!finished)
            {
                var confirm=runtime.Hud.CharacterContent.ConfirmRect??runtime.Hud.NetworkLobby.ContentConfirmRect;
                if(confirm!=null && clicked.Add(confirm.GetInstanceID()))
                {
                    if(confirm.GetComponent<Button>()?.interactable!=true) {Fail("Штатная кнопка согласия контента недоступна.");yield break;}
                    yield return UiPointerProbe.Click(confirm);
                }
                yield return null;
            }
        }
        private PendingBodyPreparation Preparation(EntryPreparationContext context)
        {
            string profile=runtime.Session.Saves.ReadCharacterSource();var light=runtime.EntryPreparationLighting;
            return new PendingBodyPreparation {token=context.Token,world=context.WorldId,actor=context.ActorId,character=context.CharacterId,
                level=context.Level,fingerprint=context.GeometryFingerprint,seconds=Elapsed,frame=Time.frameCount,
                profileJson=profile,profileSha256=RecoverySnapshot.Fingerprint(profile),worldAbsent=runtime.Session.World==null,
                localActorAbsent=runtime.Session.LocalActor==null,pending=runtime.Session.EntryPreparation.ClientPending,
                roomPrepared=runtime.EntryRoomPrepared,room=runtime.EntryPreparationRoom?.id,lightRoom=light?.Room?.id,lightReady=light?.IsReady==true && !light.IsUpdating};
        }
        private void Requested(EntryPreparationContext context)
        {
            if(Ally || finished) return;
            Safe(()=>
            {
                Proof.Require(context.WorldId==report.world && context.ActorId==report.target && context.CharacterId==TargetCharacter && context.Level==sourceTarget.level,
                    "Входящий Requested относится к другому миру/телу.");
                token=context.Token;pendingProfile=runtime.Session.Saves.ReadCharacterSource();incoming.requested=Preparation(context);report.incoming=incoming;
                Proof.Require(incoming.requested.worldAbsent && incoming.requested.localActorAbsent && incoming.requested.pending &&
                    !context.ViewWorld.Find(context.ActorId).connected,"Requested уже создал игровое тело/мир.");
                NetworkClient.Send(new PendingBodyRequestedMessage {token=context.Token,world=context.WorldId,actor=context.ActorId,
                    character=context.CharacterId,level=context.Level,fingerprint=context.GeometryFingerprint});
            });
        }
        private void Prepared(EntryPreparationContext context)
        {
            if(Ally || finished) return;
            Safe(()=>
            {
                incoming.prepared=Preparation(context);
                Proof.Require(context.Token==token && incoming.prepared.worldAbsent && incoming.prepared.localActorAbsent && incoming.prepared.pending &&
                    incoming.prepared.roomPrepared && incoming.prepared.lightReady && incoming.prepared.room==incoming.prepared.lightRoom &&
                    incoming.prepared.profileJson==pendingProfile,"Prepared изменил профиль/подключил тело до обычного Ready либо не имеет готового света.");
            });
        }
        private void Cancelled() {if(!finished && !Ally) Fail("Штатная подготовка входа отменена; probe не завершает её искусственно.");}
        private void Admitted(EntryPreparationContext context,WorldState welcomed)
        {
            if(Ally || finished) return;
            Safe(()=>
            {
                Proof.Require(hostProof?.canonical!=null && hostProof.identity.Matches(context) && incoming.prepared!=null &&
                    !runtime.Session.EntryPreparation.ClientPending,"Welcome не связан с подготовкой/host canonical boundary.");
                var actor=welcomed.Find(context.ActorId);Proof.Require(actor!=null,"Welcome не содержит настоящее тело.");
                Proof.Same(hostProof.canonical.expectedConnectedJson,Proof.Json(actor),"canonical shared Connect → actual Welcome");
                incoming.admitted=new PendingBodyAdmitted {token=context.Token,welcomedJson=Proof.Json(actor),revision=welcomed.revision,
                    pauseRevision=runtime.Session.Pause.Revision,seconds=Elapsed,frame=Time.frameCount,preparedBeforeAdmission=true,canonicalDamagePreserved=true};
                admittedRevision=welcomed.revision;report.readyConsumedObserved=true;
            });
        }
        private void AttackReceived(AttackEvent attack)
        {
            if(finished || !Ally || !fireIssued || attack.attacker!=sourceAlly.id || attack.weapon!="pistol") return;
            Safe(()=>
            {
                if(hostProof.shot.launchJson!=null)
                {Proof.Require(StateJson.FromJson<AttackEvent>(hostProof.shot.launchJson).id==attack.id,"Обычный fire создал второй pistol attack.");return;}
                Proof.Require(attack.rangedPose!=null && attack.rangedPose.worldId==report.world && attack.rangedPose.weaponItem==pistolItem,
                    "Настоящий launch потерял captured world/item pose.");
                hostProof.shot.launchJson=StateJson.ToJson(attack);hostProof.shot.launchSeconds=Elapsed;hostProof.shot.launchFrame=Time.frameCount;
            });
        }
        private void ProjectileCreated(ProjectileFlight flight)
        {
            if(finished || !Ally || !fireIssued || flight.state.launch.attacker!=sourceAlly.id || flight.state.launch.weapon!="pistol") return;
            Safe(()=>
            {
                if(hostProof.shot.launchJson==null) AttackReceived(flight.state.launch);
                Proof.Require(StateJson.FromJson<AttackEvent>(hostProof.shot.launchJson).id==flight.state.launch.id,"Created flight относится к другому shot.");
                hostProof.shot.projectileJson=StateJson.ToJson(flight.state);
            });
        }
        private void Contact(ProjectileContact contact)
        {
            if(finished || !Ally || hostProof?.shot.launchJson==null || contact.projectile!=StateJson.FromJson<AttackEvent>(hostProof.shot.launchJson).id) return;
            Safe(()=>
            {
                Proof.Require(hostProof.shot.contact==null && contact.target==report.target && contact.weapon=="pistol",
                    "Единственный projectile имеет неверный/повторный actual contact: target="+contact.target+", surface="+contact.surfaceTexture);
                hostProof.shot.contact=new PendingBodyContact {projectile=contact.projectile,weapon=contact.weapon,level=contact.level,target=contact.target,
                    targetType=contact.targetType,surfaceTexture=contact.surfaceTexture,sourceDoorId=contact.sourceDoorId,position=contact.position,seconds=Elapsed,frame=Time.frameCount};
            });
        }
        private void DamageApplied(DamageResult result)
        {
            if(finished || !Ally || result.actor!=report.target || !fireIssued) return;
            Safe(()=>
            {
                Proof.Require(hostProof?.shot.launchJson!=null && result.attack==StateJson.FromJson<AttackEvent>(hostProof.shot.launchJson).id &&
                    hostProof.shot.localDamage==null,"Дополнительный/посторонний damage вмешался в scoped offline shot: "+result.attack);
                var actor=Target;
                bool pending=runtime.Session.EntryPreparation.TryGetServerPending(TargetCharacter,out var identity) && hostProof.identity.Matches(PendingBodyIdentity.Capture(identity));
                hostProof.shot.localDamage=new PendingBodyDamage {attack=result.attack,actor=result.actor,level=result.level,amount=result.amount,
                    beforeJson=beforeDamageJson,afterJson=Proof.Json(actor),owner=actor.owner,generation=actor.generation,lifecycle=actor.lifecycleSequence,
                    pending=pending,disconnected=!actor.connected,seconds=Elapsed,frame=Time.frameCount,beforeSeconds=beforeDamageSeconds,beforeFrame=beforeDamageFrame};
            });
        }
        private void BeforeDamage(AttackEvent attack,ActorState actor)
        {
            if(finished || !Ally || hostProof?.shot.launchJson==null || actor.id!=report.target ||
                attack.id!=StateJson.FromJson<AttackEvent>(hostProof.shot.launchJson).id) return;
            Safe(()=>
            {
                // CombatController invokes this on the same stack immediately before
                // victim-owned DamageReceiver.Apply; fire-time motion is a different witness.
                Proof.Require(beforeDamageJson==null,"Повторный контакт перед scoped DamageReceiver.Apply.");
                beforeDamageJson=Proof.Json(actor);beforeDamageSeconds=Elapsed;beforeDamageFrame=Time.frameCount;
            });
        }
        private void ServerAdmitting(EntryPreparationIdentity identity)
        {
            if(finished || !Ally || identity.Character!=TargetCharacter) return;
            Safe(()=>
            {
                BindIdentity(PendingBodyIdentity.Capture(identity));var canonical=runtime.Session.ServerWorld.Find(identity.Actor);
                hostProof.canonical=new PendingBodyCanonical {identity=PendingBodyIdentity.Capture(identity),canonicalBeforeConnectJson=Proof.Json(canonical),
                    seconds=Elapsed,frame=Time.frameCount,publicationBeforeAdmission=false};
                // Set the declared boolean only after checking the real canonical actor.
                var damage=hostProof.shot.localDamage;var local=damage==null?null:StateJson.FromJson<ActorState>(damage.afterJson);
                Proof.Require(local!=null && !canonical.connected && canonical.owner==local.owner && canonical.generation==local.generation &&
                    canonical.lifecycleSequence==local.lifecycleSequence && canonical.health==local.health && canonical.down==local.down &&
                    canonical.damageSequence==local.damageSequence && canonical.resolvedAttacks.SequenceEqual(local.resolvedAttacks),
                    "publication-before-admission: Ready достиг Connect без опубликованного offline shot; canonicalHP="+canonical.health+
                    ", localHP="+local?.health+", canonicalSeq="+canonical.damageSequence+", localSeq="+local?.damageSequence+", attack="+damage?.attack);
                hostProof.canonical.publicationBeforeAdmission=true;
                // The source profile of the incoming actor is the supplied unchanged
                // copied profile. Outside hub the shared rule deliberately keeps body state.
                hostProof.canonical.expectedConnectedJson=Proof.ExpectedConnected(runtime.Session.ServerWorld,canonical,report.source.targetJson,identity.Connection);
                var expected=StateJson.FromJson<ActorState>(hostProof.canonical.expectedConnectedJson);
                expectedConnectedOwner=expected.owner;expectedConnectedGeneration=expected.generation;expectedConnectedLifecycle=expected.lifecycleSequence;
                Proof.RequireCanonical(hostProof,report.target);
                Proof.Require(hostProof.requestedConnectionVerified,"Requested connection witness не доставлен до admission.");
                SendProof(1,hostProof.identity.token,StateJson.ToJson(hostProof),NetworkServer.connections[identity.Connection]);
            });
        }
        private void ReceiptPrepared(RecoverySnapshot snapshot,ActorState actor)
        {
            if(finished || Ally || incoming.admitted==null || incoming.receipt!=null) return;
            Safe(()=>
            {
                Proof.Require(snapshot.WorldId==report.world && snapshot.Revision==admittedRevision && actor.id==report.target,
                    "Первый Welcome receipt не соответствует admitted snapshot/actor.");
                var archived=snapshot.CreateLiveWorld().Find(report.target);
                Proof.Same(incoming.admitted.welcomedJson,Proof.Json(archived),"Welcome actor → actual immutable snapshot actor");
                var normalized=archived;CosmeticEquipment.ReconcileInventory(normalized);
                WeaponRecoilReplication.Normalize(normalized,incoming.admitted.pauseRevision,true,"pending body / Welcome receipt oracle");
                // The production receipt is a capture copy followed by a detached
                // event copy. Match those actual typed serializer boundaries.
                normalized=normalized.Copy().Copy();
                Proof.Same(Proof.Json(normalized),Proof.Json(actor),"Welcome normal live neutralization → frozen receipt capture");
                incoming.receipt=new PendingBodyReceipt {world=snapshot.WorldId,snapshot=snapshot.Id,revision=snapshot.Revision,
                    receiptPlayerJson=Proof.Json(actor),expectedProfileJson=Proof.ExpectedProfile(actor),preparedSeconds=Elapsed};
            });
        }
        private void ReceiptPersisted(RecoverySnapshot snapshot,string profileJson)
        {
            if(finished || Ally || incoming.receipt?.snapshot!=snapshot.Id) return;
            Safe(()=>
            {
                var receipt=incoming.receipt;
                Proof.Require(receipt.persistedProfileJson==null && profileJson==receipt.expectedProfileJson,
                    "FIFO profile не равен полному frozen receipt actor + ContentProvenance: "+snapshot.Id);
                receipt.persistedProfileJson=profileJson;receipt.persistedSeconds=Elapsed;
                receipt.archive=Proof.Archive(runtime.Session.Saves,snapshot,report.target);
                Proof.Same(incoming.admitted.welcomedJson,receipt.archive.actorJson,"Welcome actor → own durable archive");
            });
        }
        private void RecoveryPersisted(RecoverySnapshot snapshot)
        {
            if(finished || !Ally || snapshot.WorldId!=report.world) return;
            Safe(()=>
            {
                Proof.Require(persisted.Count<64 || persisted.ContainsKey(snapshot.Id),"Переполнено ограниченное множество persisted snapshots.");
                persisted[snapshot.Id]=snapshot;
            });
        }
        private void Acknowledged(int connection,string snapshot,long revision)
        {
            if(finished || !Ally || hostProof?.identity==null || connection!=hostProof.identity.connection) return;
            Safe(()=> {Proof.Require(acks.Count<64 || acks.ContainsKey(snapshot),"Переполнено множество ACK.");acks[snapshot]=(connection,revision);});
        }
        private void ValidateIncoming(PendingBodyIncomingProof proof)
        {
            Proof.RequireCanonical(hostProof,report.target);
            Proof.Require(proof?.requested!=null && proof.prepared!=null && proof.admitted!=null && proof.receipt!=null && proof.frame!=null &&
                proof.requested.token==hostProof.identity.token && proof.prepared.token==hostProof.identity.token && proof.admitted.token==hostProof.identity.token &&
                proof.requested.profileJson==proof.prepared.profileJson && proof.requested.worldAbsent && proof.requested.localActorAbsent && proof.requested.pending &&
                proof.prepared.worldAbsent && proof.prepared.localActorAbsent && proof.prepared.pending && proof.prepared.roomPrepared && proof.prepared.lightReady &&
                proof.prepared.seconds<=proof.admitted.seconds && proof.receipt.world==report.world && proof.receipt.revision==proof.admitted.revision &&
                proof.receipt.expectedProfileJson==proof.receipt.persistedProfileJson && proof.receipt.archive.snapshot==proof.receipt.snapshot &&
                proof.frame.ready && proof.frame.actor==report.target,"Incoming proof не содержит полного pending/admitted/durable/frame контракта.");
            Proof.Same(hostProof.canonical.expectedConnectedJson,proof.admitted.welcomedJson,"host canonical → incoming Welcome");
            Proof.Same(proof.admitted.welcomedJson,proof.receipt.archive.actorJson,"incoming Welcome → incoming archive");
        }
        private void Update()
        {
            if(finished) return;
            if(Time.realtimeSinceStartupAsDouble>=deadline) {Fail("Deadline 100s; "+Context());return;}
            if(!Ally && runtime.Session.EntryPreparation.ClientPending) report.pendingFrames++;
        }
        private void LateUpdate()
        {
            if(finished) return;
            Safe(()=>
            {
                if(Ally && selectedCommand && !readyWritten && runtime.LevelPresentationReady && runtime.Session.LocalActor is {} actor && Target is {} target &&
                    actor.equippedWeaponItem==pistolItem && actor.weapon=="pistol")
                {
                    Proof.Require(Anatomy.TryRegion(target,"torso",out var aim,out _),"Нет actual target aim.");RequireCurrentRay(actor,target,aim);
                    var frame=Capture(0,"ally-ready",actor);frames.Add(frame);
                    var pose=WeaponPose.SampleRanged(actor);
                    report.allyReady=new PendingBodyReady {world=report.world,actor=actor.id,target=target.id,pistolItem=pistolItem,
                        actorJson=Proof.Json(actor),targetJson=Proof.Json(target),muzzle=pose.Muzzle,aim=aim,selectionRevision=actor.weaponSelectionRevision,
                        rounds=WeaponInventory.Selected(actor).weaponState.rounds,clearRay=true,firstContact=target.id,seconds=Elapsed,frame=Time.frameCount};
                    string path=Path.Combine(Path.GetDirectoryName(runtime.Options.Report),"ally-ready.json");
                    File.WriteAllText(path,StateJson.ToJson(report.allyReady,true));readyWritten=true;
                }
                if(!Ally && !receiptSent && incoming.admitted!=null && incoming.receipt?.persistedProfileJson!=null && runtime.LevelPresentationReady)
                {
                    incoming.frame=Capture(1,"incoming-admitted-durable",runtime.Session.LocalActor);frames.Add(incoming.frame);
                    ValidateIncoming(incoming);report.incoming=incoming;receiptSent=true;
                    SendProof(2,token,StateJson.ToJson(incoming),null);
                }
                if(Ally && hostProof?.canonical!=null && incoming.receipt!=null)
                {
                    var actual=runtime.Session.ServerWorld?.Find(report.target);
                    report.readyConsumedObserved=actual?.connected==true && actual.owner==expectedConnectedOwner && actual.generation==expectedConnectedGeneration &&
                        actual.lifecycleSequence==expectedConnectedLifecycle && actual.level==hostProof.identity.level;
                    TryComplete();
                }
            });
        }
        private PendingBodyFrame Capture(int phase,string label,ActorState actor)
        {
            var camera=runtime.WorldCamera;var view=runtime.FindActorVisual(actor.id) as PonyVisual;
            Proof.Require(runtime.LevelPresentationReady && camera!=null && camera.enabled && camera.cullingMask!=0 && view!=null &&
                view.DrawnFrame>=0 && view.DrawnPartCount>0,"Нет готовой actual world camera/нарисованного actor для кадра: "+label);
            bool hasBounds=view.TryGetDrawnBounds(out var bounds);
            Proof.Require(hasBounds,"Нет фактических нарисованных bounds actor: "+label);
            var lighting=runtime.ActiveRoomLighting;var room=runtime.Geometry.Get(actor.level);
            Proof.Require(lighting!=null,"Нет actual published lighting для готового кадра.");
            RoomLightingGpuEvidence.EnsurePublished(lighting,room,runtime.Geometry.OcclusionRevision(actor.level),
                lighting.ForegroundMap,lighting.BackgroundMap,lighting.Bounds);
            var min=camera.WorldToViewportPoint(bounds.min);var max=camera.WorldToViewportPoint(bounds.max);
            Proof.Require(min.z>0 && min.x<1 && max.x>0 && min.y<1 && max.y>0,"Actual actor отсутствует во viewport: "+label);
            string png=runtime.Options.Report+".phase-"+phase+".png";
            NetworkFrameCapture.Save(camera,png,canvas,1920,1080);NetworkSceneRecord.Save(runtime,phase);
            return new PendingBodyFrame {label=label,phase=phase,png=png,sha256=RoomLightingGpuEvidence.ByteHash(File.ReadAllBytes(png)),
                scene=runtime.Options.Report+".phase-"+phase+".scene.json",actor=actor.id,target=report.target,level=actor.level,
                width=1920,height=1080,seconds=Elapsed,frame=Time.frameCount,clip=view.DrawnClip,drawnFrame=view.DrawnFrame,
                ready=true,cullingMask=camera.cullingMask,minimum=bounds.min,maximum=bounds.max,viewportMinimum=min,viewportMaximum=max};
        }
        private void TryComplete()
        {
            if(finished || !Ally || incoming.receipt==null || !report.readyConsumedObserved) return;
            var receipt=incoming.receipt;
            if(!persisted.TryGetValue(receipt.snapshot,out var snapshot) || !acks.TryGetValue(receipt.snapshot,out var ack) ||
                ack.connection!=hostProof.identity.connection || ack.revision!=receipt.revision) return;
            ValidateIncoming(incoming);Proof.Require(snapshot.Revision==receipt.revision,"Canonical persisted revision отличается от incoming receipt.");
            report.canonicalArchive=Proof.Archive(runtime.Session.Saves,snapshot,report.target);
            Proof.Same(incoming.admitted.welcomedJson,report.canonicalArchive.actorJson,"incoming Welcome → host canonical archive");
            report.acknowledgedConnection=ack.connection;report.acknowledgedSnapshot=receipt.snapshot;report.acknowledgedRevision=ack.revision;
            frames.Add(Capture(2,"ally-canonical-ack",runtime.Session.LocalActor));
            // Send the exact same persistence result, not a later selected save head.
            NetworkServer.connections[hostProof.identity.connection].Send(new PendingBodyFinishMessage {success=true,
                token=hostProof.identity.token,world=report.world,snapshot=receipt.snapshot,revision=receipt.revision,connection=ack.connection,
                canonicalArchiveJson=StateJson.ToJson(report.canonicalArchive),
                detail="Обычный pending hit сохранён до Connect, принят Welcome и подтверждён exact archive/profile/ACK."});
            Finish(true,"Обычный pending hit сохранён до Connect, принят Welcome и подтверждён exact archive/profile/ACK.");
        }
        private void SendProof(int kind,string context,string json,NetworkConnectionToClient connection)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(json);Proof.Require(bytes.Length>0 && bytes.Length<=MaximumProofBytes,"Debug proof превысил bounded 512КиБ.");
            string hash=RecoverySnapshot.Fingerprint(json);
            for(int offset=0;offset<bytes.Length;offset+=ChunkBytes)
            {
                var data=new byte[Math.Min(ChunkBytes,bytes.Length-offset)];Buffer.BlockCopy(bytes,offset,data,0,data.Length);
                var part=new PendingBodyProofPart {kind=kind,token=context,hash=hash,total=bytes.Length,offset=offset,data=data};
                if(connection!=null) connection.Send(part);else NetworkClient.Send(part);
            }
        }
        private string Assemble(PendingBodyProofPart part)
        {
            Proof.Require(part.kind is 1 or 2 && !receivedKinds.Contains(part.kind) && part.total>0 && part.total<=MaximumProofBytes &&
                part.data!=null && part.data.Length>0 && part.data.Length<=ChunkBytes,"Недопустимый/повторный debug proof chunk.");
            if(!assemblies.TryGetValue(part.kind,out var value))
            {
                Proof.Require(part.offset==0,"Debug proof начался с неполного пакета.");
                value=new Assembly {total=part.total,token=part.token,hash=part.hash,bytes=new byte[part.total]};assemblies.Add(part.kind,value);
            }
            Proof.Require(value.total==part.total && value.token==part.token && value.hash==part.hash && value.offset==part.offset &&
                part.offset+part.data.Length<=part.total,"Смешаны debug proof context/chunks.");
            Buffer.BlockCopy(part.data,0,value.bytes,part.offset,part.data.Length);value.offset+=part.data.Length;
            if(value.offset<value.total) return null;
            string json=new UTF8Encoding(false,true).GetString(value.bytes);
            Proof.Require(RecoverySnapshot.Fingerprint(json)==value.hash,"Hash debug proof не совпал.");
            assemblies.Remove(part.kind);receivedKinds.Add(part.kind);return json;
        }
        private string Context()=>"mode="+report?.mode+", ready="+runtime.LevelPresentationReady+", pending="+runtime.Session.EntryPreparation.ClientPending+
            ", reserved="+runtime.Session.EntryPreparation.ServerPendingCount+", fire="+fireIssued+", launch="+hostProof?.shot?.launchJson?.Length+
            ", damage="+hostProof?.shot?.localDamage?.attack+", canonical="+hostProof?.canonical?.publicationBeforeAdmission+
            ", admitted="+(incoming.admitted!=null)+", receipt="+incoming.receipt?.snapshot+", persisted="+(incoming.receipt?.persistedProfileJson!=null);
        private void Safe(Action action) {if(finished) return;try {action();} catch(Exception error) {Fail(error.ToString());}}
        private void Fail(string detail)
        {
            if(finished) return;Debug.LogError("Pending body diagnostic: "+detail);
            if(Ally && NetworkServer.active) NetworkServer.SendToAll(new PendingBodyFinishMessage {success=false,detail=detail});
            else if(NetworkClient.isConnected) NetworkClient.Send(new PendingBodyFinishMessage {success=false,detail=detail});
            Finish(false,detail);
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            report.sourceArchiveUnchanged=File.Exists(report.source.archive) && File.ReadAllText(report.source.archive)==source.Json;
            if(success && !report.sourceArchiveUnchanged) {success=false;detail="Исходный immutable archive изменился.";}
            report.success=success;report.detail=detail;report.context=Context();report.frames=frames.ToArray();report.incoming=incoming;
            finished=true;Unsubscribe();File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        private void Unsubscribe()
        {
            if(runtime==null) return;
            runtime.Session.ServerReady-=RegisterServer;runtime.Session.ClientReady-=RegisterClient;
            runtime.Session.EntryPreparation.Requested-=Requested;runtime.EntryPresentationPrepared-=Prepared;
            runtime.Session.EntryPreparation.ClientAdmitted-=Admitted;runtime.Session.EntryPreparation.ServerAdmitting-=ServerAdmitting;
            runtime.Session.EntryPreparation.Cancelled-=Cancelled;runtime.Session.AttackReceived-=AttackReceived;
            runtime.Combat.Projectiles.Created-=ProjectileCreated;runtime.Combat.Projectiles.ContactObserved-=Contact;
            runtime.Combat.ContactObserved-=BeforeDamage;runtime.Combat.DamageApplied-=DamageApplied;runtime.Session.RecoveryReceiptPrepared-=ReceiptPrepared;
            runtime.Session.RecoveryReceiptPersisted-=ReceiptPersisted;runtime.Session.RecoveryPersisted-=RecoveryPersisted;
            runtime.Session.RecoveryAcknowledged-=Acknowledged;
            persisted.Clear();acks.Clear();assemblies.Clear();
        }
        private void OnDestroy()=>Unsubscribe();
    }
}
