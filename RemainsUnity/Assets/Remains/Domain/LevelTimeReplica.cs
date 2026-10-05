using System;
using System.Collections.Generic;

namespace Remains.Domain
{
    [Serializable]
    public sealed class LevelTimeStamp
    {
        public string level;
        public double elapsedSeconds;
        public bool running;
    }

    // A live session epoch is distinct from the saved simulation clock. Network time
    // only measures the interval after this explicitly confirmed epoch.
    public sealed class LevelTimeReplica
    {
        private readonly Dictionary<string,LevelTimeStamp> clocks=new(StringComparer.Ordinal);
        private double sentAt;
        public long Sequence {get;private set;}=-1;
        public string WorldId {get;private set;}
        public bool Receive(string world,long sequence,double networkTime,LevelTimeStamp[] stamps)
        {
            if(string.IsNullOrWhiteSpace(world) || sequence<0 || !double.IsFinite(networkTime) || networkTime<0 || stamps==null)
                throw new InvalidOperationException("Некорректный сетевой отсчёт часов уровня.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var stamp in stamps)
                if(stamp==null || string.IsNullOrWhiteSpace(stamp.level) || !ids.Add(stamp.level) ||
                    !double.IsFinite(stamp.elapsedSeconds) || stamp.elapsedSeconds<0)
                    throw new InvalidOperationException($"Некорректные часы в пакете: мир={world}, sequence={sequence}, level={stamp?.level}.");
            if(WorldId==world && sequence<=Sequence) return false;
            clocks.Clear();
            foreach(var stamp in stamps) clocks.Add(stamp.level,new LevelTimeStamp
                {level=stamp.level,elapsedSeconds=stamp.elapsedSeconds,running=stamp.running});
            WorldId=world;Sequence=sequence;sentAt=networkTime;return true;
        }
        public bool Has(string world,string level)=>WorldId==world && level!=null && clocks.ContainsKey(level);
        public double Read(string world,string level,double networkTime)
        {
            if(!double.IsFinite(networkTime) || networkTime<0) throw new ArgumentOutOfRangeException(nameof(networkTime));
            if(!Has(world,level)) throw new InvalidOperationException($"Нет подтверждённых часов уровня: мир={world}, level={level}.");
            var clock=clocks[level];
            double result=clock.elapsedSeconds+(clock.running?Math.Max(0,networkTime-sentAt):0);
            if(!double.IsFinite(result)) throw new InvalidOperationException($"Часы уровня переполнены: мир={world}, level={level}.");
            return result;
        }
        public bool Running(string world,string level)
            =>Has(world,level)?clocks[level].running:throw new InvalidOperationException("Нет подтверждённых часов уровня "+level);
        public void Reset() {clocks.Clear();WorldId=null;Sequence=-1;sentAt=0;}
    }
}
