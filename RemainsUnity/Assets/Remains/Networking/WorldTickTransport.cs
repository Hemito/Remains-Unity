using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mirror;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;

namespace Remains.Networking
{
    // Transport v2: a world tick (WorldTick as UTF-8 JSON) in one or more reliable parts.
    public struct WorldTickMessage : NetworkMessage
    {
        public string world;
        public long seq;
        public int total,offset;
        public byte[] data;
    }

    public static class WorldTickSender
    {
        public const int MaximumBytes=8*1024*1024;
        public static byte[] Encode(WorldTick tick)=>new UTF8Encoding(false).GetBytes(StateJson.ToJson(tick));
        public static void Send(byte[] payload,WorldTick tick,int maximumContentBytes,Action<WorldTickMessage> send)
        {
            if(payload.Length>MaximumBytes) throw new InvalidDataException($"Тик мира {tick.seq} превышает предел {MaximumBytes} байт: {payload.Length}.");
            // Envelope: world string, seq, two ints and the byte-array length prefix (at most 9 bytes).
            int chunk=maximumContentBytes-(tick.world?.Length??0)*3-8-8-9-16;
            if(chunk<=0) throw new InvalidDataException("Транспорт не вмещает заголовок тика мира: "+maximumContentBytes);
            for(int offset=0;offset==0 || offset<payload.Length;offset+=chunk)
            {
                var data=new byte[Math.Min(chunk,payload.Length-offset)];
                Buffer.BlockCopy(payload,offset,data,0,data.Length);
                send(new WorldTickMessage {world=tick.world,seq=tick.seq,total=payload.Length,offset=offset,data=data});
            }
        }
    }

    // Client side: the canonical world (the host's state, never touched by local prediction) advanced by ticks.
    public sealed class WorldTickReceiver
    {
        public WorldState Canonical {get;private set;}
        public WorldTickTexts Texts {get;private set;}
        // The last applied tick's parsed parts (now the canonical objects): the live world takes detached copies.
        public WorldTickApplier.Parsed LastParsed {get;private set;}
        public long Seq {get;private set;}
        public bool Ready=>Canonical!=null;
        private byte[] buffer;private long bufferSeq;private int received;

        public void Reset(WorldState canonical,long seq) {Canonical=canonical;Texts=new WorldTickTexts(canonical);Seq=seq;buffer=null;}
        public void Forget() {Canonical=null;Texts=null;Seq=0;buffer=null;}

        public enum Result {Pending,Ignored,Applied,Gap}
        // A whole tick is parsed and applied to the canonical world, or nothing changes (InvalidDataException).
        public Result Receive(WorldTickMessage message,out WorldTick tick)
        {
            tick=null;
            if(!Ready) return Result.Ignored;   // before the first full world: the full one carries the base seq
            if(message.world!=Canonical.worldId) throw new InvalidDataException($"Тик другого мира: {message.world}, ожидается {Canonical.worldId}.");
            if(message.seq<=Seq) return Result.Ignored;   // already in the full world or applied
            if(message.seq!=Seq+1) return Result.Gap;
            if(message.total<0 || message.total>WorldTickSender.MaximumBytes || message.data==null || message.offset<0 || message.offset+message.data.Length>message.total)
                throw new InvalidDataException($"Некорректная часть тика {message.seq}: offset={message.offset}, total={message.total}.");
            if(message.offset==0) {buffer=new byte[message.total];bufferSeq=message.seq;received=0;}
            else if(buffer==null || bufferSeq!=message.seq || message.offset!=received) throw new InvalidDataException($"Часть тика {message.seq} вне порядка: offset={message.offset}, получено={received}.");
            Buffer.BlockCopy(message.data,0,buffer,message.offset,message.data.Length);received+=message.data.Length;
            if(received<message.total) return Result.Pending;
            var bytes=buffer;buffer=null;
            tick=StateJson.FromJson<WorldTick>(new UTF8Encoding(false,true).GetString(bytes));
            if(tick.seq!=message.seq || tick.world!=message.world) throw new InvalidDataException("Заголовок тика не совпадает с пакетом.");
            LastParsed=WorldTickApplier.Parse(Canonical,tick);
            WorldTickApplier.Apply(Canonical,LastParsed,null);
            Texts.Apply(tick);
            Seq=tick.seq;
            return Result.Applied;
        }

        public ActorState CopyCanonicalActor(string id)
        {
            var actor=Canonical?.Find(id);
            return actor==null?null:ValidatedStateCopy.Copy(actor);
        }

        // Change kinds and part keys of a tick for the world's consumers (null keys: a list changed its composition).
        public static WorldChangeKind Changes(WorldTick tick)
        {
            var kinds=tick.skeleton!=null?WorldChangeKind.Skeleton:WorldChangeKind.None;
            foreach(var list in tick.lists) kinds|=WorldChangeKinds.ForList(list.name);
            return kinds;
        }
        public static HashSet<string> ChangedParts(WorldTick tick)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var list in tick.lists)
            {
                if(list.order!=null) return null;
                if(list.keys!=null) foreach(string key in list.keys) keys.Add(key);
            }
            return keys;
        }
        public static PropState FindBody(WorldState world,string key)
        {
            int colon=key.IndexOf(':');if(colon<0) return null;
            string list=key.Substring(0,colon),id=key.Substring(colon+1);
            if(list=="containers") return world.containers.Find(c=>c.id==id);
            if(list=="props") return world.props.Find(p=>p.id==id);
            return null;
        }
    }
}
