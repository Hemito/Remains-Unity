using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct WarehouseSeamFinish : NetworkMessage {public bool success;public string detail,snapshot,world,actors;}
    public struct WarehouseSeamDurable : NetworkMessage {public string actor,snapshot;}
    public struct WarehouseSeamAccepted : NetworkMessage {public string snapshot;}
    // The shared seam probe changes only initial player placement. All later movement is normal input.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryWarehouseSeamProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success,traversalVerified,savedReloadVerified,reinstallVerified;
            public bool canonicalWriteVerified,productionReceiptVerified,playerStateVerified;
            public bool terminalIdentityVerified,receiptCaptureVerified,receiptCharacterVerified,pairDurableVerified;
            public string detail,world,snapshot,actor,owner,level,seam;
            public string snapshotPlayerJson,receiptPlayerJson,receiptExpectedCharacterJson,receiptCharacterJson,receiptCharacterSha256,terminalPlayerJson,preCommitPlayerJson;
            public long snapshotRevision;
            public float snapshotHealth,receiptHealth,finalHealth;
            public double preCommitCapturedAt,receiptCapturedAt,receiptPersistedAt,archiveVerifiedAt,terminalVerifiedAt;
            public int generation,props,containers,doors,npcs;
            public Vector2 position;
        }
        private PrototypeRuntime runtime;private FactoryOfficeSeamProbe seam;
        private bool traveled,sent,finished,receiving,durableVerified,acceptedSent,playerStateVerified;private float deadline;private string seamLabel;
        private WarehouseSeamFinish verifiedResult;private readonly HashSet<int> durablePeers=new();
        private readonly HashSet<int> requiredPeers=new(),receiptPeers=new();
        private readonly HashSet<string> persistedSnapshots=new(StringComparer.Ordinal);
        private string finishSnapshot,finishWorld;private long finishRevision;
        private ActorState verifiedPlayer;
        private ActorState localCommitWitness,canonicalCommitWitness;
        private double preCommitCapturedAt,archiveVerifiedAt,terminalVerifiedAt;
        private bool terminalIdentityVerified,receiptCharacterVerified;
        private sealed class ReceiptWitness
        {
            public ActorState player;
            public string playerJson,expectedCharacterJson,characterJson;
            public long revision;
            public double capturedAt,persistedAt;
        }
        // The verification deadline bounds this diagnostic cache. It holds only
        // detached captures, never mutable live actors or Unity objects.
        private readonly Dictionary<string,ReceiptWitness> receiptWitnesses=new(StringComparer.Ordinal);
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+65;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory) || value.Options.ProbePeers is <1 or >2)
                throw new InvalidOperationException("Стык склада требует отдельное сохранение и 1–2 участников.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка стыка не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(LegacyFactoryFixture.Create());
            }
            if(value.Options.Smoke.StartsWith("continuation-seam",StringComparison.Ordinal))
            {
                bool lower=value.Options.Smoke.StartsWith("continuation-seam-lower",StringComparison.Ordinal);
                seamLabel=lower?"continuation-lower-seam":"continuation-upper-seam";
                seam=new FactoryOfficeSeamProbe(value,new FactorySeamProbeSettings(lower?4:7.2f,151.8f,152,155.4f,151.9f,
                    lower?"Нижний стык склад→комната27":"Верхний стык склад→комната27",seamLabel));
            }
            else {seamLabel="warehouse-seam";seam=new FactoryOfficeSeamProbe(value,true);}
            runtime.CommandSource=seam.Command;
            value.Session.RecoveryPersisted+=OnRecoveryPersisted;
            value.Session.RecoveryAcknowledged+=OnRecoveryAcknowledged;
            value.Session.RecoveryReceiptPrepared+=OnReceiptPrepared;
            value.Session.RecoveryReceiptPersisted+=OnReceiptPersisted;
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<WarehouseSeamFinish>(result=>
                {if(!result.success) Finish(result);else if(!receiving){receiving=true;StartCoroutine(VerifyLocalArchive(result));}});
                NetworkClient.RegisterHandler<WarehouseSeamAccepted>(result=>
                {
                    if(finished) return;
                    try
                    {
                        if(!durableVerified || result.snapshot!=verifiedResult.snapshot)
                            throw new InvalidOperationException("Подтверждён другой или незаписанный снимок.");
                        RequireStoppedPlayerIdentity(value.Session.LocalActor,verifiedPlayer,"локальный игрок / завершённый exact receipt barrier");
                        terminalIdentityVerified=true;terminalVerifiedAt=Time.realtimeSinceStartupAsDouble;
                        Finish(verifiedResult);
                    }
                    catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
                });
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<WarehouseSeamDurable>((connection,result)=>
            {
                if(finished || !sent || result.snapshot!=verifiedResult.snapshot || !requiredPeers.Contains(connection.connectionId) ||
                    value.Session.ConnectedActor(connection)?.id!=result.actor) return;
                durablePeers.Add(connection.connectionId);
                TryAccept();
            });
        }
        private void OnRecoveryPersisted(RecoverySnapshot snapshot)
        {
            if(finished || snapshot==null) return;
            persistedSnapshots.Add(snapshot.Id);
            TryAccept();
        }
        private void OnReceiptPrepared(RecoverySnapshot snapshot,ActorState actor)
        {
            if(finished || snapshot==null) return;
            try
            {
                if(receiptWitnesses.ContainsKey(snapshot.Id))
                    throw new InvalidOperationException($"Повторная подготовка exact receipt стыка: snapshot={snapshot.Id}.");
                if(receiptWitnesses.Count>=128 || actor==null)
                    throw new InvalidOperationException($"Нет ограниченного capture игрока для receipt стыка: snapshot={snapshot.Id}, count={receiptWitnesses.Count}.");
                var witness=new ReceiptWitness {player=actor,playerJson=StateJson.ToJson(actor),revision=snapshot.Revision,
                    capturedAt=Time.realtimeSinceStartupAsDouble};
                // This is the detached diagnostic copy. Match the single save rule
                // without modifying either the live actor or the queued actor.
                ContentProvenance.Record(ref actor.weaponContent,ref actor.weaponContentHistory);
                witness.expectedCharacterJson=StateJson.ToJson(actor);
                receiptWitnesses.Add(snapshot.Id,witness);
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
        }
        private void OnReceiptPersisted(RecoverySnapshot snapshot,string characterJson)
        {
            if(finished || snapshot==null) return;
            try
            {
                if(!receiptWitnesses.TryGetValue(snapshot.Id,out var witness) || witness.revision!=snapshot.Revision ||
                    witness.characterJson!=null || characterJson==null)
                    throw new InvalidOperationException($"Запись receipt стыка не связана с единственным подготовленным игроком: snapshot={snapshot.Id}.");
                witness.characterJson=characterJson;witness.persistedAt=Time.realtimeSinceStartupAsDouble;
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
        }
        private void OnRecoveryAcknowledged(int connection,string snapshot,long revision)
        {
            if(finished || snapshot!=finishSnapshot || revision!=finishRevision || !requiredPeers.Contains(connection)) return;
            receiptPeers.Add(connection);TryAccept();
        }
        private void TryAccept()
        {
            if(finished || !Host || acceptedSent || !verifiedResult.success || finishSnapshot!=verifiedResult.snapshot ||
                !persistedSnapshots.Contains(finishSnapshot) || requiredPeers.Count!=runtime.Options.ProbePeers ||
                !requiredPeers.All(receiptPeers.Contains) || !requiredPeers.All(durablePeers.Contains)) return;
            acceptedSent=true;NetworkServer.SendToAll(new WarehouseSeamAccepted {snapshot=finishSnapshot});
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут прохода офис→склад.");
                var session=runtime.Session;var actor=session.LocalActor;
                if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(!traveled) {traveled=true;session.Travel.RequestFactory();}
                seam.Tick();if(seam.Failure!=null) throw new InvalidOperationException(seam.Failure);
                if(!Host || !seam.ContainerReady || sent) return;
                sent=true;StartCoroutine(PersistAndFinish());
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
        }
        private IEnumerator PersistAndFinish()
        {
            yield return null;
            var session=runtime.Session;string exactId=null;long exactRevision=0;
            Action<string,long> prepared=(id,revision)=>
            {
                exactId=id;exactRevision=revision;finishSnapshot=id;finishRevision=revision;
                canonicalCommitWitness=session.ServerWorld.Find(localCommitWitness.id)?.Copy();
                RequirePlayerState(localCommitWitness,canonicalCommitWitness,"frozen локальный хост / synchronous canonical capture");
            };
            session.RecoveryPrepared+=prepared;
            try
            {
                foreach(var connection in NetworkServer.connections.Values)
                    if(session.ConnectedActor(connection) is {connected:true,level:"factory:0"}) requiredPeers.Add(connection.connectionId);
                if(requiredPeers.Count!=runtime.Options.ProbePeers)
                    throw new InvalidOperationException("Перед записью стыка изменился точный состав подключённых игроков.");
                RequirePlayerState(session.LocalActor,session.ServerWorld.Find(session.LocalActor?.id),"локальный хост / канонический мир перед записью");
                localCommitWitness=session.LocalActor.Copy();preCommitCapturedAt=Time.realtimeSinceStartupAsDouble;
                finishWorld=session.ServerWorld.worldId;
                session.CommitWorld();
                if(exactId==null || canonicalCommitWitness==null)
                    throw new InvalidOperationException("Commit стыка не подготовил exact synchronous снимок игрока.");
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
            finally {session.RecoveryPrepared-=prepared;}
            if(finished) yield break;
            string path=Archive(session.ServerWorld.worldId,exactId);
            // The archive is published before the complete queued operation ends.
            // Its existence alone must never substitute for the production completion callback.
            while(!finished && (!persistedSnapshots.Contains(exactId) || !File.Exists(path))) yield return new WaitForSecondsRealtime(.1f);
            if(finished) yield break;
            try
            {
                var snapshot=new RecoverySnapshot(File.ReadAllText(path));var saved=snapshot.CreateLiveWorld();
                if(snapshot.Id!=exactId || snapshot.Revision!=exactRevision || snapshot.WorldId!=finishWorld || snapshot.WorldId!=session.ServerWorld.worldId)
                    throw new InvalidOperationException("После прохода записан другой immutable снимок.");
                RequirePlayerState(localCommitWitness,saved.Find(localCommitWitness.id),"frozen локальный хост / записанный снимок");
                RequirePlayerState(canonicalCommitWitness,saved.Find(localCommitWitness.id),"frozen canonical capture / записанный снимок");
                var players=saved.actors.Where(a=>a.player && a.connected).ToArray();
                if(players.Length!=runtime.Options.ProbePeers || players.Any(a=>a.level!="factory:0"))
                    throw new InvalidOperationException("Записанный снимок потерял точный состав конечных игроков стыка.");
                bool valid=RoomContentProbePersistence.UnchangedOnReinstall(saved,"factory:0") && RoomContentProbePersistence.HasCompleteAuthoredContent(saved,"factory:0");
                verifiedResult=new WarehouseSeamFinish {success=valid,snapshot=snapshot.Id,world=snapshot.WorldId,
                    actors=StateJson.ToJson(players),detail=valid?
                    seamLabel+": стык пройден в обе стороны обычным движением; актёр/владелец/уровень/опора сохранены, мир записан без повторной генерации содержимого.":
                    "Сохранённый склад потерял объекты или изменился при повторной установке."};
                NetworkServer.SendToAll(verifiedResult);
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
        }
        private string Archive(string world,string id)
        {
            if(!Guid.TryParseExact(world,"N",out _) || id?.Length!=64 || !id.All(Uri.IsHexDigit)) throw new InvalidOperationException("Некорректная identity записанного снимка стыка.");
            return Path.Combine(runtime.Session.Saves.WorldDirectoryPath,"Worlds",world,"Snapshots",id+".json");
        }
        private IEnumerator VerifyLocalArchive(WarehouseSeamFinish result)
        {
            string path=Archive(result.world,result.snapshot);
            // For a remote peer this event follows world/profile pair + playtime publication.
            // The host's own profile completion is proved separately by its exact BackupAck.
            while(!finished && (!persistedSnapshots.Contains(result.snapshot) || !File.Exists(path) ||
                !receiptWitnesses.TryGetValue(result.snapshot,out var pendingWitness) || pendingWitness.characterJson==null))
                yield return new WaitForSecondsRealtime(.1f);
            if(finished) yield break;
            try
            {
                var snapshot=new RecoverySnapshot(File.ReadAllText(path));var saved=snapshot.CreateLiveWorld();var actor=runtime.Session.LocalActor;
                // Keep JSON number tokens exact. Re-parsing known motionSentAt through an
                // ActorState DTO would introduce an unrelated Decimal -> Double conversion.
                var expected=StateJson.FromJson<JArray>(result.actors).Single(a=>(string)a["id"]==actor.id);var archived=saved.Find(actor.id);
                if(snapshot.Id!=result.snapshot || snapshot.WorldId!=result.world || !seam.Verified || archived==null ||
                    !JToken.DeepEquals(StateJson.FromJson<JObject>(StateJson.ToJson(archived)),expected) ||
                    !RoomContentProbePersistence.UnchangedOnReinstall(saved,"factory:0") || !RoomContentProbePersistence.HasCompleteAuthoredContent(saved,"factory:0"))
                    throw new InvalidOperationException("Собственный immutable архив потерял конечное тело/владельца/контент стыка.");
                var witness=receiptWitnesses[result.snapshot];
                if(witness.revision!=snapshot.Revision)
                    throw new InvalidOperationException("Exact архив и receipt capture имеют разные состояния мира.");
                RequireStoppedPlayerIdentity(witness.player,archived,"frozen receipt игрок / собственный exact архив");
                // ReceiveWorld may preserve newer local damage. Its frozen receipt
                // must match that operation's profile, not an older world actor.
                var expectedCharacter=StateJson.FromJson<JObject>(witness.expectedCharacterJson);
                var actualCharacter=StateJson.FromJson<JObject>(witness.characterJson);
                RequireExactJson(expectedCharacter,actualCharacter,"frozen receipt / профиль того же completed FIFO job",actor.id);
                if(Host) RequirePlayerState(localCommitWitness,archived,"frozen локальный хост / собственный exact архив");
                receiptCharacterVerified=true;archiveVerifiedAt=Time.realtimeSinceStartupAsDouble;
                playerStateVerified=true;verifiedPlayer=archived;
                durableVerified=true;verifiedResult=result;NetworkClient.Send(new WarehouseSeamDurable {actor=actor.id,snapshot=result.snapshot});
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
        }
        private static void RequirePlayerState(ActorState actual,ActorState saved,string context)
        {
            RequireStoppedPlayerIdentity(actual,saved,context);
            var live=Semantic(actual);var archived=Semantic(saved);
            RequireExactJson(live,archived,context,actual.id);
        }
        private static void RequireStoppedPlayerIdentity(ActorState actual,ActorState saved,string context)
        {
            if(actual==null || saved==null || actual.id!=saved.id || actual.owner!=saved.owner || actual.generation!=saved.generation ||
                actual.character!=saved.character || actual.lifecycleSequence!=saved.lifecycleSequence ||
                actual.controlStateVersion!=saved.controlStateVersion || !actual.player || !saved.player || !actual.connected || !saved.connected ||
                actual.down || saved.down || actual.level!="factory:0" || saved.level!="factory:0" ||
                !float.IsFinite(actual.health) || !float.IsFinite(saved.health) || actual.health<=0 || saved.health<=0 ||
                !float.IsFinite(actual.x+actual.y+actual.vx+actual.vy+saved.x+saved.y+saved.vx+saved.vy) ||
                Vector2.Distance(actual.Position,saved.Position)>.1f || Mathf.Abs(actual.vx)>=.01f || Mathf.Abs(actual.vy)>=.01f ||
                Mathf.Abs(saved.vx)>=.01f || Mathf.Abs(saved.vy)>=.01f)
                throw new InvalidOperationException($"Конечное тело стыка не сохранено/не остановилось: {context}, actor={actual?.id}, actual=({actual?.x},{actual?.y})/{actual?.health}, saved=({saved?.x},{saved?.y})/{saved?.health}.");
        }
        private static JObject Semantic(ActorState value)
        {
            var json=StateJson.FromJson<JObject>(StateJson.ToJson(value));
            // Local presentation publication can lead the canonical actor even at
            // synchronous capture. Compare progress exactly and stop/position
            // through RequireStoppedPlayerIdentity; no HP/receipts are removed.
            foreach(string key in new[]{"revision","poseTime","poseSequence","motionSequence","motionSentAt","motionClockGeneration",
                "weaponAngle","weaponFlipY","weaponTipX","weaponTipY","weaponReach","weaponLift","heldItemPoseX","heldItemPoseY","heldItemPoseValid",
                "x","y","vx","vy"}) json.Remove(key);
            return json;
        }
        private static void RequireExactJson(JObject live,JObject archived,string context,string actor)
        {
            if(!JToken.DeepEquals(live,archived))
            {
                var keys=live.Properties().Select(p=>p.Name).Concat(archived.Properties().Select(p=>p.Name)).Distinct(StringComparer.Ordinal)
                    .Where(key=>!JToken.DeepEquals(live[key],archived[key]));
                throw new InvalidOperationException($"Записанный игрок расходится с captured HP/состоянием/вещами/неизвестными данными: {context}, actor={actor}, fields={string.Join(",",keys)}.");
            }
        }
        private void LateUpdate() {if(!finished) seam?.Capture();}
        private void Finish(WarehouseSeamFinish result)
        {
            if(finished) return;finished=true;var world=runtime.Session.World;var actor=runtime.Session.LocalActor;
            DetachReceipts();
            bool success=result.success && seam.Verified && durableVerified && terminalIdentityVerified && receiptCharacterVerified;
            receiptWitnesses.TryGetValue(result.snapshot??"",out var receipt);
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=result.detail,world=world?.worldId,
                snapshot=result.snapshot,seam=seamLabel,traversalVerified=seam.Verified,savedReloadVerified=durableVerified,reinstallVerified=durableVerified,
                canonicalWriteVerified=persistedSnapshots.Contains(result.snapshot??""),
                productionReceiptVerified=Host?requiredPeers.Count==runtime.Options.ProbePeers && requiredPeers.All(receiptPeers.Contains):durableVerified,
                playerStateVerified=playerStateVerified,
                terminalIdentityVerified=terminalIdentityVerified,receiptCaptureVerified=receiptCharacterVerified,receiptCharacterVerified=receiptCharacterVerified,
                pairDurableVerified=durableVerified && receiptCharacterVerified && terminalIdentityVerified,
                snapshotRevision=finishRevision!=0?finishRevision:receipt?.revision??0,
                snapshotHealth=verifiedPlayer?.health??0,receiptHealth=receipt?.player.health??0,finalHealth=actor?.health??0,
                snapshotPlayerJson=verifiedPlayer==null?null:StateJson.ToJson(verifiedPlayer),receiptPlayerJson=receipt?.playerJson,
                receiptExpectedCharacterJson=receipt?.expectedCharacterJson,receiptCharacterJson=receipt?.characterJson,
                receiptCharacterSha256=receipt?.characterJson==null?null:RecoverySnapshot.Fingerprint(receipt.characterJson),
                terminalPlayerJson=actor==null?null:StateJson.ToJson(actor),
                preCommitPlayerJson=localCommitWitness==null?null:StateJson.ToJson(localCommitWitness),
                preCommitCapturedAt=preCommitCapturedAt,receiptCapturedAt=receipt?.capturedAt??0,receiptPersistedAt=receipt?.persistedAt??0,
                archiveVerifiedAt=archiveVerifiedAt,terminalVerifiedAt=terminalVerifiedAt,
                actor=actor?.id,owner=actor?.owner,level=actor?.level,generation=actor?.generation??0,position=actor?.Position??Vector2.zero,
                props=world?.props.Count(p=>p.level=="factory:0")??0,containers=world?.containers.Count(p=>p.level=="factory:0")??0,
                doors=world?.doors.Count(p=>p.level=="factory:0")??0,npcs=world?.actors.Count(a=>!a.player && a.level=="factory:0")??0},true));
            receiptWitnesses.Clear();localCommitWitness=null;canonicalCommitWitness=null;verifiedPlayer=null;
            Debug.Log("Склад40 live: "+success+", "+result.detail);StartCoroutine(Quit(success));
        }
        private void DetachReceipts()
        {
            if(runtime?.Session==null) return;
            runtime.Session.RecoveryPersisted-=OnRecoveryPersisted;runtime.Session.RecoveryAcknowledged-=OnRecoveryAcknowledged;
            runtime.Session.RecoveryReceiptPrepared-=OnReceiptPrepared;runtime.Session.RecoveryReceiptPersisted-=OnReceiptPersisted;
        }
        private void OnDestroy() {DetachReceipts();receiptWitnesses.Clear();}
        private IEnumerator Quit(bool success) {yield return new WaitForSecondsRealtime(1.2f);Application.Quit(success?0:1);}
    }
}
