using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Remains.Domain;
using UnityEngine;

namespace Remains.Persistence
{
    // Immutable wire payload: never expose its deserialized state for prediction.
    public sealed class RecoverySnapshot
    {
        public string Json { get; }
        public string Id { get; }
        public string WorldId { get; }
        public long Revision { get; }

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
        }

        public static string Fingerprint(string text)
        {
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        public WorldState CreateLiveWorld() => JsonUtility.FromJson<WorldState>(Json);
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
