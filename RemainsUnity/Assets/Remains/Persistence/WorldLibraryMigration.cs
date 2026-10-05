using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
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
        {public string source,bytes,backup,snapshot,worldId,error;}
        [Serializable] private sealed class Labels : ExtensibleState {public List<Label> entries=new();}
        [Serializable] private sealed class Label : ExtensibleState {public string id,name;}
        private readonly string profiles,worlds,initialProfile,ledgerPath;
        private readonly object gate=new();
        private bool complete;
        private readonly List<string> warnings=new();
        public IReadOnlyList<string> Warnings {get {lock(gate) return warnings.ToArray();}}
        public WorldLibraryMigration(string profilesDirectory,string worldDirectory,string initialProfileDirectory)
        {
            profiles=SavePath.Absolute(profilesDirectory);worlds=SavePath.Absolute(worldDirectory);
            initialProfile=SavePath.Absolute(initialProfileDirectory);
            ledgerPath=Path.Combine(worlds,"LegacyMigration","ledger.json");
        }
        public void Ensure(CancellationToken cancellation=default)
        {
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
            int imported=0;string preferred=null;
            foreach(string profile in Directory.Exists(profiles)?Directory.EnumerateDirectories(profiles).OrderBy(path=>path,StringComparer.Ordinal):Enumerable.Empty<string>())
            {
                foreach(string source in Sources(profile))
                {
                    cancellation.ThrowIfCancellationRequested();
                    byte[] bytes=File.ReadAllBytes(source);string hash=ByteHash(bytes);
                    var prior=ledger.sources.Find(record=>record.source==source && record.bytes==hash);
                    if(prior!=null)
                    {
                        if(prior.error!=null) Warn(prior.error);
                        if(IsInitialSelection(profile,source) && prior.snapshot!=null) preferred=ArchivePath(prior.worldId,prior.snapshot);
                        continue;
                    }
                    string relative=Path.GetRelativePath(profile,source);
                    string backup=Path.Combine(worlds,"LegacyMigration","Backups",Path.GetFileName(profile),hash,relative);
                    CopyExact(backup,bytes);
                    var record=new SourceRecord {source=source,bytes=hash,backup=Path.GetRelativePath(worlds,backup)};
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
                    ledger.sources.Add(record);Publish(ledger);
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
                ledger.selectionInitialized=true;Publish(ledger);
            }
            if(imported>0) Warn($"Миры профилей перенесены в общую библиотеку: снимков={imported}, каталог={worlds}. Исходники сохранены; точные резервные копии и журнал находятся в LegacyMigration.");
        }
        private bool IsInitialSelection(string profile,string source)=>
            string.Equals(profile,initialProfile,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(source)=="world.json";
        private string ArchivePath(string world,string snapshot)=>Path.Combine(worlds,"Worlds",world,"Snapshots",snapshot+".json");
        private void Publish(Ledger ledger)=>AtomicSaveFile.Write(ledgerPath,StateJson.ToJson(ledger,true),uniqueTemporary:true);
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
