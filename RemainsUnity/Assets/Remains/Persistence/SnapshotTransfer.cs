using System;
using System.IO;
using System.Text;

namespace Remains.Persistence
{
    // Reliable ordered chunks, explicit limits, final content hash before publication.
    public sealed class SnapshotTransfer
    {
        public const int ChunkSize=8192;
        public const int MaximumChunkBytes=256*1024;
        public const int MaximumBytes=32*1024*1024;
        private readonly string expected;
        private byte[] buffer;
        private int received;
        private readonly int chunkLimit;
        public int ReceivedBytes=>received;
        public SnapshotTransfer(string expectedHash,int maximumChunkSize=ChunkSize)
        {
            if(maximumChunkSize<=0 || maximumChunkSize>MaximumChunkBytes)
                throw new ArgumentOutOfRangeException(nameof(maximumChunkSize),"Недопустимый предел части снимка.");
            expected=expectedHash;chunkLimit=maximumChunkSize;
        }
        public RecoverySnapshot Append(int total,int offset,byte[] chunk)
        {
            var bytes=AppendRaw(total,offset,chunk);
            if(bytes==null) return null;
            var snapshot=RecoverySnapshot.FromReceived(new UTF8Encoding(false,true).GetString(bytes));
            if(snapshot.Id!=expected) throw new InvalidDataException("Хеш переданного сохранения не совпадает с выбранной копией.");
            return snapshot;
        }
        // The complete payload once all chunks arrived (null before); same bounds, order and repeat checks.
        public byte[] AppendRaw(int total,int offset,byte[] chunk)
        {
            if(total<=0 || total>MaximumBytes || chunk==null || chunk.Length==0 || chunk.Length>chunkLimit ||
                offset<0 || offset>total-chunk.Length)
                throw new InvalidDataException("Некорректные границы пакета сохранения.");
            if(buffer==null) buffer=new byte[total];
            if(total!=buffer.Length || offset>received) throw new InvalidDataException("Нарушен порядок пакетов сохранения.");
            if(offset<received)
            {
                if(offset+chunk.Length>received) throw new InvalidDataException("Перекрытие пакетов сохранения.");
                for(int i=0;i<chunk.Length;i++) if(buffer[offset+i]!=chunk[i]) throw new InvalidDataException("Повторный пакет сохранения отличается от исходного.");
                return null;
            }
            Buffer.BlockCopy(chunk,0,buffer,offset,chunk.Length);received+=chunk.Length;
            return received==buffer.Length?buffer:null;
        }
    }
}
