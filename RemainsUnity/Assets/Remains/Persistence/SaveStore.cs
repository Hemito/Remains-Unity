using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Persistence
{
    public sealed class SaveStore
    {
        public string DirectoryPath { get; }
        public SaveStore(string directory)
        {
            DirectoryPath = directory;
            Directory.CreateDirectory(directory);
        }

        public WorldState LoadWorld(bool prepareForPlay=true)
        {
            string path = Path.Combine(DirectoryPath, "world.json");
            if (!File.Exists(path)) return null;
            var world = JsonUtility.FromJson<WorldState>(File.ReadAllText(path));
            if (world == null || world.schema != 1) throw new InvalidDataException($"Неподдерживаемый сейв: {path}");
            if(!prepareForPlay) return world;
            if(world.actors.Any(a=>a.player && a.loadoutVersion==0))
            {
                string backup=PreserveSource("world.json");
                foreach(var actor in world.actors.Where(a=>a.player && a.loadoutVersion==0)) UpgradeLoadout(actor,backup);
                world.revision++;
            }
            foreach(var actor in world.actors) actor.inventoryUses ??= new List<InventoryUse>();
            return world;
        }

        public void SaveWorld(WorldState world)
        {
            SaveRecovery(new RecoverySnapshot(JsonUtility.ToJson(world,true)));
        }

        public void SaveRecovery(RecoverySnapshot snapshot)
        {
            // Keep every distinct payload, including branches with identical revision numbers.
            string archive=Path.Combine("Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
            string path=Path.Combine(DirectoryPath,archive);
            if(File.Exists(path))
            {
                if(File.ReadAllText(path)!=snapshot.Json)
                    throw new InvalidDataException($"Повреждён архив снимка: {path}.");
            }
            else AtomicWrite(archive,snapshot.Json);
            PreserveExistingWorld("world.json");
            PreserveExistingWorld(Path.Combine("Worlds",snapshot.WorldId+".json"));
            AtomicWrite(Path.Combine("Worlds",snapshot.WorldId+".json"),snapshot.Json);
            AtomicWrite("world.json",snapshot.Json);
        }

        private void PreserveExistingWorld(string filename)
        {
            string path=Path.Combine(DirectoryPath,filename);
            if(!File.Exists(path)) return;
            var previous=new RecoverySnapshot(File.ReadAllText(path));
            string archive=Path.Combine("Worlds",previous.WorldId,"Snapshots",previous.Id+".json");
            if(!File.Exists(Path.Combine(DirectoryPath,archive))) AtomicWrite(archive,previous.Json);
        }
        public void SaveCharacter(ActorState actor) => AtomicWrite("character.json", JsonUtility.ToJson(actor, true));
        public ActorState LoadCharacter()
        {
            string path=Path.Combine(DirectoryPath,"character.json");
            if(!File.Exists(path)) return null;
            var actor=JsonUtility.FromJson<ActorState>(File.ReadAllText(path));
            if(actor.loadoutVersion==0) UpgradeLoadout(actor,PreserveSource("character.json"));
            return actor;
        }

        public string PreserveRecoverySource()
            => PreserveSource("world.json");

        private static void UpgradeLoadout(ActorState actor,string backup)
        {
            WeaponAmmunition.Initialize(actor,true);
            actor.revision++;
            Debug.LogWarning($"Миграция боезапаса v0→v1: actor={actor.id}, добавлено стартовое оснащение прототипа (магазины и резерв); здоровье и существующие предметы сохранены. Исходник: {backup}.");
        }

        private string PreserveSource(string filename)
        {
            string source=Path.Combine(DirectoryPath,filename);
            if(!File.Exists(source)) return null;
            string directory=Path.Combine(DirectoryPath,"Recovery");Directory.CreateDirectory(directory);
            string destination=Path.Combine(directory,Path.GetFileNameWithoutExtension(filename)+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json");
            File.Copy(source,destination);
            return destination;
        }

        private void AtomicWrite(string filename, string content)
        {
            string destination = Path.Combine(DirectoryPath, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".previous");
            else File.Move(temporary, destination);
        }
    }
}
