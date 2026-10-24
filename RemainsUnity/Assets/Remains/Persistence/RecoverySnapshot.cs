using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Collections.Generic;
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
        // The whole text. A snapshot composed from parts (host FromWorld, client delta) builds it on first use, which
        // for a periodic snapshot is the save writer's thread: the game thread only hashes the parts (Fingerprint).
        public string Json
        {
            get
            {
                var value=Volatile.Read(ref json);
                if(value!=null) return value;
                lock(jsonGate) return json??=SnapshotPartCache.Splice(template,parts);
            }
        }
        private string json;
        // The text into a writer: from the parts when it has not been built (no whole-text string for a save).
        public void WriteTo(System.IO.TextWriter writer)
        {
            var value=Volatile.Read(ref json);
            if(value!=null) writer.Write(value);
            else SnapshotPartCache.WriteTo(template,parts,writer);
        }
        // Whether a file holds exactly this text: its bytes hashed in pieces against the ID; only an unexpected
        // mismatch (a BOM or other encoding) reads the text to compare.
        public bool MatchesFile(string path)=>TextSha256.OfFile(path)==Id || System.IO.File.ReadAllText(path)==Json;
        // The actor parts with this ID and the text of the last one (identity checks of a saved pair without
        // parsing the world); a snapshot without parts reads its validated text.
        internal int ActorParts(string id,out string text)
        {
            text=null;int count=0;
            if(parts!=null)
            {
                string key="actors:"+id;
                foreach(var set in parts) if(set.Name=="actors")
                    for(int i=0;i<set.Keys.Length;i++) if(set.Keys[i]==key) {count++;text=set.Texts[i];}
                return count;
            }
            foreach(var actor in JsonUtility.FromTrustedJson<WorldState>(Json).actors)
                if(actor.id==id) {count++;text=JsonUtility.ToJson(actor);}
            return count;
        }
        private readonly string template;
        private readonly IReadOnlyList<SnapshotPartCache.PartSet> parts;
        private readonly object jsonGate=new();
        private string fileId;
        // File identity stays SHA256 of the exact JSON. Periodic replication uses Manifest instead;
        // durable writers calculate this property before their completion/ACK reaches the game thread.
        public string Id
        {
            get
            {
                var ready=Volatile.Read(ref fileId);if(ready!=null) return ready;
                lock(jsonGate)
                {
                    if(fileId==null)
                        using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: хеш файла"))
                            Volatile.Write(ref fileId,SnapshotPartCache.Fingerprint(template,parts));
                    return fileId;
                }
            }
        }
        public bool TryFileId(out string value) {value=Volatile.Read(ref fileId);return value!=null;}
        public SnapshotManifest Manifest {get;private set;}
        public string ReplicationId=>Manifest.Id;
        private sealed class ManifestMemory {public SnapshotManifest value;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState,ManifestMemory> manifestMemories=new();
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
            this.json = json;
            WorldId = world.worldId;
            Revision = world.revision;
            fileId = Fingerprint(json);
            var split=SnapshotPartCache.Split(world);
            Manifest=SnapshotManifest.Build(split.Template,split.Lists);
            preparedLive = world;
        }

        // From live state: the text is composed from cached parts (SnapshotPartCache), whose strict parse covers
        // what the whole-text round-trip covered; the world rules are checked on the live world the text was
        // written from. No parsed graph is kept: CreateLiveWorld parses the immutable Json when asked.
        private RecoverySnapshot(WorldState world,string template,IReadOnlyList<SnapshotPartCache.PartSet> parts,SnapshotManifest previousManifest=null)
        {
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: правила мира")) Validate(world);
            this.template=template;this.parts=parts;
            WorldId = world.worldId;
            Revision = world.revision;
            using(Remains.Diagnostics.FrameHitchLog.Section("Снимок: манифест")) Manifest=SnapshotManifest.Build(template,parts,previousManifest);
            producedHere = true;
        }

        // A delta-assembled snapshot (WorldReplica.Apply): the text must hash to the expected ID; the live world was
        // built from the strictly parsed template and changed parts plus unchanged live parts, and passes the rules.
        internal static RecoverySnapshot Assembled(string template,IReadOnlyList<SnapshotPartCache.PartSet> parts,string expectedId,WorldState world,
            bool retainPreparedLive=true,SnapshotManifest previousManifest=null)
        {
            if(template==null || parts==null || world==null) throw new ArgumentNullException(world==null?nameof(world):template==null?nameof(template):nameof(parts));
            var snapshot=new RecoverySnapshot(world,template,parts,previousManifest);
            if(snapshot.ReplicationId!=expectedId)
                throw new System.IO.InvalidDataException($"Изменение мира собрано с другим манифестом: ожидался {expectedId}, получен {snapshot.ReplicationId}.");
            if(retainPreparedLive) snapshot.preparedLive=world;
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
            var mines=new HashSet<string>(StringComparer.Ordinal);
            foreach(var mine in world.mines ?? new List<MineState>())
            {
                try {MineRules.Validate(mine);}
                catch(InvalidOperationException error)
                {throw new System.IO.InvalidDataException("Некорректная мина восстановительного снимка: мир="+world.worldId+". "+error.Message,error);}
                if(!mines.Add(mine.id)) throw new System.IO.InvalidDataException("Повтор ID мины в восстановительном снимке: мир="+world.worldId+", мина="+mine.id+".");
            }
            var hazards=new HashSet<string>(StringComparer.Ordinal);
            foreach(var hazard in world.hazards ?? new List<HazardState>())
            {
                try {HazardRules.Validate(hazard);}
                catch(InvalidOperationException error)
                {throw new System.IO.InvalidDataException("Некорректная ловушка восстановительного снимка: мир="+world.worldId+". "+error.Message,error);}
                if(!hazards.Add(hazard.id)) throw new System.IO.InvalidDataException("Повтор ID ловушки в восстановительном снимке: мир="+world.worldId+", ловушка="+hazard.id+".");
            }
        }

        // The caller owns live state until serialization finishes; later mutations cannot change Json.
        // Strictness: every part's text passes StateJson.FromJson (unknown numeric extensions etc.), the
        // skeleton on every call, unchanged list elements once (SnapshotPartCache).
        public static RecoverySnapshot FromWorld(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            return FromComposition(world,SnapshotPartCache.Compose(world));
        }
        // Transport v2: the composition comes from the world tick composer's part texts (no world serialization).
        public static RecoverySnapshot FromComposition(WorldState world,SnapshotPartCache.Composition composition)
        {
            if(world==null || composition==null) throw new ArgumentNullException(world==null?nameof(world):nameof(composition));
            var memory=manifestMemories.GetOrCreateValue(world);
            var snapshot=new RecoverySnapshot(world,composition.Template,composition.Lists,memory.value) {Composition=composition};
            memory.value=snapshot.Manifest;
            lock(producedGate)
            {
                produced.Enqueue(snapshot);
                while(produced.Count>ProducedKept) produced.Dequeue();
            }
            return snapshot;
        }

        // SHA-256 of the UTF-8 text, hex lowercase (TextSha256: streamed, system implementation on Windows).
        public static string Fingerprint(string text)
        {
            if(text==null) throw new ArgumentNullException(nameof(text));
            var hasher=TextSha256.Begin();hasher.Append(text);return hasher.Finish();
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
        private sealed class Receipt {public long revision;public string fileId;}
        private readonly Dictionary<string,Receipt> sent = new();
        private readonly Queue<string> order = new();
        public int Count=>sent.Count;
        public void Clear() {sent.Clear();order.Clear();}
        public void Register(RecoverySnapshot snapshot)
        {
            if (sent.ContainsKey(snapshot.ReplicationId)) return;
            snapshot.TryFileId(out string ready);
            sent.Add(snapshot.ReplicationId,new Receipt {revision=snapshot.Revision,fileId=ready});
            order.Enqueue(snapshot.ReplicationId);
            while (order.Count > 256) sent.Remove(order.Dequeue());
        }
        // Called on the game thread after the canonical save worker completed. No old payload is retained.
        public void Complete(RecoverySnapshot snapshot)
        {
            if(!snapshot.TryFileId(out string ready)) throw new InvalidOperationException("Запись снимка завершена без точного SHA: "+snapshot.ReplicationId);
            if(sent.TryGetValue(snapshot.ReplicationId,out var receipt)) receipt.fileId=ready;
        }
        public bool Pending(long revision)
        {
            foreach(var receipt in sent.Values) if(receipt.revision==revision && receipt.fileId==null) return true;
            return false;
        }
        public bool Matches(string id,long revision)
        {
            if(id==null) return false;
            foreach(var receipt in sent.Values)
                if(receipt.revision==revision && receipt.fileId==id) return true;
            return false;
        }
    }
}
