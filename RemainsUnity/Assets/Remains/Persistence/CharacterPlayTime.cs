using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Remains.Serialization;
using Remains.Diagnostics;

namespace Remains.Persistence
{
    [Serializable]
    public sealed class CharacterTimeRecord : ExtensibleState
    {
        public int version=1;
        public double seconds;
        public bool completeHistory;
        public string trackedFrom;
        public void Validate()
        {
            if(version!=1 || !double.IsFinite(seconds) || seconds<0 || seconds>=TimeSpan.MaxValue.TotalSeconds ||
                !DateTimeOffset.TryParse(trackedFrom,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out _))
                throw new InvalidDataException("Повреждён учёт игрового времени персонажа.");
        }
        public static CharacterTimeRecord Read(string directory)
        {
            string path=Path.Combine(directory,"playtime.json");
            if(!File.Exists(path)) return null;
            return Parse(File.ReadAllText(path),path);
        }
        internal static CharacterTimeRecord Parse(string json,string path)
        {
            try
            {
                var value=StateJson.FromJson<CharacterTimeRecord>(json);
                if(value==null) throw new InvalidDataException("Пустой учёт игрового времени.");
                value.Validate();return value;
            }
            catch(InvalidDataException error) {throw new InvalidDataException("Некорректный учёт игрового времени: "+path+". "+error.Message,error);}
        }
        public static CharacterTimeRecord New(bool complete)=>new() {completeHistory=complete,trackedFrom=DateTimeOffset.UtcNow.ToString("O")};
    }

    // Local profile metadata. No host clocks or world snapshots can rewind it.
    public sealed class CharacterPlayTime
    {
        private readonly Func<double> now;
        private WriteContext active;
        private double baseline,lastCapturedElapsed;
        public CharacterPlayTime(Func<double> clock)=>now=clock??throw new ArgumentNullException(nameof(clock));

        // Only the game thread reads the session clock. Captures do no file I/O;
        // queued work owns its profile context even after Stop detaches it.
        public CapturedCheckpoint CaptureCheckpoint(string profileDirectory)
        {
            using var timing=TransitionTiming.Measure("playtime.capture",profileDirectory);
            if(string.IsNullOrWhiteSpace(profileDirectory)) throw new ArgumentException("Не указана папка профиля для учёта игрового времени.",nameof(profileDirectory));
            string directory=SavePath.Absolute(profileDirectory);
            string root=Path.GetPathRoot(directory);
            while(directory.Length>root.Length && (directory[directory.Length-1]==Path.DirectorySeparatorChar || directory[directory.Length-1]==Path.AltDirectorySeparatorChar))
                directory=directory.Substring(0,directory.Length-1);
            var comparison=Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            if(active!=null && !string.Equals(active.Directory,directory,comparison))
                throw new InvalidOperationException("Нельзя сменить профиль во время учёта игровой сессии: ожидается "+active.Directory+", получен "+directory+".");
            double current=now();
            if(!double.IsFinite(current)) throw new InvalidOperationException("Некорректные часы игрового времени: "+directory);
            if(active==null)
            {
                active=new WriteContext(directory);baseline=current;lastCapturedElapsed=0;
            }
            double elapsed=current-baseline;
            if(!double.IsFinite(elapsed) || elapsed<lastCapturedElapsed)
                throw new InvalidOperationException("Часы игрового времени пошли назад: "+directory);
            lastCapturedElapsed=elapsed;
            return new CapturedCheckpoint(active,elapsed);
        }
        public void Stop() {active=null;baseline=lastCapturedElapsed=0;}

        public sealed class CapturedCheckpoint
        {
            private readonly WriteContext context;
            private int consumed,persisted;
            private double loadMilliseconds,writeMilliseconds,persistMilliseconds;
            public string ProfileDirectory=>context.Directory;
            public double ElapsedSeconds {get;}
            public bool IsPersisted=>Volatile.Read(ref persisted)!=0;
            public double LoadMilliseconds=>Volatile.Read(ref loadMilliseconds);
            public double WriteMilliseconds=>Volatile.Read(ref writeMilliseconds);
            public double PersistMilliseconds=>Volatile.Read(ref persistMilliseconds);
            internal CapturedCheckpoint(WriteContext context,double elapsed) {this.context=context;ElapsedSeconds=elapsed;}
            // The attempt is single-use, including failure. A later capture retains
            // cumulative elapsed and may retry after the failed queue is drained.
            public void Persist()
            {
                if(Interlocked.Exchange(ref consumed,1)!=0)
                    throw new InvalidOperationException("Снимок игрового времени уже использован: "+ProfileDirectory);
                long started=Stopwatch.GetTimestamp();double load=0,write=0;bool success=false;
                try {context.Persist(ElapsedSeconds,out load,out write);success=true;}
                finally
                {
                    Volatile.Write(ref loadMilliseconds,load);Volatile.Write(ref writeMilliseconds,write);
                    Volatile.Write(ref persistMilliseconds,Milliseconds(started));
                    if(success) Volatile.Write(ref persisted,1);
                }
            }
        }

        // Accessed only by captured worker operations, serialized by the existing
        // FIFO and the per-file lease. No Unity clock or main-thread timing scope.
        internal sealed class WriteContext
        {
            public string Directory {get;}
            private readonly string path;
            private CharacterTimeRecord baseRecord;
            private string lastWrittenJson;
            private double lastSuccessfulElapsed;
            public WriteContext(string directory) {Directory=directory;path=Path.Combine(directory,"playtime.json");}
            public void Persist(double elapsed,out double loadMilliseconds,out double writeMilliseconds)
            {
                loadMilliseconds=writeMilliseconds=0;
                using var access=SaveAccess.Enter(path);
                if(elapsed<lastSuccessfulElapsed)
                    throw new InvalidOperationException("Нарушен порядок записи игрового времени: "+path);
                long reading=Stopwatch.GetTimestamp();CharacterTimeRecord initial;
                try
                {
                    string actual=File.Exists(path)?File.ReadAllText(path):null;
                    if(baseRecord!=null && actual!=lastWrittenJson)
                        throw new InvalidDataException("Учёт игрового времени изменён другой записью: "+path+". Исходный файл сохранён; автоматическое сложение сессий запрещено.");
                    initial=baseRecord??(actual==null?CharacterTimeRecord.New(false):CharacterTimeRecord.Parse(actual,path));
                }
                finally {loadMilliseconds=Milliseconds(reading);}
                var next=StateJson.Copy(initial);next.seconds+=elapsed;next.Validate();
                string json=StateJson.ToJson(next,true);long writing=Stopwatch.GetTimestamp();
                try {AtomicSaveFile.Write(path,json);}
                finally {writeMilliseconds=Milliseconds(writing);}
                // Keep the fixed base, rather than adding cumulative elapsed to the
                // previous checkpoint. Failed publication never advances this cache.
                baseRecord=initial;lastWrittenJson=json;lastSuccessfulElapsed=elapsed;
            }
        }
        private static double Milliseconds(long started)=>(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
    }
}
