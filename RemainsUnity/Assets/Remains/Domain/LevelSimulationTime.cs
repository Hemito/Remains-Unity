using System;
using Remains.Serialization;

namespace Remains.Domain
{
    // Durable gameplay time. Session/transport timestamps never belong to this record.
    [Serializable]
    public sealed class LevelSimulationTime : ExtensibleState
    {
        public int schema=1;
        public string level;
        public double elapsedSeconds;

        public void Validate()
        {
            if(schema!=1 || string.IsNullOrWhiteSpace(level) || !double.IsFinite(elapsedSeconds) || elapsedSeconds<0)
                throw new InvalidOperationException($"Некорректное игровое время: level={level}, schema={schema}, elapsed={elapsedSeconds}.");
        }
    }
}
