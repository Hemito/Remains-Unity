using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mirror;
using Remains.Persistence;
using Remains.Diagnostics;
using Remains.Domain;

namespace Remains.Networking
{
    public struct WorldPartMessage : NetworkMessage
    {
        public string snapshot,owner,actor;
        public string baseSnapshot;   // delta replication: null = full snapshot; else a WorldDeltaPayload from this base
        public int total,offset;
        public byte[] data;
        public long tickSeq;          // transport v2: the world tick this full snapshot includes (later ticks follow it)
    }
    // A client whose delta failed (other base, hash mismatch, no replica) asks for a full snapshot.
    public struct WorldResyncRequest : NetworkMessage
    {
        public string world,snapshot,reason;
    }
    public sealed class ReceivedWorld
    {
        public RecoverySnapshot snapshot;
        public WorldState live;
        public HashSet<string> reusedActors;
        public Func<string,ActorState> copyCanonicalActor;
        public Func<WorldState,string,PropState> findBody;
        public string owner,actor;
        public bool resync;   // a delta could not be applied: ask the host for a full snapshot
        public WorldChangeKind changes=WorldChangeKind.All;   // a delta: its changed part lists, the skeleton and actors
        public HashSet<string> changedParts;   // a delta keeping every list's composition: the changed part keys; else null
        public string reason;
        public long tickSeq;          // a full snapshot: the world tick it includes
        public bool receiptOnly;      // transport v2: a recovery delta (the live world follows ticks, not snapshots)
    }
    public sealed class WorldReplication
    {
        // Diagnostic only: existing payload bytes, without materializing lazy snapshot JSON.
        public static event Action<string,int> PayloadMeasured;
        private SnapshotTransfer incoming;
        private string incomingId,owner,actor,incomingBase;
        private WorldReplica replica;
        public void Reset() {incoming=null;incomingId=null;owner=null;actor=null;incomingBase=null;}
        public void Forget() {Reset();replica=null;}
        // Transport v2: deltas keep the recovery copy only; the live world is advanced by world ticks.
        public bool LiveFromTicks;
        public static void Send(NetworkConnectionToClient connection,RecoverySnapshot snapshot,string owner=null,string actor=null,long tickSeq=0)
            =>SendParts(snapshot,part=> {part.owner=part.offset==0?owner:null;part.actor=part.offset==0?actor:null;part.tickSeq=tickSeq;connection.Send(part);});
        internal static void SendParts(RecoverySnapshot snapshot,Action<WorldPartMessage> send)
        {
            byte[] bytes=snapshot.Utf8;
            if(CpuBaselineCapture.Enabled) PayloadMeasured?.Invoke("full-send",bytes.Length);
            if(bytes.Length>SnapshotTransfer.MaximumBytes) throw new InvalidDataException("Снимок мира превышает текущий предел 32 МиБ.");
            // Enqueue this reliable snapshot contiguously. Later reliable actor events
            // must follow its final chunk, so an older snapshot cannot overwrite them.
            for(int offset=0;offset<bytes.Length;offset+=SnapshotTransfer.ChunkSize)
            {
                var data=new byte[Math.Min(SnapshotTransfer.ChunkSize,bytes.Length-offset)];
                Buffer.BlockCopy(bytes,offset,data,0,data.Length);
                send(new WorldPartMessage {snapshot=snapshot.Id,total=bytes.Length,offset=offset,data=data});
            }
        }
        // One encoded delta for every recipient on the same base.
        public static void SendDelta(NetworkConnectionToClient connection,string baseSnapshot,string snapshot,byte[] payload)
            =>SendDeltaParts(baseSnapshot,snapshot,payload,NetworkMessages.MaxContentSize(Channels.Reliable),part=>connection.Send(part));
        public static void SendDeltaParts(string baseSnapshot,string snapshot,byte[] payload,int maximumContentBytes,Action<WorldPartMessage> send)
        {
            if(payload==null || payload.Length==0 || send==null || string.IsNullOrEmpty(baseSnapshot) || string.IsNullOrEmpty(snapshot))
                throw new ArgumentException("Изменение мира: нет данных, получателя или идентификатора базы/результата.");
            if(CpuBaselineCapture.Enabled) PayloadMeasured?.Invoke("delta-send",payload.Length);
            if(payload.Length>SnapshotTransfer.MaximumBytes) throw new InvalidDataException("Изменение мира превышает текущий предел 32 МиБ.");
            // Exact string/int envelope, plus the largest possible varuint byte-array length prefix.
            // Transport still fragments the reliable message at MTU; only Mirror envelopes are coalesced.
            using var header=NetworkWriterPool.Get();
            header.WriteString(snapshot);header.WriteString(null);header.WriteString(null);header.WriteString(baseSnapshot);
            header.WriteInt(payload.Length);header.WriteInt(0);
            int chunkSize=Math.Min(SnapshotTransfer.MaximumChunkBytes,maximumContentBytes-header.Position-9);
            if(chunkSize<=0) throw new InvalidDataException("Транспорт не вмещает заголовок изменения мира: "+maximumContentBytes);
            for(int offset=0;offset<payload.Length;offset+=chunkSize)
            {
                var data=new byte[Math.Min(chunkSize,payload.Length-offset)];
                Buffer.BlockCopy(payload,offset,data,0,data.Length);
                send(new WorldPartMessage {snapshot=snapshot,baseSnapshot=baseSnapshot,total=payload.Length,offset=offset,data=data});
            }
        }
        // Projectiles are rebuilt; actors only notify consumers when their live projection was replaced.
        private static WorldChangeKind Changes(WorldDeltaPayload payload)
        {
            var kinds=WorldChangeKind.Skeleton|WorldChangeKind.Projectiles;
            foreach(var list in payload.lists)
                if((list.keys?.Length??0)>0 || list.order!=null) kinds|=WorldChangeKinds.ForList(list.name);
            return kinds;
        }
        // The changed part keys ("containers:<id>"...) when no list changed its composition (order); null otherwise.
        private static HashSet<string> ChangedParts(WorldDeltaPayload payload)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var list in payload.lists)
            {
                if(list.order!=null) return null;
                if(list.keys!=null) foreach(string key in list.keys) keys.Add(key);
            }
            return keys;
        }
        public ReceivedWorld Receive(WorldPartMessage message,Func<WorldState> current)
        {
            try {return ReceivePart(message,current);}
            catch(Exception error) when(error is InvalidDataException || error is InvalidOperationException || error is DecoderFallbackException)
            {return Reject(message,error);}
        }
        // Advancing the canonical replica and publishing its live projection form one admission boundary.
        // A reconciliation/event failure must never leave the next delta based on an unpublished graph.
        public ReceivedWorld ReceiveAndPublish(WorldPartMessage message,Func<WorldState> current,Action<ReceivedWorld> publish)
        {
            ReceivedWorld received;
            try {received=Receive(message,current);}
            catch(Exception error)
            {
                Forget();
                // Damaged or mismatched data: a full snapshot repairs it.
                if(error is InvalidDataException || error is InvalidOperationException || error is DecoderFallbackException)
                    return Reject(message,error);
                throw; // Unexpected programming failures remain visible; their replica is invalidated too.
            }
            if(received==null || received.resync) return received;
            try {publish(received);}
            catch(Exception error)
            {
                Forget();
                // Reconciliation conflicts (an ordinary update) may be transient: a full snapshot can repair them. The
                // full exception is logged here (only its message travels as the reason); the host bounds repeated
                // requests per connection, so a persistent failure ends the connection instead of looping.
                // A failed admission (Welcome) or an unexpected exception type is a session failure.
                if(received.owner==null && (error is InvalidDataException || error is InvalidOperationException))
                {
                    UnityEngine.Debug.LogWarning($"Публикация принятого мира не удалась, запрашивается полный снимок: snapshot={message.snapshot}. {error}");
                    return Reject(message,error);
                }
                // The original exception propagates (its type and stack); the mark tells the session to stop.
                error.Data[PublishFailureKey]=message.snapshot;
                throw;
            }
            return received;
        }
        // Exception.Data key on a publish failure that a resync cannot repair (the session must stop).
        public const string PublishFailureKey="remains.world-publish-failed";
        public static bool IsPublishFailure(Exception error)=>error.Data.Contains(PublishFailureKey);
        private ReceivedWorld Reject(WorldPartMessage message,Exception error)
        {
            Forget();
            return new ReceivedWorld {resync=true,reason=$"base={message.baseSnapshot}, snapshot={message.snapshot}, offset={message.offset}, total={message.total}: {error.Message}"};
        }
        private ReceivedWorld ReceivePart(WorldPartMessage message,Func<WorldState> current)
        {
            using var timing=TransitionTiming.Measure("snapshot.assemble",message.snapshot);
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Снимок: сборка частей (клиент)");
            if(incoming==null)
            {
                if(message.offset!=0) throw new InvalidDataException("Снимок мира начался с неполного пакета.");
                incomingId=message.snapshot;owner=message.owner;actor=message.actor;incomingBase=message.baseSnapshot;
                incoming=new SnapshotTransfer(incomingId,incomingBase==null?SnapshotTransfer.ChunkSize:SnapshotTransfer.MaximumChunkBytes);
            }
            if(message.snapshot!=incomingId || message.baseSnapshot!=incomingBase) throw new InvalidDataException("Получен другой снимок до завершения текущего.");
            if(incomingBase!=null)
            {
                var bytes=incoming.AppendRaw(message.total,message.offset,message.data);
                if(bytes==null) return null;
                if(CpuBaselineCapture.Enabled) PayloadMeasured?.Invoke("delta-receive",bytes.Length);
                string baseId=incomingBase;var owned=owner;var acting=actor;Reset();
                try
                {
                    if(replica==null) throw new InvalidDataException("Нет копии частей мира для применения изменения.");
                    WorldDeltaPayload payload;
                    using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: разбор изменения (клиент)"))
                        payload=Remains.Serialization.StateJson.FromJson<WorldDeltaPayload>(new UTF8Encoding(false,true).GetString(bytes));
                    if(payload.snapshot!=message.snapshot || payload.baseSnapshot!=baseId) throw new InvalidDataException("Заголовок изменения мира не совпадает с пакетом.");
                    RecoverySnapshot applied;WorldState live;HashSet<string> reusedActors;
                    var appliedReplica=replica;
                    if(LiveFromTicks)
                    {
                        using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: копия восстановления (клиент)")) applied=appliedReplica.Apply(payload,current());
                        return new ReceivedWorld {snapshot=applied,owner=owned,actor=acting,receiptOnly=true};
                    }
                    using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: применение изменения (клиент)")) applied=appliedReplica.ApplyLive(payload,current(),out live,out reusedActors);
                    Remains.Diagnostics.FrameHitchLog.Note($"изменение мира: частей {WorldDelta.ChangedParts(payload)}, байт {bytes.Length}");
                    return new ReceivedWorld {snapshot=applied,live=live,reusedActors=reusedActors,copyCanonicalActor=appliedReplica.CopyCanonicalActor,findBody=appliedReplica.FindLiveBody,
                        owner=owned,actor=acting,changes=Changes(payload),changedParts=ChangedParts(payload)};
                }
                catch(Exception error) when (error is InvalidDataException || error is InvalidOperationException)
                {
                    replica=null;
                    return new ReceivedWorld {resync=true,reason=$"base={baseId}, snapshot={message.snapshot}: {error.Message}"};
                }
            }
            var snapshot=incoming.Append(message.total,message.offset,message.data);
            if(snapshot==null) return null;
            if(CpuBaselineCapture.Enabled) PayloadMeasured?.Invoke("full-receive",message.total);
            var result=new ReceivedWorld {snapshot=snapshot,owner=owner,actor=actor,tickSeq=message.tickSeq};Reset();
            // A full snapshot (join, resync, host without a base): the copy of parts that deltas advance from.
            try {using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: раскладка на части (клиент)")) replica=WorldReplica.FromSnapshot(snapshot);}
            catch(InvalidDataException error)
            {
                replica=null;
                UnityEngine.Debug.LogWarning("Копия частей мира не построена; изменения будут запрошены полным снимком: "+error.Message);
            }
            return result;
        }
    }
}
