using System;
using System.IO;
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

        public WorldState LoadWorld()
        {
            string path = Path.Combine(DirectoryPath, "world.json");
            if (!File.Exists(path)) return null;
            var world = JsonUtility.FromJson<WorldState>(File.ReadAllText(path));
            if (world == null || world.schema != 1) throw new InvalidDataException($"Неподдерживаемый сейв: {path}");
            return world;
        }

        public void SaveWorld(WorldState world)
        {
            if(!Guid.TryParseExact(world.worldId,"N",out _)) throw new InvalidDataException($"Некорректный ID мира: {world.worldId}.");
            string json=JsonUtility.ToJson(world,true);
            AtomicWrite(Path.Combine("Worlds",world.worldId+".json"),json);
            AtomicWrite("world.json",json);
        }
        public void SaveCharacter(ActorState actor) => AtomicWrite("character.json", JsonUtility.ToJson(actor, true));
        public ActorState LoadCharacter()
        {
            string path=Path.Combine(DirectoryPath,"character.json");
            return File.Exists(path)?JsonUtility.FromJson<ActorState>(File.ReadAllText(path)):null;
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
