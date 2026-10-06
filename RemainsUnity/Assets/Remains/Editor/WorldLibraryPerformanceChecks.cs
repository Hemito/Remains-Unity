using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class WorldLibraryPerformanceChecks
    {
        public static void Run()=>RunAt(Path.Combine(Path.GetTempPath(),"remains-library-performance-"+Guid.NewGuid().ToString("N")),160);
        public static void RunAt(string root,int sourceCount=160)
        {
            if(sourceCount<6) throw new ArgumentOutOfRangeException(nameof(sourceCount));
            var libraries=new SaveLibraries(root,"Alpha");var a=libraries.ForCharacter("Alpha");var b=libraries.ForCharacter("Beta");
            var original=new WorldState();WorldHistory.BeginContinuation(original);
            original.extensions=new Dictionary<string,JToken> {{"performance-mod",new JObject {{"value","unknown source data"}}}};
            var left=original.Copy();WorldHistory.BeginContinuation(left);left.revision=8;
            var right=original.Copy();WorldHistory.BeginContinuation(right);right.revision=8;
            var snapshots=new[]{new RecoverySnapshot(StateJson.ToJson(original)),new RecoverySnapshot(StateJson.ToJson(left)),new RecoverySnapshot(StateJson.ToJson(right))};
            File.WriteAllText(Path.Combine(a.CharacterDirectoryPath,"world.json"),snapshots[1].Json);
            File.WriteAllText(Path.Combine(b.CharacterDirectoryPath,"world.json"),snapshots[2].Json);
            string sources=Path.Combine(a.CharacterDirectoryPath,"Worlds");Directory.CreateDirectory(sources);
            for(int i=0;i<sourceCount-2;i++) File.WriteAllText(Path.Combine(sources,i+".json"),snapshots[i%3].Json);
            libraries.Migration.Ensure();
            Require(libraries.Migration.InspectedSources==sourceCount && libraries.Migration.HashedSources==sourceCount && libraries.Migration.LedgerPublications==1,
                "Первый перенос не линейный: полный ledger должен публиковаться один раз");
            string ledgerPath=Path.Combine(libraries.WorldsDirectory,"LegacyMigration","ledger.json");
            string pendingPath=Path.Combine(libraries.WorldsDirectory,"LegacyMigration","pending.jsonl");
            var ledger=JObject.Parse(File.ReadAllText(ledgerPath));var records=(JArray)ledger["sources"];
            Require(records.Count==sourceCount && new FileInfo(pendingPath).Length==0,"Потеря/дублирование записей или pending не consolidated");
            foreach(JObject record in records)
                Require(File.ReadAllBytes((string)record["source"]).SequenceEqual(File.ReadAllBytes(Path.Combine(libraries.WorldsDirectory,(string)record["backup"]))),
                    "Резервная копия не совпадает побайтно");
            Require(Directory.EnumerateFiles(Path.Combine(libraries.WorldsDirectory,"Worlds"),"*.json",SearchOption.AllDirectories).Count()==3,
                "Идентичные источники не dedup или потеряна ветвь");
            Require(a.SelectedSnapshot().Id==snapshots[1].Id,"Выбор Alpha заменён ветвью Beta");
            var restart=new SaveLibraries(root,"Beta");restart.Migration.Ensure();
            Require(restart.Migration.HashedSources==0 && restart.Migration.LedgerPublications==0 && restart.ForCharacter("Beta").SelectedSnapshot().Id==snapshots[1].Id,
                "Неизменные source заново хэшируются/ledger переписывается или выбор зависит от персонажа");
            // A legacy v1 ledger receives optional stamps once; unknown ledger/record data survives.
            ledger["unknown-ledger"]=new JObject {{"nested",new JArray(1,"данные мода")}};
            ((JObject)records[0])["unknown-record"]=new JObject {{"unchanged",true}};
            foreach(JObject record in records) {record.Remove("sourceLength");record.Remove("sourceTicks");}
            File.WriteAllText(ledgerPath,ledger.ToString());
            var upgrade=new SaveLibraries(root,"Alpha");upgrade.Migration.Ensure();
            Require(upgrade.Migration.HashedSources==sourceCount && upgrade.Migration.LedgerPublications==1,"Legacy stamp upgrade не consolidated");
            ledger=JObject.Parse(File.ReadAllText(ledgerPath));records=(JArray)ledger["sources"];
            Require(ledger["unknown-ledger"]!=null && records[0]["unknown-record"]!=null,"Неизвестные ledger extension потеряны");
            // An ordinary source update is hashed and retained as another exact-byte version.
            string changedSource=(string)records[0]["source"];
            File.AppendAllText(changedSource," ");
            var updated=new SaveLibraries(root,"Alpha");updated.Migration.Ensure();
            Require(updated.Migration.HashedSources==1 && updated.Migration.LedgerPublications==1,"Изменённый source не проверен отдельно");
            ledger=JObject.Parse(File.ReadAllText(ledgerPath));records=(JArray)ledger["sources"];
            Require(records.Count==sourceCount+1,"Предыдущая byte-version source потеряна");
            // Simulate process interruption after durable journal append, before ledger checkpoint.
            // Deletion is a real production operation; replay must not recreate its archives.
            new WorldLibrary(a).Delete(original.worldId);
            var pending=(JObject)records.Last.DeepClone();pending["unknown-journal"]=new JObject {{"retained",7}};records.Remove(records.Last);
            File.WriteAllText(ledgerPath,ledger.ToString());
            File.WriteAllText(pendingPath,pending.ToString(Newtonsoft.Json.Formatting.None)+"\n{interrupted");
            var replay=new SaveLibraries(root,"Beta");replay.Migration.Ensure();
            var replayLedger=JObject.Parse(File.ReadAllText(ledgerPath));
            Require(((JArray)replayLedger["sources"]).Count==sourceCount+1 && ((JArray)replayLedger["sources"]).Last["unknown-journal"]!=null,
                "Durable journal/unknown extension не восстановлен");
            Require(replay.ForCharacter("Beta").Catalog.Read().entries.Count==0 && replay.ForCharacter("Beta").SelectedSnapshot()==null &&
                replay.Migration.Warnings.Any(w=>w.Contains("Незавершённая последняя запись")),"Replay resurrected удалённый мир или скрывает partial-tail diagnostic");
            // A corrupt completed journal line is not a partial-write fallback.
            File.WriteAllText(pendingPath,"broken\n");
            bool refused=false;try {new SaveLibraries(root,"Alpha").Migration.Ensure();} catch(InvalidDataException) {refused=true;}
            Require(refused && File.ReadAllText(pendingPath)=="broken\n","Повреждённый полный journal принят/заменён молча");
            File.WriteAllText(pendingPath,"{}");refused=false;
            try {new SaveLibraries(root,"Alpha").Migration.Ensure();} catch(InvalidDataException) {refused=true;}
            Require(refused && File.ReadAllText(pendingPath)=="{}","Полная JSON запись с отсутствующими полями принята за truncated-tail");
            Debug.Log($"Библиотека: PASS — sources={sourceCount}, один ledger checkpoint, zero-hash restart, changed-source hash, точные backup/ветви, extension preservation, durable replay без resurrection, partial-tail warning и fail-closed corruption.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Библиотека миров: "+reason);}
    }
}
