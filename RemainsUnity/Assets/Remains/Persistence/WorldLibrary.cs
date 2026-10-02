using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Remains.Persistence
{
    // Local display names do not mutate canonical snapshots or their ancestry.
    public sealed class WorldLibrary
    {
        [Serializable] private sealed class Entry {public string id,name;}
        [Serializable] private sealed class Labels {public List<Entry> entries=new();}
        private readonly SaveStore store;
        private string LabelsPath=>Path.Combine(store.DirectoryPath,"world-names.json");
        public WorldLibrary(SaveStore store)=>this.store=store;
        private Labels Read()=>File.Exists(LabelsPath)?JsonUtility.FromJson<Labels>(File.ReadAllText(LabelsPath))??throw new InvalidDataException("Не удалось прочитать имена миров."):new Labels();
        public string Name(string id)=>Read().entries.Find(entry=>entry.id==id)?.name??"Мир "+id.Substring(0,Math.Min(8,id.Length));
        public void Rename(string id,string name)
        {
            name=(name??"").Trim();
            if(name.Length==0 || name.Length>48 || Array.Exists(name.ToCharArray(),char.IsControl))
                throw new ArgumentException("Название должно содержать от 1 до 48 символов без переносов строки.");
            var labels=Read();var entry=labels.entries.Find(value=>value.id==id);
            if(entry==null) {entry=new Entry {id=id};labels.entries.Add(entry);}
            entry.name=name;AtomicSaveFile.Write(LabelsPath,JsonUtility.ToJson(labels,true));
        }
        public void PrepareNewGame()
        {
            var selected=store.SelectedSnapshot();
            if(selected==null) return;
            store.SaveRecovery(selected);
            Archive(new[]{Path.Combine(store.DirectoryPath,"world.json")},"Selections");
        }
        public void Delete(string worldId)
        {
            var paths=new List<string>();
            string active=Path.Combine(store.DirectoryPath,"world.json");
            if(File.Exists(active) && new RecoverySnapshot(File.ReadAllText(active)).WorldId==worldId) paths.Add(active);
            string root=Path.Combine(store.DirectoryPath,"Worlds");
            if(Directory.Exists(root)) foreach(string path in Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories))
                if(new RecoverySnapshot(File.ReadAllText(path)).WorldId==worldId) paths.Add(path);
            if(paths.Count==0) throw new InvalidOperationException("Этот мир уже отсутствует в локальных сохранениях.");
            Archive(paths,"DeletedWorlds");
        }
        private void Archive(IEnumerable<string> paths,string category)
        {
            string destination=Path.Combine(store.DirectoryPath,category,Guid.NewGuid().ToString("N"));
            var moved=new List<(string source,string target)>();
            try
            {
                foreach(string source in paths)
                {
                    string relative=Path.GetRelativePath(store.DirectoryPath,source);
                    if(Path.IsPathRooted(relative) || relative.StartsWith("..")) throw new InvalidDataException("Файл сохранения вне каталога профиля.");
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
