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
    }
    // A client whose delta failed (other base, hash mismatch, no replica) asks for a full snapshot.
    public struct WorldResyncRequest : NetworkMessage
    {
        public string world,snapshot,reason;
    }
    public sealed class ReceivedWorld
    {
        public RecoverySnapshot snapshot;
        public string owner,actor;
        public bool resync;   // a delta could not be applied: ask the host for a full snapshot
        public WorldChangeKind changes=WorldChangeKind.All;   // a delta: its changed part lists, the skeleton and actors
        public HashSet<string> changedParts;   // a delta keeping every list's composition: the changed part keys; else null
        public string reason;
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
        public static void Send(NetworkConnectionToClient connection,RecoverySnapshot snapshot,string owner=null,string actor=null)
            =>SendParts(snapshot,part=> {part.owner=part.offset==0?owner:null;part.actor=part.offset==0?actor:null;connection.Send(part);});
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
        {
            if(CpuBaselineCapture.Enabled) PayloadMeasured?.Invoke("delta-send",payload.Length);
            if(payload.Length>SnapshotTransfer.MaximumBytes) throw new InvalidDataException("Изменение мира превышает текущий предел 32 МиБ.");
            for(int offset=0;offset<payload.Length;offset+=SnapshotTransfer.ChunkSize)
            {
                var data=new byte[Math.Min(SnapshotTransfer.ChunkSize,payload.Length-offset)];
                Buffer.BlockCopy(payload,offset,data,0,data.Length);
                connection.Send(new WorldPartMessage {snapshot=snapshot,baseSnapshot=baseSnapshot,total=payload.Length,offset=offset,data=data});
            }
        }
        // Actors and projectiles are always included: their objects are replaced on every applied delta (WorldReplica.Apply).
        private static WorldChangeKind Changes(WorldDeltaPayload payload)
        {
            var kinds=WorldChangeKind.Skeleton|WorldChangeKind.Actors|WorldChangeKind.Projectiles;
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
            using var timing=TransitionTiming.Measure("snapshot.assemble",message.snapshot);
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Снимок: сборка частей (клиент)");
            if(incoming==null)
            {
                if(message.offset!=0) throw new InvalidDataException("Снимок мира начался с неполного пакета.");
                incomingId=message.snapshot;incoming=new SnapshotTransfer(incomingId);owner=message.owner;actor=message.actor;incomingBase=message.baseSnapshot;
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
                    RecoverySnapshot applied;
                    using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: применение изменения (клиент)")) applied=replica.Apply(payload,current());
                    Remains.Diagnostics.FrameHitchLog.Note($"изменение мира: частей {WorldDelta.ChangedParts(payload)}, байт {bytes.Length}");
                    return new ReceivedWorld {snapshot=applied,owner=owned,actor=acting,changes=Changes(payload),changedParts=ChangedParts(payload)};
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
            var result=new ReceivedWorld {snapshot=snapshot,owner=owner,actor=actor};Reset();
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
