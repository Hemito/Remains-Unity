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

        public void SaveWorld(WorldState world) => AtomicWrite("world.json", JsonUtility.ToJson(world, true));
        public void SaveCharacter(ActorState actor) => AtomicWrite("character.json", JsonUtility.ToJson(actor, true));

        private void AtomicWrite(string filename, string content)
        {
            string destination = Path.Combine(DirectoryPath, filename);
            string temporary = destination + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".previous");
            else File.Move(temporary, destination);
        }
    }
}
