using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class RecoveryCaptureChecks
    {
        public static void Run()=>RunAt(Path.Combine(Path.GetTempPath(),"remains-recovery-capture-"+Guid.NewGuid().ToString("N")));
        public static void RunAt(string root)
        {
            var store=new SaveStore(root);root=store.WorldDirectoryPath;var world=new WorldState();WorldHistory.BeginContinuation(world);
            world.extensions=new Dictionary<string,JToken> {{"capture-mod",new JObject {{"data",new string('x',128000)},{"precise",decimal.MaxValue}}}};
            var actor=new ActorState {id="player:capture",character="capture",player=true,x=7.2f,y=10.4f};
            actor.inventory.Add(new ItemStack {id="item:capture",definition="p10",count=4,
                extensions=new Dictionary<string,JToken> {{"item-mod",new JObject {{"nested",new JArray(7,"данные")}}}}});
            actor.inventoryAliases["item:prior"]="item:capture";world.actors.Add(actor);
            world.containers.Add(new ContainerState {id="container:capture",items=new List<ItemStack> {new ItemStack {id="source:capture",definition="caps",count=3}}});
            world.levelTimeVersion=1;world.levelTimes.Add(new LevelSimulationTime {level="factory:0",elapsedSeconds=13.5});
            world.completedTransactions.Add("transaction:already-completed");
            var direct=RecoverySnapshot.FromWorld(world);var strict=new RecoverySnapshot(StateJson.ToJson(world));
            Require(direct.Json==strict.Json && direct.Id==strict.Id && direct.WorldId==world.worldId && direct.Revision==world.revision,"FromWorld изменил bytes/ID/metadata");
            CheckLiveOwnership(direct);
            actor.inventory[0].count=2;actor.x=8;
            Require(direct.CreateLiveWorld().actors[0].inventory[0].count==4 && direct.CreateLiveWorld().actors[0].x==7.2f,"Live mutation изменила immutable payload");
            long sequence=world.checkpointSequence;
            var captured=store.CaptureWorldSnapshot(world);
            Require(world.checkpointSequence==sequence+1 && world.weaponContent!=null && captured.Json==StateJson.ToJson(world) && !File.Exists(Path.Combine(root,"world.json")),
                "Capture потерял Record/sequence semantics или выполняет disk write");
            var restored=captured.CreateLiveWorld();
            Require(restored.extensions["capture-mod"]["precise"].Value<decimal>()==decimal.MaxValue &&
                restored.actors[0].inventory[0].extensions["item-mod"]["nested"][1].Value<string>()=="данные" &&
                restored.actors[0].inventoryAliases["item:prior"]=="item:capture" && restored.completedTransactions.Contains("transaction:already-completed"),
                "Capture потерял неизвестные fields/decimal precision/alias/receipt");
            store.SaveRecovery(captured);int hits=store.ReusedWorldPayloads;
            var next=store.CaptureWorldSnapshot(world);store.SaveRecovery(next);
            Require(store.ReusedWorldPayloads==hits+2,"Own written active/flat payload дважды десериализуется снова");
            string active=Path.Combine(root,"world.json"),flat=Path.Combine(root,"Worlds",world.worldId+".json");
            var external=next.CreateLiveWorld();external.actors[0].x=9.25f;external.extensions["external-change"]="сохранить";
            var externalCopy=RecoverySnapshot.FromWorld(external);File.WriteAllText(active,externalCopy.Json);hits=store.ReusedWorldPayloads;
            store.SaveRecovery(next);
            Require(store.ReusedWorldPayloads==hits+1 && File.ReadAllText(Path.Combine(root,"Worlds",world.worldId,"Snapshots",externalCopy.Id+".json"))==externalCopy.Json,
                "External valid change прошёл stale cache или потерян предыдущий payload");
            // Same-size, same-timestamp content replacement still goes through strict validation.
            DateTime ticks=File.GetLastWriteTimeUtc(active);string corrupt=next.Json.Replace(world.worldId,"g"+world.worldId.Substring(1));
            Require(corrupt.Length==next.Json.Length && corrupt!=next.Json,"Fixture stamp-spoof не создан");
            File.WriteAllText(active,corrupt);File.SetLastWriteTimeUtc(active,ticks);hits=store.ReusedWorldPayloads;
            store.SaveRecovery(next);
            Require(store.ReusedWorldPayloads==hits+1 && Directory.EnumerateFiles(Path.Combine(root,"Recovery"),"*.json").Any(path=>File.ReadAllText(path)==corrupt) &&
                File.ReadAllText(active)==next.Json && File.ReadAllText(flat)==next.Json,"Invalid external same-stamp file не сохранён точным backup/принят cache");
            var restart=new SaveStore(root);restart.SaveRecovery(next);
            Require(restart.ReusedWorldPayloads==0,"Новый store доверяет чужому persistent cache");
            var bad=world.Copy();bad.extensions["unsupported-numeric"]=new JObject {{"nested",new JArray(double.MaxValue)}};
            Reject(()=>RecoverySnapshot.FromWorld(bad),"Unknown float вне Decimal accepted FromWorld");
            Reject(()=>new RecoverySnapshot(StateJson.ToJson(bad)),"Unknown float вне Decimal accepted public strict constructor");
            bad=world.Copy();bad.levelTimes[0].elapsedSeconds=-1;
            Reject(()=>RecoverySnapshot.FromWorld(bad),"Negative level clock accepted");
            bad=world.Copy();bad.extensions["WorldId"]=world.worldId;
            Reject(()=>RecoverySnapshot.FromWorld(bad),"Reserved/case-collision extension accepted");
            Debug.Log("Recovery capture: PASS — immutable 135KB payload, first-parse ownership/concurrent consumers, Record/sequence без disk, nested extension/Decimal/alias/receipt, exact known active+flat reuse, external valid archive, same-stamp invalid backup, strict unknown-number/clock/field guards. Own JSON reparse намеренно сохранён.");
        }
        private static void CheckLiveOwnership(RecoverySnapshot source)
        {
            var snapshot=new RecoverySnapshot(source.Json);
            var first=snapshot.CreateLiveWorld();string expected=StateJson.ToJson(first);
            first.actors[0].x=999;first.actors[0].inventory[0].count=99;
            first.extensions["capture-mod"]["precise"]=0;
            first.actors[0].inventory[0].extensions["item-mod"]["nested"][1]="изменённый live";
            var later=snapshot.CreateLiveWorld();
            Require(!ReferenceEquals(first,later) && StateJson.ToJson(later)==expected && snapshot.Json==source.Json &&
                snapshot.Id==source.Id && snapshot.Revision==source.Revision && snapshot.WorldId==source.WorldId,
                "First live mutation изменил immutable snapshot/следующего consumer");
            var concurrent=new RecoverySnapshot(source.Json);
            using var start=new ManualResetEventSlim();
            var left=Task.Run(()=>{start.Wait();return concurrent.CreateLiveWorld();});
            var right=Task.Run(()=>{start.Wait();return concurrent.CreateLiveWorld();});
            start.Set();Require(Task.WaitAll(new Task[] {left,right},TimeSpan.FromSeconds(10)),"Concurrent CreateLiveWorld timeout");
            Require(!ReferenceEquals(left.Result,right.Result) && !ReferenceEquals(left.Result.actors[0],right.Result.actors[0]) &&
                StateJson.ToJson(left.Result)==expected && StateJson.ToJson(right.Result)==expected,"Concurrent consumers shared live graph");
            left.Result.actors[0].inventory[0].count=88;
            left.Result.extensions["capture-mod"]["precise"]=1;
            Require(StateJson.ToJson(right.Result)==expected && StateJson.ToJson(concurrent.CreateLiveWorld())==expected,
                "Concurrent live mutation leaked into another caller/immutable payload");
        }
        private static void Reject(Action action,string reason)
        {bool refused=false;try {action();} catch(Exception error) when(error is InvalidDataException || error is Newtonsoft.Json.JsonException || error is InvalidOperationException || error is System.Reflection.TargetInvocationException {InnerException:InvalidDataException}) {refused=true;}Require(refused,reason);}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Recovery capture: "+reason);}
    }
}
