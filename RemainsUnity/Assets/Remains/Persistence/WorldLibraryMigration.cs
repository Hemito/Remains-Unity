using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Persistence
{
    // Copy-only migration. Legacy profiles and every source byte remain available.
    // The ledger prevents deleted imports from reappearing after a restart.
    public sealed class WorldLibraryMigration
    {
        [Serializable] private sealed class Ledger : ExtensibleState
        {public int version=1;public bool selectionInitialized;public List<SourceRecord> sources=new();}
        [Serializable] private sealed class SourceRecord : ExtensibleState
        {public string source,bytes,backup,snapshot,worldId,error;public long sourceLength=-1,sourceTicks=-1;}
        [Serializable] private sealed class Labels : ExtensibleState {public List<Label> entries=new();}
        [Serializable] private sealed class Label : ExtensibleState {public string id,name;}
        private readonly string profiles,worlds,initialProfile,ledgerPath,pendingPath;
        private readonly object gate=new();
        private bool complete;
        public int InspectedSources {get;private set;}
        public int HashedSources {get;private set;}
        public int LedgerPublications {get;private set;}
        private readonly List<string> warnings=new();
        public IReadOnlyList<string> Warnings {get {lock(gate) return warnings.ToArray();}}
        public WorldLibraryMigration(string profilesDirectory,string worldDirectory,string initialProfileDirectory)
        {
            profiles=SavePath.Absolute(profilesDirectory);worlds=SavePath.Absolute(worldDirectory);
            initialProfile=SavePath.Absolute(initialProfileDirectory);
            ledgerPath=Path.Combine(worlds,"LegacyMigration","ledger.json");
            pendingPath=Path.Combine(worlds,"LegacyMigration","pending.jsonl");
        }
        public void Ensure(CancellationToken cancellation=default)
        {
            // Always acquire the library before migration locks, including reentrant callers.
            using var access=SaveAccess.Enter(worlds,cancellation);
            lock(gate)
            {
                if(complete) return;
                // Two local game processes may initialize the same library together.
                using var mutex=new Mutex(false,"Remains.WorldLibraryMigration."+RecoverySnapshot.Fingerprint(worlds.ToUpperInvariant()));
                bool acquired=false;
                try
                {
                    try {acquired=WaitHandle.WaitAny(new[]{mutex,cancellation.WaitHandle})==0;}
                    catch(AbandonedMutexException) {acquired=true;Warn("Перенос библиотеки миров был прерван; проверяем журнал и точные копии перед продолжением.");}
                    if(!acquired) cancellation.ThrowIfCancellationRequested();
                    Migrate(cancellation);complete=true;
                }
                finally {if(acquired) mutex.ReleaseMutex();}
            }
        }
        private void Migrate(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var ledger=File.Exists(ledgerPath)?StateJson.FromJson<Ledger>(File.ReadAllText(ledgerPath)):new Ledger();
            if(ledger==null || ledger.version!=1 || ledger.sources==null)
                throw new InvalidDataException("Неподдерживаемый журнал переноса миров: "+ledgerPath);
            // One durable small journal record per source; consolidate the growing ledger once.
            // Replay precedes scanning, so a deleted imported snapshot is never recreated.
            var known=new Dictionary<(string source,string bytes),SourceRecord>();
            foreach(var record in ledger.sources) Index(known,record);
            bool dirty=ReplayPending(ledger,known);
            var latest=ledger.sources.GroupBy(record=>record.source,StringComparer.Ordinal)
                .ToDictionary(group=>group.Key,group=>group.Last(),StringComparer.Ordinal);
            int imported=0;string preferred=null;
            foreach(string profile in Directory.Exists(profiles)?Directory.EnumerateDirectories(profiles).OrderBy(path=>path,StringComparer.Ordinal):Enumerable.Empty<string>())
            {
                foreach(string source in Sources(profile))
                {
                    cancellation.ThrowIfCancellationRequested();
                    InspectedSources++;
                    var info=new FileInfo(source);long length=info.Length,ticks=info.LastWriteTimeUtc.Ticks;
                    latest.TryGetValue(source,out var stamped);
                    if(stamped!=null && stamped.sourceLength==length && stamped.sourceTicks==ticks)
                    {
                        if(stamped.error!=null) Warn(stamped.error);
                        if(IsInitialSelection(profile,source) && stamped.snapshot!=null) preferred=ArchivePath(stamped.worldId,stamped.snapshot);
                        continue;
                    }
                    byte[] bytes=File.ReadAllBytes(source);string hash=ByteHash(bytes);HashedSources++;
                    info.Refresh();bool stable=length==info.Length && ticks==info.LastWriteTimeUtc.Ticks;
                    known.TryGetValue((source,hash),out var prior);
                    if(prior!=null)
                    {
                        if(stable) {prior.sourceLength=length;prior.sourceTicks=ticks;latest[source]=prior;dirty=true;}
                        if(prior.error!=null) Warn(prior.error);
                        if(IsInitialSelection(profile,source) && prior.snapshot!=null) preferred=ArchivePath(prior.worldId,prior.snapshot);
                        continue;
                    }
                    string relative=Path.GetRelativePath(profile,source);
                    string backup=Path.Combine(worlds,"LegacyMigration","Backups",Path.GetFileName(profile),hash,relative);
                    CopyExact(backup,bytes);
                    var record=new SourceRecord {source=source,bytes=hash,backup=Path.GetRelativePath(worlds,backup),
                        sourceLength=stable?length:-1,sourceTicks=stable?ticks:-1};
                    // Recovery/Selections/DeletedWorlds remain backups, not resurrected live worlds.
                    if(IsLiveSnapshot(relative))
                    {
                        try
                        {
                            var summary=WorldFileSummary.Read(backup);
                            string target=ArchivePath(summary.worldId,summary.snapshot);
                            string text=File.ReadAllText(backup);
                            if(File.Exists(target))
                            {
                                if(File.ReadAllText(target)!=text) throw new InvalidDataException("Архив с этим ID содержит другой снимок: "+target);
                            }
                            else {CopyExact(target,bytes);imported++;}
                            record.snapshot=summary.snapshot;record.worldId=summary.worldId;
                            if(IsInitialSelection(profile,source)) preferred=target;
                        }
                        catch(Exception error) when(error is InvalidDataException || error is Newtonsoft.Json.JsonException || error is ArgumentException)
                        {record.error=$"Старый снимок не включён в библиотеку: source={source}, backup={backup}; {error.Message}";Warn(record.error);}
                    }
                    else if(relative=="world-names.json")
                    {
                        try
                        {
                            var labels=StateJson.FromJson<Labels>(File.ReadAllText(backup));
                            if(labels?.entries==null) throw new InvalidDataException("Отсутствует список имён миров.");
                            var library=new WorldLibrary(new SaveStore(profile,worlds));
                            foreach(var label in labels.entries)
                            {
                                if(label==null || !Guid.TryParseExact(label.id,"N",out _)) throw new InvalidDataException("Некорректный ID подписи мира.");
                                string diagnostic=library.ImportName(label.id,label.name,Path.GetFileName(profile));
                                if(diagnostic!=null) Warn(diagnostic);
                            }
                        }
                        catch(Exception error) when(error is InvalidDataException || error is ArgumentException)
                        {record.error=$"Старые подписи миров не перенесены полностью: source={source}, backup={backup}; {error.Message}";Warn(record.error);}
                    }
                    AppendPending(record); // Flush before processing another file, including cancellation.
                    ledger.sources.Add(record);known.Add((source,hash),record);latest[source]=record;dirty=true;
                }
            }
            if(!ledger.selectionInitialized)
            {
                string active=Path.Combine(worlds,"world.json");
                if(!File.Exists(active) && preferred!=null && File.Exists(preferred))
                {
                    // Preserve the explicitly initial profile's selection, never rank histories by time.
                    try {_=new RecoverySnapshot(File.ReadAllText(preferred));CopyExact(active,File.ReadAllBytes(preferred));}
                    catch(InvalidDataException error) {Warn($"Старый выбранный мир не прошёл полную проверку: файл={preferred}; {error.Message}. Выберите исправную копию в библиотеке.");}
                }
                else if(!File.Exists(active) && ledger.sources.Any(record=>record.snapshot!=null))
                    Warn("Старые миры перенесены. У первоначально выбранного персонажа нет выбранной исправной копии; выберите мир в общей библиотеке. Даты файлов не определяют выбор.");
                ledger.selectionInitialized=true;dirty=true;
            }
            if(dirty) Publish(ledger);
            // A crash after Publish and before this reset only replays identical records.
            if(File.Exists(pendingPath) && new FileInfo(pendingPath).Length>0)
                AtomicSaveFile.Write(pendingPath,string.Empty,uniqueTemporary:true);
            if(imported>0) Warn($"Миры профилей перенесены в общую библиотеку: снимков={imported}, каталог={worlds}. Исходники сохранены; точные резервные копии и журнал находятся в LegacyMigration.");
        }
        private bool IsInitialSelection(string profile,string source)=>
            string.Equals(profile,initialProfile,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(source)=="world.json";
        private string ArchivePath(string world,string snapshot)=>Path.Combine(worlds,"Worlds",world,"Snapshots",snapshot+".json");
        private void Publish(Ledger ledger)
        {AtomicSaveFile.Write(ledgerPath,StateJson.ToJson(ledger,true),uniqueTemporary:true);LedgerPublications++;}
        private static void Index(Dictionary<(string source,string bytes),SourceRecord> known,SourceRecord record)
        {
            ValidateRecord(record);
            known.TryAdd((record.source,record.bytes),record);
        }
        private static void ValidateRecord(SourceRecord record)
        {
            if(record==null || string.IsNullOrWhiteSpace(record.source) || string.IsNullOrWhiteSpace(record.bytes) || string.IsNullOrWhiteSpace(record.backup))
                throw new InvalidDataException("Некорректная запись журнала переноса миров.");
        }
        private bool ReplayPending(Ledger ledger,Dictionary<(string source,string bytes),SourceRecord> known)
        {
            if(!File.Exists(pendingPath)) return false;
            string text=File.ReadAllText(pendingPath);if(text.Length==0) return false;
            string[] lines=text.Split('\n');bool dirty=false;var valid=new StringBuilder();
            for(int index=0;index<lines.Length;index++)
            {
                string line=lines[index];if(line.Length==0 && index==lines.Length-1) continue;
                SourceRecord record;
                try
                {
                    record=StateJson.FromJson<SourceRecord>(line);
                    // Validate before deciding whether this record already exists in the ledger.
                    ValidateRecord(record);
                }
                catch(InvalidDataException error) when(error.InnerException is Newtonsoft.Json.JsonException && index==lines.Length-1 && !text.EndsWith("\n",StringComparison.Ordinal))
                {
                    Warn($"Незавершённая последняя запись переноса отброшена: файл={pendingPath}; {error.Message}. Её исходник будет проверен заново.");
                    break;
                }
                if(!known.ContainsKey((record.source,record.bytes)))
                {ledger.sources.Add(record);Index(known,record);dirty=true;}
                valid.Append(line).Append('\n');
            }
            if(valid.ToString()!=text) AtomicSaveFile.Write(pendingPath,valid.ToString(),uniqueTemporary:true);
            return dirty;
        }
        private void AppendPending(SourceRecord record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pendingPath));
            byte[] bytes=new UTF8Encoding(false).GetBytes(StateJson.ToJson(record)+"\n");
            using var stream=new FileStream(pendingPath,FileMode.Append,FileAccess.Write,FileShare.Read);
            stream.Write(bytes,0,bytes.Length);stream.Flush(true);
        }
        private void Warn(string warning) {if(!warnings.Contains(warning)) {warnings.Add(warning);Debug.LogWarning(warning);}}
        private static IEnumerable<string> Sources(string profile)
        {
            foreach(string name in new[]{"world.json","world.json.previous","world-names.json","world-names.json.previous"})
            {string path=Path.Combine(profile,name);if(File.Exists(path)) yield return path;}
            foreach(string name in new[]{"Worlds","Recovery","Selections","DeletedWorlds"})
            {
                string directory=Path.Combine(profile,name);
                if(Directory.Exists(directory)) foreach(string path in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).OrderBy(path=>path,StringComparer.Ordinal)) yield return path;
            }
        }
        private static bool IsLiveSnapshot(string relative)=>
            (relative=="world.json" || relative=="world.json.previous" || relative.StartsWith("Worlds"+Path.DirectorySeparatorChar,StringComparison.Ordinal)) &&
            (relative.EndsWith(".json",StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".json.previous",StringComparison.OrdinalIgnoreCase));
        private static string ByteHash(byte[] bytes)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void CopyExact(string destination,byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if(File.Exists(destination))
            {
                if(!File.ReadAllBytes(destination).SequenceEqual(bytes)) throw new InvalidDataException("Резервная копия или архив изменены: "+destination);
                return;
            }
            string temporary=destination+".importing-"+Guid.NewGuid().ToString("N");
            try {File.WriteAllBytes(temporary,bytes);File.Move(temporary,destination);}
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
        }
    }
}
