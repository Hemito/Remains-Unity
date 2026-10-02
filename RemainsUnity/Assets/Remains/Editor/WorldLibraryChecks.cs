using System;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class WorldLibraryChecks
    {
        public static void Run()
        {
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/world-library",Guid.NewGuid().ToString("N")));
            var store=new SaveStore(directory);var library=new WorldLibrary(store);
            var first=new WorldState();var copy=store.SaveWorld(first);
            File.WriteAllText(Path.Combine(directory,"character.json"),"character-sentinel");
            library.Rename(first.worldId,"Моя первая база");
            Require(new WorldLibrary(store).Name(first.worldId)=="Моя первая база" && store.SelectedSnapshot().Id==copy.Id,"Переименование изменило мир или не сохранилось");
            library.PrepareNewGame();
            Require(store.LoadWorld(false)==null && store.Continuations().Any(value=>value.WorldId==first.worldId),"Новая игра не сбросила выбор или потеряла прежний мир");
            var second=new WorldState();store.SaveWorld(second);
            Require(second.worldId!=first.worldId && store.Continuations().Any(value=>value.WorldId==first.worldId),"Новый мир заменил старый");
            library.Delete(first.worldId);
            Require(store.Continuations().All(value=>value.WorldId!=first.worldId) && store.SelectedSnapshot().WorldId==second.worldId,"Удаление затронуло другой выбранный мир");
            library.Delete(second.worldId);
            Require(store.SelectedSnapshot()==null && store.Continuations().Count==0,"Удалённый мир остался выбранным или в списке");
            Require(File.ReadAllText(Path.Combine(directory,"character.json"))=="character-sentinel","Изменён персонаж");
            Require(Directory.EnumerateFiles(Path.Combine(directory,"DeletedWorlds"),"*.json",SearchOption.AllDirectories).Any(),"Нет резервной копии удалённого мира");
            Debug.Log("Управление мирами: PASS — имя, новый мир, удаление выбранного/другого мира, сохранность персонажа и резервных копий.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
