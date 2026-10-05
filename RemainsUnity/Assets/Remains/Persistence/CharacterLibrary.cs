using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Remains.Domain;
using Remains.Content;
using Remains.Serialization;

namespace Remains.Persistence
{
    public sealed class CharacterLibraryEntry
    {
        public string name,directory,error;
        public ActorState actor;
        public CharacterTimeRecord time;
    }
    public sealed class CharacterLibrary
    {
        private readonly string root,fixedDirectory;
        public CharacterLibrary(string profilesRoot,string singleDirectory=null)
        {root=SavePath.Absolute(profilesRoot);fixedDirectory=singleDirectory==null?null:SavePath.Absolute(singleDirectory);}
        public string ProfilePath(string name)=>fixedDirectory??Path.Combine(root,name);
        public List<CharacterLibraryEntry> Read(CancellationToken token=default)
        {
            var entries=new List<CharacterLibraryEntry>();
            IEnumerable<string> directories=fixedDirectory!=null?new[]{fixedDirectory}:Directory.Exists(root)?Directory.EnumerateDirectories(root):Array.Empty<string>();
            foreach(string directory in directories)
            {
                token.ThrowIfCancellationRequested();string path=Path.Combine(directory,"character.json");
                if(!File.Exists(path)) continue; // A world-only folder is not a character.
                var entry=new CharacterLibraryEntry {directory=directory,name=Path.GetFileName(directory)};
                try
                {
                    entry.actor=StateJson.FromJson<ActorState>(File.ReadAllText(path));
                    if(entry.actor==null || string.IsNullOrWhiteSpace(entry.actor.character) || !entry.actor.player ||
                        fixedDirectory==null && entry.actor.character!=entry.name)
                        throw new InvalidDataException("Имя/тип персонажа не соответствует папке профиля.");
                    entry.name=entry.actor.character;
                    PonyAppearance.ValidateSettings(entry.actor.appearance,entry.name);
                    if(entry.actor.appearance==null) _=PonyAppearance.Snapshot(entry.actor);
                    entry.time=CharacterTimeRecord.Read(directory);
                }
                catch(Exception error) when(error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException || error is ArgumentException || error is Newtonsoft.Json.JsonException)
                {entry.error=$"Профиль {path}: {error.Message}";entry.actor=null;}
                entries.Add(entry);
            }
            return entries.OrderBy(entry=>entry.name,StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        // The fully prepared actor is supplied by the main thread; this method only writes files.
        public CharacterLibraryEntry Create(ActorState actor)
        {
            CharacterName.ValidateNew(actor.character);PonyAppearance.ValidateSettings(actor.appearance,actor.character);
            if(!actor.player || actor.id!="player:"+actor.character || actor.appearance==null)
                throw new InvalidDataException("Новый персонаж не подготовлен.");
            string directory=ProfilePath(actor.character);
            // Windows profile identity is case-insensitive. Never replace a different spelling.
            if(fixedDirectory==null && Directory.Exists(root) && Directory.EnumerateDirectories(root).Any(path=>string.Equals(Path.GetFileName(path),actor.character,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Персонаж или папка с таким именем уже существует.");
            Directory.CreateDirectory(directory);
            string destination=Path.Combine(directory,"character.json"),temporary=destination+".creating-"+Guid.NewGuid().ToString("N");
            if(File.Exists(destination)) throw new InvalidOperationException("Персонаж с таким именем уже существует.");
            var time=CharacterTimeRecord.New(true);
            try
            {
                File.WriteAllText(temporary,StateJson.ToJson(actor,true));
                File.Move(temporary,destination); // No overwrite, including competing creators.
                AtomicSaveFile.Write(Path.Combine(directory,"playtime.json"),StateJson.ToJson(time,true));
            }
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
            return new CharacterLibraryEntry {name=actor.character,directory=directory,actor=actor.Copy(),time=time};
        }
    }
}
