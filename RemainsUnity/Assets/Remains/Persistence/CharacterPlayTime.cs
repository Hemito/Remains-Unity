using System;
using System.IO;
using Remains.Serialization;

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
            var value=StateJson.FromJson<CharacterTimeRecord>(File.ReadAllText(path));
            if(value==null) throw new InvalidDataException("Пустой учёт игрового времени: "+path);
            value.Validate();return value;
        }
        public static CharacterTimeRecord New(bool complete)=>new() {completeHistory=complete,trackedFrom=DateTimeOffset.UtcNow.ToString("O")};
    }

    // Local profile metadata. No host clocks or world snapshots can rewind it.
    public sealed class CharacterPlayTime
    {
        private readonly Func<double> now;
        private string directory;
        private CharacterTimeRecord record;
        private double baseline;
        public CharacterPlayTime(Func<double> clock)=>now=clock;
        public void Checkpoint(string profileDirectory)
        {
            if(directory!=profileDirectory)
            {
                if(directory!=null) throw new InvalidOperationException("Нельзя сменить профиль во время учёта игровой сессии.");
                record=CharacterTimeRecord.Read(profileDirectory)??CharacterTimeRecord.New(false);
                directory=profileDirectory;baseline=now();
            }
            double current=now(),elapsed=current-baseline;
            if(!double.IsFinite(elapsed) || elapsed<0) throw new InvalidOperationException("Часы игрового времени пошли назад.");
            var next=StateJson.Copy(record);next.seconds+=elapsed;next.Validate();
            AtomicSaveFile.Write(Path.Combine(directory,"playtime.json"),StateJson.ToJson(next,true));
            record=next;baseline=current;
        }
        public void Stop() {directory=null;record=null;}
    }
}
