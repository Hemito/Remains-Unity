using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    // Only the world coordinator advances these durable records. Owners will sample
    // confirmed motion segments locally; this class does not move any passenger.
    public sealed class LevelTimeAuthority
    {
        private readonly Dictionary<string,LevelSimulationTime> clocks=new(StringComparer.Ordinal);
        private readonly List<LevelSimulationTime> active=new();
        public LevelTimeAuthority(WorldState world)
        {
            LevelTimeMigration.ValidateWorld(world);
            if(world.levelTimeVersion!=1) throw new InvalidOperationException("Часы мира не установлены: "+world.worldId);
            foreach(var state in world.levelTimes) clocks.Add(state.level,state);
        }
        public double Read(string level)=>Record(level).elapsedSeconds;
        private LevelSimulationTime Record(string level)=>level!=null && clocks.TryGetValue(level,out var state)?state:
            throw new InvalidOperationException("Не установлены часы уровня: "+level);

        public void Advance(double delta,Func<string,bool> maySimulate)
        {
            if(!double.IsFinite(delta) || delta<0) throw new ArgumentOutOfRangeException(nameof(delta),delta,"Шаг игрового времени должен быть конечным и неотрицательным.");
            if(maySimulate==null) throw new ArgumentNullException(nameof(maySimulate));
            // Validate every candidate before mutating any record. A bad mod policy
            // or overflow must not leave a partly advanced set of level clocks.
            active.Clear();
            foreach(var pair in clocks)
            {
                pair.Value.Validate();
                if(!double.IsFinite(pair.Value.elapsedSeconds+delta))
                    throw new InvalidOperationException("Переполнение часов уровня: "+pair.Key);
                if(maySimulate(pair.Key)) active.Add(pair.Value);
            }
            foreach(var record in active) record.elapsedSeconds+=delta;
        }
    }
}
