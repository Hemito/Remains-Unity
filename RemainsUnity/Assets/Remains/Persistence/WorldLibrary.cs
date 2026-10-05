using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Remains.Persistence
{
    // Local display names do not mutate canonical snapshots or their ancestry.
    public sealed class WorldLibrary
    {
        [Serializable] private sealed class Entry : Remains.Serialization.ExtensibleState {public string id,name;public List<Alias> legacyNames=new();}
        [Serializable] private sealed class Alias : Remains.Serialization.ExtensibleState {public string profile,name;}
        [Serializable] private sealed class Labels : Remains.Serialization.ExtensibleState {public List<Entry> entries=new();}
        private readonly SaveStore store;
        private string LabelsPath=>Path.Combine(store.WorldDirectoryPath,"world-names.json");
        public WorldLibrary(SaveStore store)=>this.store=store;
        private Labels Read()
        {
            store.EnsureWorldLibrary();
            return File.Exists(LabelsPath)?JsonUtility.FromJson<Labels>(File.ReadAllText(LabelsPath))??throw new InvalidDataException("Не удалось прочитать имена миров."):new Labels();
        }
        public string Name(string id)=>Read().entries.Find(entry=>entry.id==id)?.name??"Мир "+id.Substring(0,Math.Min(8,id.Length));
        public Dictionary<string,string> Names()=>Read().entries.ToDictionary(entry=>entry.id,entry=>entry.name,StringComparer.Ordinal);
        public static string DisplayName(Dictionary<string,string> names,string id)=>names.TryGetValue(id,out string name)?name:"Мир "+id.Substring(0,Math.Min(8,id.Length));
        public void Rename(string id,string name)
        {
            name=(name??"").Trim();
            if(name.Length==0 || name.Length>48 || Array.Exists(name.ToCharArray(),char.IsControl))
                throw new ArgumentException("Название должно содержать от 1 до 48 символов без переносов строки.");
            var labels=Read();var entry=labels.entries.Find(value=>value.id==id);
            if(entry==null) {entry=new Entry {id=id};labels.entries.Add(entry);}
            entry.name=name;AtomicSaveFile.Write(LabelsPath,JsonUtility.ToJson(labels,true));
        }
        // Labels are local metadata, never a reason to select or merge a snapshot.
        internal string ImportName(string id,string name,string profile)
        {
            if(string.IsNullOrWhiteSpace(name) || name.Length>48 || Array.Exists(name.ToCharArray(),char.IsControl))
                throw new ArgumentException($"Некорректная старая подпись: профиль={profile}, мир={id}.");
            var labels=Read();var entry=labels.entries.Find(value=>value.id==id);
            if(entry==null) {entry=new Entry {id=id,name=name};labels.entries.Add(entry);}
            entry.legacyNames??=new();
            if(!entry.legacyNames.Any(value=>value.profile==profile && value.name==name)) entry.legacyNames.Add(new Alias {profile=profile,name=name});
            AtomicSaveFile.Write(LabelsPath,JsonUtility.ToJson(labels,true));
            return entry.name==name?null:$"У мира сохранены разные локальные имена: мир={id}, текущая подпись={entry.name}, профиль={profile}, старая подпись={name}. Вариант сохранён в world-names.json и резервной копии; истории мира не объединены.";
        }
        public void PrepareNewGame()
        {
            RecoverySnapshot selected;
            try {selected=store.SelectedSnapshot();}
            catch(InvalidDataException error)
            {
                Archive(new[]{Path.Combine(store.WorldDirectoryPath,"world.json")},"Recovery");
                Debug.LogWarning($"Перед новой игрой повреждённый выбранный файл перенесён в {store.WorldDirectoryPath}/Recovery. {error.Message}");
                return;
            }
            if(selected==null) return;
            store.SaveRecovery(selected);
            Archive(new[]{Path.Combine(store.WorldDirectoryPath,"world.json")},"Selections");
        }
        public void Delete(string worldId)
        {
            var catalog=store.Catalog.Read();
            foreach(string warning in catalog.warnings) Debug.LogWarning(warning);
            var paths=catalog.files.Where(entry=>entry.summary?.worldId==worldId)
                .Select(entry=>WorldCatalog.Resolve(store.WorldDirectoryPath,entry.file)).ToList();
            if(paths.Count==0) throw new InvalidOperationException("Этот мир уже отсутствует в локальных сохранениях.");
            Archive(paths,"DeletedWorlds");
        }
        private void Archive(IEnumerable<string> paths,string category)
        {
            string destination=Path.Combine(store.WorldDirectoryPath,category,Guid.NewGuid().ToString("N"));
            var moved=new List<(string source,string target)>();
            try
            {
                foreach(string source in paths)
                {
                    string relative=Path.GetRelativePath(store.WorldDirectoryPath,source);
                    if(Path.IsPathRooted(relative) || relative.StartsWith("..")) throw new InvalidDataException("Файл сохранения вне библиотеки миров.");
                    string target=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Move(source,target);moved.Add((source,target));
                }
            }
            catch
            {
                for(int i=moved.Count-1;i>=0;i--) File.Move(moved[i].target,moved[i].source);
                throw;
            }
        }
    }
}
