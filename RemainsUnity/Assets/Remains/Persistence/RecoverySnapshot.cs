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

        public RecoverySnapshot(string json)
        {
            var world = JsonUtility.FromJson<WorldState>(json);
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
            Json = json;
            WorldId = world.worldId;
            Revision = world.revision;
            Id = Fingerprint(json);
            preparedLive = world;
        }

        // The caller owns live state until serialization finishes; later mutations cannot change Json.
        // Keep the strict round-trip here: StateJson.FromJson additionally rejects unknown numeric
        // extensions which the live domain validators and serializer callbacks do not validate.
        // Removing this parse requires an equivalent serializer/extension validation contract.
        public static RecoverySnapshot FromWorld(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            return new RecoverySnapshot(JsonUtility.ToJson(world));
        }

        public static string Fingerprint(string text)
        {
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        // Exactly one consumer takes ownership of the already validated parse.
        // The snapshot retains no reference after transfer; concurrent/subsequent
        // consumers independently parse immutable Json and cannot see live edits.
        public WorldState CreateLiveWorld()
            => Interlocked.Exchange(ref preparedLive, null) ?? JsonUtility.FromJson<WorldState>(Json);
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
