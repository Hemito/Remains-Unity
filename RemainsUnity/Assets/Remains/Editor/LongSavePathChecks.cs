using System;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class LongSavePathChecks
    {
        public static void Run()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/long-save",Guid.NewGuid().ToString("N"),new string('a',90),new string('b',90)));
            if(root.Length<280) throw new InvalidOperationException("Путь проверки недостаточно длинный.");
            var store=new SaveStore(root);var world=new WorldState();
            var first=store.SaveWorld(world);world.revision++;var second=store.SaveWorld(world);
            if(store.SelectedSnapshot().Id!=second.Id || store.LoadWorld(false).revision!=world.revision ||
                !store.Continuations().Any(s=>s.Id==second.Id)) throw new InvalidOperationException("Длинный путь потерял сохранённый мир.");
            store.SelectContinuation(first);
            if(store.SelectedSnapshot().Id!=first.Id) throw new InvalidOperationException("Длинный путь не восстановил выбранный снимок.");
            string backup=store.PreserveRecoverySource();
            if(File.ReadAllText(backup)!=first.Json) throw new InvalidOperationException("Длинный путь повредил исходную копию.");
            var actor=new ActorState {id="long-path-player",character="Длинный путь",loadoutVersion=1,health=73};
            store.SaveCharacter(actor);actor.health=64;store.SaveCharacter(actor);
            if(store.LoadCharacter().health!=64 || !File.Exists(Path.Combine(store.DirectoryPath,"character.json.previous")))
                throw new InvalidOperationException("Длинный путь потерял персонажа или предыдущую версию.");
            Debug.Log($"Длинный путь сохранений проверен: длина корня={root.Length}, каталог={store.DirectoryPath}.");
        }
    }
}
