using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    public static class WorldCatalogChecks
    {
        public static void Run()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/world-catalog",Guid.NewGuid().ToString("N")));
            var store=new SaveStore(root);var world=new WorldState();WorldHistory.BeginContinuation(world);
            world.extensions=new Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"catalog-check-data",new string('x',8192)}};
            for(int i=0;i<80;i++)
            {
                world.checkpointSequence=i;world.revision=i;
                var copy=new RecoverySnapshot(StateJson.ToJson(world));
                string path=Path.Combine(store.DirectoryPath,"Worlds",world.worldId,"Snapshots",copy.Id+".json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,copy.Json);
            }
            var current=store.SaveWorld(world);var branch=world.Copy();WorldHistory.BeginContinuation(branch);branch.revision=1;
            var alternative=store.SaveWorld(branch);store.SelectContinuation(current);
            var cold=store.Catalog.Read();
            string index=Path.Combine(store.DirectoryPath,"Library","world-catalog.json");
            byte[] indexBytes=File.ReadAllBytes(index);long indexTicks=File.GetLastWriteTimeUtc(index).Ticks;
            var warm=store.Catalog.Read();
            Require(!warm.readIndex && !warm.wroteIndex && File.GetLastWriteTimeUtc(index).Ticks==indexTicks && File.ReadAllBytes(index).SequenceEqual(indexBytes),
                "Неизменный список перечитывает/переписывает JSON индекс");
            // Returned DTOs cannot corrupt the private memory index.
            warm.files[0].summary.worldId="modified-by-caller";
            var detached=store.Catalog.Read();
            Require(detached.files.All(entry=>entry.summary?.worldId!= "modified-by-caller") && !detached.readIndex && !detached.wroteIndex,
                "Mod-facing DTO заменил внутренний индекс");
            warm=detached;
            // Saving rotates the archive (SaveStore.ArchiveKept): the 80 written copies are pruned to the newest ones.
            var archived=Directory.GetFiles(Path.Combine(store.DirectoryPath,"Worlds",world.worldId,"Snapshots"),"*.json");
            Require(archived.Length<=SaveStore.ArchiveKept && archived.Any(file=>Path.GetFileNameWithoutExtension(file)==current.Id) &&
                archived.Any(file=>Path.GetFileNameWithoutExtension(file)==alternative.Id),$"Ротация архива: копий {archived.Length}, текущая и ветка должны остаться.");
            Require(cold.readPayloads==cold.inspectedFiles && cold.inspectedFiles>=archived.Length,"Первичная индексация не прочитала архив");
            Require(warm.readPayloads==0,"Повторный список перечитывает полные снимки");
            Require(warm.entries.Count==1 && warm.entries[0].summary.snapshot==alternative.Id,"Предок не скрыт по ancestry");
            Require(warm.selected.snapshot==current.Id,"Описание выбранного мира заменено более поздним архивом");
            Require(warm.entries[0].Load(store.DirectoryPath).Json==alternative.Json,"Полный выбранный пакет изменился");
            var fork=world.Copy();WorldHistory.BeginContinuation(fork);fork.revision=900;
            var divergent=store.SaveWorld(fork);var changed=store.Catalog.Read();
            Require(changed.wroteIndex && changed.readPayloads==3 && changed.entries.Count==2 &&
                changed.entries.Any(e=>e.summary.snapshot==divergent.Id) && changed.entries.Any(e=>e.summary.snapshot==alternative.Id),
                "Изменённые файлы/независимые ветви не отражены или перечитан весь архив");
            string archive=Path.Combine(store.DirectoryPath,"Worlds",fork.worldId,"Snapshots",divergent.Id+".json");
            var held=changed.entries.Single(e=>e.summary.snapshot==divergent.Id);
            string preserved=File.ReadAllText(archive);File.WriteAllText(archive,preserved+" ");
            bool rejected=false;try {held.Load(store.DirectoryPath);} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Изменённый после открытия пакет не отклонён по хешу");File.WriteAllText(archive,preserved);
            string cache=Path.Combine(store.DirectoryPath,"Library","world-catalog.json");File.WriteAllText(cache,"broken-index");
            var rebuilt=store.Catalog.Read();
            Require(rebuilt.readIndex && rebuilt.wroteIndex && rebuilt.warnings.Any(w=>w.Contains("Индекс миров пересобран")) && rebuilt.readPayloads==rebuilt.inspectedFiles &&
                rebuilt.entries.Count==2,"Повреждённый индекс не восстановлен явно из исходников");
            Require(File.ReadAllText(archive)==preserved,"Восстановление индекса изменило снимок");
            string oversized=Path.Combine(store.DirectoryPath,"Worlds","invalid-number.json");
            File.WriteAllText(oversized,"{\"schema\":1,\"worldId\":\""+world.worldId+"\",\"revision\":999999999999999999999999999999999999999}");
            var invalid=store.Catalog.Read();
            Require(invalid.entries.Count==2 && invalid.warnings.Any(w=>w.Contains("invalid-number.json")),
                "Число вне диапазона остановило весь список или не показало диагностику");
            new WorldLibrary(store).Delete(world.worldId);
            Require(store.Catalog.Read().entries.Count==0 && store.SelectedSnapshot()==null &&
                File.Exists(oversized) &&
                Directory.EnumerateFiles(Path.Combine(store.DirectoryPath,"DeletedWorlds"),"*.json",SearchOption.AllDirectories).Count()>=archived.Length,
                "Удаление потеряло архивные снимки/ветви или оставило мир в списке");
            Debug.Log($"Индекс миров: PASS — {cold.inspectedFiles} файлов, повторное чтение payload=0, ancestry/ветви, 3 изменённых файла, строгий выбор, явная пересборка и обратимое удаление.");
        }
        public static void BuildAndBenchmark()
        {
            var args=Environment.GetCommandLineArgs();int option=Array.IndexOf(args,"--catalog-source");
            if(option<0 || option+1>=args.Length) throw new InvalidOperationException("Нужен явный --catalog-source для чтения проверяемого профиля.");
            string source=args[option+1];
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/library-real-profile-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));Directory.CreateDirectory(output);
            var watch=Stopwatch.StartNew();var catalog=new WorldCatalog(source,Path.Combine(output,"world-catalog.json"));
            var cold=catalog.Read();double initial=watch.Elapsed.TotalMilliseconds;watch.Restart();
            var warm=catalog.Read();double cached=watch.Elapsed.TotalMilliseconds;
            File.WriteAllText(Path.Combine(output,"report.json"),StateJson.ToJson(new Benchmark {source=source,
                inspectedFiles=cold.inspectedFiles,readPayloads=cold.readPayloads,cachedReads=warm.readPayloads,
                initialMs=initial,cachedMs=cached,heads=warm.entries.Count,warnings=warm.warnings},true));
            if(warm.readPayloads!=0) throw new InvalidOperationException("Повторный список прочитал тела снимков профиля.");
            Debug.Log($"Профиль прочитан без изменения исходников: файлов={cold.inspectedFiles}, продолжений={warm.entries.Count}, первое чтение={initial:F1} мс, повтор={cached:F1} мс, payload повтор={warm.readPayloads}.");
            PrototypeBuild.Build();
        }
        [Serializable] private sealed class Benchmark {public string source;public int inspectedFiles,readPayloads,cachedReads,heads;public double initialMs,cachedMs;public List<string> warnings;}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Индекс миров: "+reason);}
    }
}
