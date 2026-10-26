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
        public int total,offset;
        public byte[] data;
        public long tickSeq;          // transport v2: the world tick this full snapshot includes (later ticks follow it)
    }
    // A client whose world data failed (damaged parts, a tick it cannot apply) asks for a full snapshot.
    public struct WorldResyncRequest : NetworkMessage
    {
        public string world,snapshot,reason;
    }
    public sealed class ReceivedWorld
    {
        public RecoverySnapshot snapshot;
        public string owner,actor;
        public bool resync;   // the data could not be assembled: ask the host for a full snapshot
        public string reason;
        public long tickSeq;          // the world tick this full snapshot includes (later ticks follow it)
    }
    public sealed class WorldReplication
    {
        // Diagnostic only: existing payload bytes, without materializing lazy snapshot JSON.
        public static event Action<string,int> PayloadMeasured;
        private SnapshotTransfer incoming;
        private string incomingId,owner,actor;
        public void Reset() {incoming=null;incomingId=null;owner=null;actor=null;}
        public void Forget()=>Reset();
        public static void Send(NetworkConnectionToClient connection,RecoverySnapshot snapshot,string owner=null,string actor=null,long tickSeq=0)
            =>SendParts(snapshot,part=> {part.owner=part.offset==0?owner:null;part.actor=part.offset==0?actor:null;part.tickSeq=tickSeq;connection.Send(part);});
        internal static void SendParts(RecoverySnapshot snapshot,Action<WorldPartMessage> send)
        {
            // Packed (PayloadCodec): receivers assemble with SnapshotTransfer.AppendPacked.
            byte[] bytes=PayloadCodec.Pack(snapshot.Utf8);
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
        public ReceivedWorld Receive(WorldPartMessage message)
        {
            try {return ReceivePart(message);}
            catch(Exception error) when(error is InvalidDataException || error is InvalidOperationException || error is DecoderFallbackException)
            {return Reject(message,error);}
        }
        // Advancing the canonical replica and publishing its live projection form one admission boundary.
        // A reconciliation/event failure must never leave the next delta based on an unpublished graph.
        public ReceivedWorld ReceiveAndPublish(WorldPartMessage message,Action<ReceivedWorld> publish)
        {
            ReceivedWorld received;
            try {received=Receive(message);}
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
            return new ReceivedWorld {resync=true,reason=$"snapshot={message.snapshot}, offset={message.offset}, total={message.total}: {error.Message}"};
        }
        private ReceivedWorld ReceivePart(WorldPartMessage message)
        {
            using var timing=TransitionTiming.Measure("snapshot.assemble",message.snapshot);
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Снимок: сборка частей (клиент)");
            if(incoming==null)
            {
                if(message.offset!=0) throw new InvalidDataException("Снимок мира начался с неполного пакета.");
                incomingId=message.snapshot;owner=message.owner;actor=message.actor;
                incoming=new SnapshotTransfer(incomingId,SnapshotTransfer.ChunkSize);
            }
            if(message.snapshot!=incomingId) throw new InvalidDataException("Получен другой снимок до завершения текущего.");
            var snapshot=incoming.AppendPacked(message.total,message.offset,message.data);
            if(snapshot==null) return null;
            if(CpuBaselineCapture.Enabled) PayloadMeasured?.Invoke("full-receive",message.total);
            var result=new ReceivedWorld {snapshot=snapshot,owner=owner,actor=actor,tickSeq=message.tickSeq};Reset();
            return result;
        }
    }
}
