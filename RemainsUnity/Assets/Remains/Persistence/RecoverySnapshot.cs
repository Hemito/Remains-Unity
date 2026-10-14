using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Remains.Domain;
using UnityEngine;

namespace Remains.Persistence
{
    // Immutable wire payload. Its validated graph is transferred to one caller,
    // never shared with subsequent live consumers.
    public sealed class RecoverySnapshot
    {
        public string Json { get; }
        public string Id { get; }
        public string WorldId { get; }
        public long Revision { get; }
        private WorldState preparedLive;
        // The parts this snapshot was composed from (FromWorld on the host): the base or target of a delta.
        public SnapshotPartCache.Composition Composition {get;private set;}
        private byte[] utf8;
        // UTF-8 payload, encoded once per snapshot for all recipients (not once per connection).
        public byte[] Utf8=>utf8??=new UTF8Encoding(false).GetBytes(Json);
        // Composed and validated by this process (FromWorld): live copies may use the trusted reader.
        private readonly bool producedHere;
        // The last few snapshots this process produced, by ID: its own host client receives them back through the
        // network path and must not strictly re-parse a text it has just validated (TD-15).
        private static readonly object producedGate=new();
        private static readonly Queue<RecoverySnapshot> produced=new();
        private const int ProducedKept=4;

        public RecoverySnapshot(string json)
        {
            var world = JsonUtility.FromJson<WorldState>(json);
            Validate(world);
            Json = json;
            WorldId = world.worldId;
            Revision = world.revision;
            Id = Fingerprint(json);
            preparedLive = world;
        }

        // From live state: the text is composed from cached parts (SnapshotPartCache), whose strict parse covers
        // what the whole-text round-trip covered; the world rules are checked on the live world the text was
        // written from. No parsed graph is kept: CreateLiveWorld parses the immutable Json when asked.
        private RecoverySnapshot(WorldState world, string json)
        {
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: правила мира")) Validate(world);
            Json = json;
            WorldId = world.worldId;
            Revision = world.revision;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: хеш")) Id = Fingerprint(json);
            producedHere = true;
        }

        // A delta-assembled snapshot (WorldReplica.Apply): the text must hash to the expected ID; the live world was
        // built from the strictly parsed template and changed parts plus unchanged live parts, and passes the rules.
        internal static RecoverySnapshot Assembled(string json,string expectedId,WorldState world)
        {
            if(json==null || world==null) throw new ArgumentNullException(json==null?nameof(json):nameof(world));
            var snapshot=new RecoverySnapshot(world,json);
            if(snapshot.Id!=expectedId)
                throw new System.IO.InvalidDataException($"Изменение мира собрано с другим хешем: ожидался {expectedId}, получен {snapshot.Id}.");
            snapshot.preparedLive=world;
            return snapshot;
        }

        // A received text: the snapshot object itself when this process produced exactly this text, otherwise the
        // strict parse (any foreign or altered text).
        public static RecoverySnapshot FromReceived(string json)
        {
            if(json==null) throw new ArgumentNullException(nameof(json));
            string id=Fingerprint(json);
            lock(producedGate)
                foreach(var own in produced)
                    if(own.Id==id && string.Equals(own.Json,json,StringComparison.Ordinal)) return own;
            return new RecoverySnapshot(json);
        }

        private static void Validate(WorldState world)
        {
            if (world == null || world.schema != 1 || !Guid.TryParseExact(world.worldId, "N", out _))
                throw new System.IO.InvalidDataException("Некорректный восстановительный снимок мира.");
            try {LevelTimeMigration.ValidateWorld(world);}
            catch(InvalidOperationException error)
            {
                throw new System.IO.InvalidDataException(
                    "Некорректные часы восстановительного снимка: мир="+world.worldId+". "+error.Message,error);
            }
            try {MechanismPersistence.ValidateWorld(world);}
            catch(InvalidOperationException error)
            {
                throw new System.IO.InvalidDataException("Некорректные механизмы восстановительного снимка: мир="+world.worldId+". "+error.Message,error);
            }
            try {ContainerAppearancePersistence.ValidateWorld(world);}
            catch(InvalidOperationException error)
            {
                throw new System.IO.InvalidDataException("Некорректный вид контейнеров восстановительного снимка: мир="+world.worldId+". "+error.Message,error);
            }
            try {PropBodyRules.ValidateWorld(world);}
            catch(InvalidOperationException error)
            {
                throw new System.IO.InvalidDataException("Некорректные физические тела восстановительного снимка: мир="+world.worldId+". "+error.Message,error);
            }
            try {CosmeticEquipment.ValidateWorld(world);}
            catch(InvalidOperationException error)
            {
                throw new System.IO.InvalidDataException("Некорректное визуальное снаряжение восстановительного снимка: мир="+world.worldId+". "+error.Message,error);
            }
        }

        // The caller owns live state until serialization finishes; later mutations cannot change Json.
        // Strictness: every part's text passes StateJson.FromJson (unknown numeric extensions etc.), the
        // skeleton on every call, unchanged list elements once (SnapshotPartCache).
        public static RecoverySnapshot FromWorld(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            var composition=SnapshotPartCache.Compose(world);
            var snapshot=new RecoverySnapshot(world, composition.Json) {Composition=composition};
            lock(producedGate)
            {
                produced.Enqueue(snapshot);
                while(produced.Count>ProducedKept) produced.Dequeue();
            }
            return snapshot;
        }

        // SHA-256 of the UTF-8 text, hex lowercase. Encoded in 64 KB pieces into a reused buffer: the former
        // whole-text GetBytes allocated ~1.25 MB per factory snapshot (GC pressure), the result is identical.
        [ThreadStatic] private static byte[] fingerprintBuffer;
        [ThreadStatic] private static SHA256 fingerprintHash;
        private static readonly UTF8Encoding fingerprintEncoding=new(false);
        public static string Fingerprint(string text)
        {
            if(text==null) throw new ArgumentNullException(nameof(text));
            const int piece=16384;   // chars per piece; UTF-8 needs at most 3 bytes per UTF-16 unit
            var buffer=fingerprintBuffer??=new byte[piece*3+4];
            var sha=fingerprintHash??=SHA256.Create();
            sha.Initialize();
            var encoder=fingerprintEncoding.GetEncoder();
            for(int at=0;at<text.Length;)
            {
                int count=Math.Min(piece,text.Length-at);
                // Do not split a surrogate pair between pieces (the encoder would carry it, but keep pieces whole).
                if(at+count<text.Length && char.IsHighSurrogate(text[at+count-1])) count--;
                int bytes=encoder.GetBytes(text.AsSpan(at,count),buffer.AsSpan(),at+count>=text.Length);
                sha.TransformBlock(buffer,0,bytes,null,0);
                at+=count;
            }
            sha.TransformFinalBlock(buffer,0,0);
            var hash=sha.Hash;
            var hex=new char[hash.Length*2];
            const string digits="0123456789abcdef";
            for(int i=0;i<hash.Length;i++) {hex[i*2]=digits[hash[i]>>4];hex[i*2+1]=digits[hash[i]&15];}
            return new string(hex);
        }

        // Exactly one consumer takes ownership of the already validated parse.
        // The snapshot retains no reference after transfer; concurrent/subsequent
        // consumers independently parse immutable Json and cannot see live edits.
        public WorldState CreateLiveWorld()
            => Interlocked.Exchange(ref preparedLive, null) ??
               (producedHere ? JsonUtility.FromTrustedJson<WorldState>(Json) : JsonUtility.FromJson<WorldState>(Json));
    }

    // A revision alone cannot identify a snapshot: pose packets do not increment it.
    public sealed class RecoveryReceiptGate
    {
        private readonly Dictionary<string, long> sent = new();
        private readonly Queue<string> order = new();
        public void Clear() { sent.Clear(); order.Clear(); }
        public void Register(RecoverySnapshot snapshot)
        {
            if (sent.ContainsKey(snapshot.Id)) return;
            sent.Add(snapshot.Id, snapshot.Revision);
            order.Enqueue(snapshot.Id);
            while (order.Count > 256) sent.Remove(order.Dequeue());
        }
        public bool Matches(string id, long revision) =>
            id != null && sent.TryGetValue(id, out long actual) && actual == revision;
    }
}
