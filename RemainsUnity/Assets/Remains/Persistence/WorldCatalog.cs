using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    [Serializable]
    public sealed class WorldCatalogEntry
    {
        public string file;
        public long length,ticks;
        public WorldSummary summary;
        public string error;
        public RecoverySnapshot Load(string directory)
        {
            string path=WorldCatalog.Resolve(directory,file);
            var copy=new RecoverySnapshot(File.ReadAllText(path));
            var actual=WorldSummary.From(copy);
            if(StateJson.ToJson(actual)!=StateJson.ToJson(summary))
                throw new InvalidDataException($"Снимок изменился после чтения списка: файл={path}, ожидаемый ID={summary.snapshot}, фактический={copy.Id}. Обновите список.");
            return copy;
        }
    }
    public sealed class WorldCatalogResult
    {
        public List<WorldCatalogEntry> entries=new();
        public List<WorldCatalogEntry> files=new();
        public WorldSummary selected;
        public List<string> warnings=new();
        public int inspectedFiles,readPayloads;
        public bool readIndex,wroteIndex;
    }
    // Disposable index. Its contents never replace a save or authorize a grant.
    // File stamps only avoid repeated reads; selection verifies the payload hash.
    public sealed class WorldCatalog
    {
        [Serializable] private sealed class Cache {public int version=1;public string payload,hash;}
        [Serializable] private sealed class Records {public List<WorldCatalogEntry> entries=new();}
        private readonly string directory,cachePath;
        private sealed class MemoryIndex
        {public readonly object gate=new();public Records records;public long length,ticks;}
        private static readonly ConcurrentDictionary<string,MemoryIndex> Indexes=new(StringComparer.OrdinalIgnoreCase);
        private readonly MemoryIndex memory;
        private readonly object gate;
        private readonly Action<CancellationToken> prepare;
        private readonly Func<IEnumerable<string>> diagnostics;
        public WorldCatalog(string directory,string cachePath=null):this(directory,cachePath,null,null) {}
        public WorldCatalog(string directory,string cachePath,Action<CancellationToken> prepare,Func<IEnumerable<string>> diagnostics)
        {
            this.directory=SavePath.Absolute(directory);
            this.cachePath=SavePath.Absolute(cachePath??Path.Combine(this.directory,"Library","world-catalog.json"));
            memory=Indexes.GetOrAdd(this.cachePath,_=>new MemoryIndex());gate=memory.gate;
            this.prepare=prepare;
            this.diagnostics=diagnostics;
        }
        public WorldCatalogResult Read(CancellationToken cancellation=default)
        {
            prepare?.Invoke(cancellation);
            using var access=SaveAccess.Enter(directory,cancellation);
            lock(gate)
            {
                var result=new WorldCatalogResult();
                if(diagnostics!=null) result.warnings.AddRange(diagnostics());
                var prior=ReadCache(result,out bool validCache);bool changed=!validCache;
                var records=new Records();var known=prior.entries.ToDictionary(e=>e.file,StringComparer.OrdinalIgnoreCase);
                foreach(string path in Files())
                {
                    cancellation.ThrowIfCancellationRequested();result.inspectedFiles++;
                    string relative=Path.GetRelativePath(directory,path);var info=new FileInfo(path);
                    WorldCatalogEntry entry;
                    if(known.TryGetValue(relative,out var cached) && cached.length==info.Length && cached.ticks==info.LastWriteTimeUtc.Ticks) entry=CopyEntry(cached);
                    else
                    {
                        changed=true;result.readPayloads++;entry=new WorldCatalogEntry {file=relative,length=info.Length,ticks=info.LastWriteTimeUtc.Ticks};
                        try {entry.summary=WorldFileSummary.Read(path);}
                        catch(Exception error) when(error is InvalidDataException || error is JsonException || error is IOException || error is UnauthorizedAccessException)
                        {entry.error=$"Снимок пропущен: файл={path}; {error.Message}";}
                    }
                    records.entries.Add(entry);
                    if(entry.error!=null) result.warnings.Add(entry.error);
                    if(relative=="world.json") result.selected=entry.summary;
                }
                cancellation.ThrowIfCancellationRequested();
                result.files=records.entries;
                // Keep the full stat scan: external save changes remain visible immediately.
                // An unchanged index needs neither JSON parsing nor JSON publication.
                if(changed || records.entries.Count!=prior.entries.Count)
                {
                    string body=StateJson.ToJson(records);
                    AtomicSaveFile.Write(cachePath,StateJson.ToJson(new Cache {payload=body,hash=RecoverySnapshot.Fingerprint(body)}),uniqueTemporary:true,cancellation:cancellation);
                    Remember(records);result.wroteIndex=true;
                }
                // Prefer the immutable archive over a mutable selected/flat file.
                var copies=records.entries.Where(e=>e.summary!=null).GroupBy(e=>e.summary.snapshot)
                    .Select(g=>g.OrderByDescending(e=>e.file.Contains("Snapshots"+Path.DirectorySeparatorChar)).ThenBy(e=>e.file,StringComparer.Ordinal).First()).ToList();
                var heads=copies.GroupBy(e=>e.summary.historyVersion==1?e.summary.worldId+":"+e.summary.branchId:e.summary.snapshot)
                    .SelectMany(group=>
                    {
                        long latest=group.Max(entry=>entry.summary.sequence);
                        return group.Where(entry=>entry.summary.sequence==latest);
                    }).ToList();
                result.entries=heads.Where(e=>!heads.Any(other=>e.summary.snapshot!=other.summary.snapshot && other.summary.CompareTo(e.summary)==WorldRelation.Newer))
                    .OrderBy(e=>e.summary.worldId,StringComparer.Ordinal).ThenBy(e=>e.summary.snapshot,StringComparer.Ordinal).ToList();
                return result;
            }
        }
        private Records ReadCache(WorldCatalogResult result,out bool valid)
        {
            valid=false;var info=new FileInfo(cachePath);
            if(!info.Exists) {memory.records=null;return new Records();}
            if(memory.records!=null && memory.length==info.Length && memory.ticks==info.LastWriteTimeUtc.Ticks)
            {valid=true;return memory.records;}
            try
            {
                result.readIndex=true;
                var cache=StateJson.FromJson<Cache>(File.ReadAllText(cachePath));
                if(cache==null || cache.version!=1 || cache.payload==null || cache.hash!=RecoverySnapshot.Fingerprint(cache.payload))
                    throw new InvalidDataException("Неверная версия или контрольная сумма индекса.");
                var records=StateJson.FromJson<Records>(cache.payload);
                if(records?.entries==null) throw new InvalidDataException("В индексе нет записей.");
                var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in records.entries)
                {
                    if(entry==null) throw new InvalidDataException("Пустая запись индекса.");
                    Resolve(directory,entry.file);
                    if(!files.Add(entry.file) || entry.length<0 || entry.ticks<0 || entry.summary==null && entry.error==null)
                        throw new InvalidDataException("Неверная/повторная запись индекса.");
                    if(entry.summary!=null) WorldFileSummary.Validate(entry.summary,entry.file);
                }
                memory.records=records;memory.length=info.Length;memory.ticks=info.LastWriteTimeUtc.Ticks;valid=true;
                return records;
            }
            catch(Exception error) when(error is InvalidDataException || error is IOException || error is ArgumentException)
            {
                memory.records=null;
                result.warnings.Add($"Индекс миров пересобран из сохранений: файл={cachePath}; {error.Message}");return new Records();
            }
        }
        private void Remember(Records records)
        {
            // Results are mutable mod-facing DTOs. Never expose the private cached index.
            memory.records=new Records {entries=records.entries.Select(CopyEntry).ToList()};
            var info=new FileInfo(cachePath);memory.length=info.Length;memory.ticks=info.LastWriteTimeUtc.Ticks;
        }
        private static WorldCatalogEntry CopyEntry(WorldCatalogEntry entry)
        {
            var source=entry.summary;
            var summary=source==null?null:new WorldSummary {snapshot=source.snapshot,worldId=source.worldId,
                branchId=source.branchId,historyVersion=source.historyVersion,sequence=source.sequence,revision=source.revision,
                ancestors=source.ancestors?.Select(ancestor=>new WorldAncestor {branchId=ancestor.branchId,sequence=ancestor.sequence,
                    extensions=ancestor.extensions?.ToDictionary(field=>field.Key,field=>field.Value?.DeepClone())}).ToList()};
            return new WorldCatalogEntry {file=entry.file,length=entry.length,ticks=entry.ticks,summary=summary,error=entry.error};
        }
        private IEnumerable<string> Files()
        {
            string active=Path.Combine(directory,"world.json");if(File.Exists(active)) yield return active;
            string root=Path.Combine(directory,"Worlds");
            if(Directory.Exists(root)) foreach(string path in Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories)) yield return path;
        }
        public static string Resolve(string directory,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Некорректный относительный путь снимка.");
            string full=Path.GetFullPath(Path.Combine(directory,relative));
            string checkedRelative=Path.GetRelativePath(directory,full);
            if(checkedRelative==".." || checkedRelative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal) || Path.IsPathRooted(checkedRelative) ||
                checkedRelative!="world.json" && !checkedRelative.StartsWith("Worlds"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Снимок вне каталога миров: "+relative);
            return SavePath.Absolute(full);
        }
    }
}
