using System;
using System.IO;
using System.IO.Compression;

namespace Remains.Persistence
{
    // Network packing of large world payloads (protocol 81): world JSON deflates 7–9× (seed104: 2.25 MB → 0.25–0.32 MB),
    // which an internet link needs (a VPN peer was disconnected by a 12 MB reliable backlog of plain JSON).
    // One leading byte: 0 — the raw bytes follow; 1 — the original length (4 bytes, little-endian) and a deflate stream.
    // Unpack bounds the output, so a damaged or hostile length cannot allocate without limit.
    public static class PayloadCodec
    {
        public const int CompressFrom=512;
        private const byte Raw=0,Deflated=1;

        public static byte[] Pack(byte[] raw)
        {
            if(raw==null) throw new ArgumentNullException(nameof(raw));
            if(raw.Length>=CompressFrom)
            {
                using var output=new MemoryStream(raw.Length/4+16);
                output.WriteByte(Deflated);
                output.WriteByte((byte)raw.Length);output.WriteByte((byte)(raw.Length>>8));
                output.WriteByte((byte)(raw.Length>>16));output.WriteByte((byte)(raw.Length>>24));
                using(var deflate=new DeflateStream(output,CompressionLevel.Fastest,leaveOpen:true)) deflate.Write(raw,0,raw.Length);
                if(output.Length<raw.Length+1) return output.ToArray();
            }
            var packed=new byte[raw.Length+1];packed[0]=Raw;
            Buffer.BlockCopy(raw,0,packed,1,raw.Length);
            return packed;
        }

        public static byte[] Unpack(byte[] packed,int maximumBytes)
        {
            if(packed==null || packed.Length==0) throw new InvalidDataException("Пустой сетевой пакет мира.");
            if(packed[0]==Raw)
            {
                if(packed.Length-1>maximumBytes) throw new InvalidDataException($"Сетевой пакет мира больше предела {maximumBytes} байт: {packed.Length-1}.");
                var raw=new byte[packed.Length-1];Buffer.BlockCopy(packed,1,raw,0,raw.Length);
                return raw;
            }
            if(packed[0]!=Deflated) throw new InvalidDataException("Неизвестное сжатие сетевого пакета мира: "+packed[0]+".");
            if(packed.Length<5) throw new InvalidDataException("Сжатый пакет мира без длины.");
            int length=packed[1]|packed[2]<<8|packed[3]<<16|packed[4]<<24;
            if(length<0 || length>maximumBytes) throw new InvalidDataException($"Сжатый пакет мира объявляет {length} байт при пределе {maximumBytes}.");
            var result=new byte[length];
            using(var deflate=new DeflateStream(new MemoryStream(packed,5,packed.Length-5),CompressionMode.Decompress))
            {
                int read=0;
                while(read<length)
                {
                    int count=deflate.Read(result,read,length-read);
                    if(count==0) throw new InvalidDataException($"Сжатый пакет мира короче объявленного: {read} из {length} байт.");
                    read+=count;
                }
                if(deflate.ReadByte()>=0) throw new InvalidDataException($"Сжатый пакет мира длиннее объявленных {length} байт.");
            }
            return result;
        }
    }
}
