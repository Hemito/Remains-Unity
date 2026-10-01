using System;
using System.IO;
using System.Text;
using Mirror;
using Remains.Persistence;

namespace Remains.Networking
{
    public struct WorldPartMessage : NetworkMessage
    {
        public string snapshot,owner,actor;
        public int total,offset;
        public byte[] data;
    }
    public sealed class ReceivedWorld
    {
        public RecoverySnapshot snapshot;
        public string owner,actor;
    }
    public sealed class WorldReplication
    {
        private SnapshotTransfer incoming;
        private string incomingId,owner,actor;
        public void Reset() {incoming=null;incomingId=null;owner=null;actor=null;}
        public static void Send(NetworkConnectionToClient connection,RecoverySnapshot snapshot,string owner=null,string actor=null)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(snapshot.Json);
            if(bytes.Length>SnapshotTransfer.MaximumBytes) throw new InvalidDataException("Снимок мира превышает текущий предел 32 МиБ.");
            // Enqueue this reliable snapshot contiguously. Later reliable actor events
            // must follow its final chunk, so an older snapshot cannot overwrite them.
            for(int offset=0;offset<bytes.Length;offset+=SnapshotTransfer.ChunkSize)
            {
                var data=new byte[Math.Min(SnapshotTransfer.ChunkSize,bytes.Length-offset)];
                Buffer.BlockCopy(bytes,offset,data,0,data.Length);
                connection.Send(new WorldPartMessage {snapshot=snapshot.Id,total=bytes.Length,offset=offset,data=data,
                    owner=offset==0?owner:null,actor=offset==0?actor:null});
            }
        }
        public ReceivedWorld Receive(WorldPartMessage message)
        {
            if(incoming==null)
            {
                if(message.offset!=0) throw new InvalidDataException("Снимок мира начался с неполного пакета.");
                incomingId=message.snapshot;incoming=new SnapshotTransfer(incomingId);owner=message.owner;actor=message.actor;
            }
            if(message.snapshot!=incomingId) throw new InvalidDataException("Получен другой снимок до завершения текущего.");
            var snapshot=incoming.Append(message.total,message.offset,message.data);
            if(snapshot==null) return null;
            var result=new ReceivedWorld {snapshot=snapshot,owner=owner,actor=actor};Reset();return result;
        }
    }
}
