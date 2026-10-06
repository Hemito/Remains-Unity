using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;

namespace Remains.Editor
{
    // Controlled I/O scheduling exercises the production queue. It proves receipt
    // ordering, not atomic crash recovery of two independently published files.
    public static class RecoveryDurabilityChecks
    {
        public static void Run()=>RunAt(Path.Combine(Path.GetTempPath(),"remains-recovery-durability-"+Guid.NewGuid().ToString("N")));
        public static void RunAt(string root)
        {
            CheckBlockedWrites(Path.Combine(root,"remote"),false);
            CheckBlockedWrites(Path.Combine(root,"host"),true);
            CheckCharacterFailure(Path.Combine(root,"failure"));
            CheckShutdown(Path.Combine(root,"shutdown"));
            CheckLaterSelection(Path.Combine(root,"later"));
            UnityEngine.Debug.Log("Recovery durability: PASS — blocked canonical/replica world, blocked personal profile, failing profile suppresses receipt, FIFO, shutdown without stale ACK, immutable archive proof after newer selection. Atomic pair crash recovery не проверяется этим контрактом.");
        }
        private static void CheckBlockedWrites(string root,bool localHost)
        {
            var store=new SaveStore(root);var snapshot=GrantSnapshot();var actor=snapshot.CreateLiveWorld().actors[0];
            var queue=new OrderedSaveWrites();var failures=new List<Exception>();int worldReceipts=0,acknowledgements=0;
            using var worldEntered=new ManualResetEventSlim();using var releaseWorld=new ManualResetEventSlim();
            using var characterEntered=new ManualResetEventSlim();using var releaseCharacter=new ManualResetEventSlim();
            queue.Enqueue(()=>{worldEntered.Set();Wait(releaseWorld,"release world");store.SaveRecovery(snapshot);},()=>worldReceipts++,failures.Add);
            // A host local client does not rewrite canonical state, but its no-op
            // still follows the canonical publication on this same queue.
            if(localHost) queue.Enqueue(()=>{},null,failures.Add);
            queue.Enqueue(()=>{characterEntered.Set();Wait(releaseCharacter,"release character");store.SaveCharacter(actor);},null,failures.Add);
            queue.Enqueue(()=>{},()=>{CheckPair(store,snapshot,actor);acknowledgements++;},failures.Add);
            try
            {
                Wait(worldEntered,"world start");queue.Pump();
                Require(!characterEntered.IsSet && worldReceipts==0 && acknowledgements==0 && !File.Exists(Path.Combine(root,"world.json")),"ACK/profile before blocked world");
                releaseWorld.Set();Wait(characterEntered,"character start");queue.Pump();
                Require(worldReceipts==1 && acknowledgements==0 && !File.Exists(Path.Combine(root,"character.json")),"ACK before blocked personal profile");
                // The two-file crash window is real: a committed world may already
                // exist while profile publication has not completed. No ACK yet.
                Require(new RecoverySnapshot(File.ReadAllText(Path.Combine(root,"world.json"))).Id==snapshot.Id,"World write lost immutable capture");
                releaseCharacter.Set();Drain(queue);
                Require(failures.Count==0 && acknowledgements==1,"Successful pair did not yield exactly one receipt");
                queue.Pump();Require(acknowledgements==1,"Pump duplicated receipt");
            }
            finally {releaseWorld.Set();releaseCharacter.Set();queue.Flush();}
        }
        private static void CheckCharacterFailure(string root)
        {
            var store=new SaveStore(root);var snapshot=GrantSnapshot();var queue=new OrderedSaveWrites();
            var failures=new List<Exception>();int acknowledgements=0;bool barrierRan=false;
            queue.Enqueue(()=>store.SaveRecovery(snapshot),null,failures.Add);
            queue.Enqueue(()=>throw new IOException("Контролируемая ошибка записи личного профиля."),null,failures.Add);
            queue.Enqueue(()=>barrierRan=true,()=>acknowledgements++,failures.Add);
            Drain(queue);
            Require(failures.Count==2 && failures.TrueForAll(e=>e is IOException) && !barrierRan && acknowledgements==0,
                "Failed character did not poison receipt barrier");
            Require(store.SelectedSnapshot().Id==snapshot.Id && !File.Exists(Path.Combine(root,"character.json")),"Failure fixture did not reach world/profile boundary");
        }
        private static void CheckShutdown(string root)
        {
            var store=new SaveStore(root);var snapshot=GrantSnapshot();var actor=snapshot.CreateLiveWorld().actors[0];
            var queue=new OrderedSaveWrites();int callbacks=0;
            queue.Enqueue(()=>store.SaveRecovery(snapshot),()=>callbacks++,error=>throw error);
            queue.Enqueue(()=>store.SaveCharacter(actor),null,error=>throw error);
            queue.Enqueue(()=>{},()=>callbacks++,error=>throw error);
            queue.Flush();queue.Pump();CheckPair(store,snapshot,actor);
            Require(callbacks==0 && queue.PendingCount==0,"Shutdown emitted old-session receipt/callback");
            bool failed=false;queue.Enqueue(()=>throw new IOException("Контролируемая ошибка shutdown."),null,error=>throw error);
            try {queue.Flush();} catch(IOException) {failed=true;}
            Require(failed && queue.PendingCount==0,"Shutdown silently accepted failed write");
        }
        private static void CheckLaterSelection(string root)
        {
            var store=new SaveStore(root);var first=GrantSnapshot();var world=first.CreateLiveWorld();world.revision++;world.checkpointSequence++;
            var second=RecoverySnapshot.FromWorld(world);var queue=new OrderedSaveWrites();int callbacks=0;
            using var secondWritten=new ManualResetEventSlim();
            queue.Enqueue(()=>store.SaveRecovery(first),()=>{Require(File.ReadAllText(Archive(store,first))==first.Json,"Earlier immutable archive unavailable after later write");callbacks++;},error=>throw error);
            queue.Enqueue(()=>{store.SaveRecovery(second);secondWritten.Set();},()=>callbacks++,error=>throw error);
            Wait(secondWritten,"later snapshot");
            Require(store.SelectedSnapshot().Id==second.Id && callbacks==0,"Fixture did not delay callbacks behind newer selected state");
            Drain(queue);Require(callbacks==2,"Persisted callbacks lost");
        }
        private static RecoverySnapshot GrantSnapshot()
        {
            var world=new WorldState {revision=8};WorldHistory.BeginContinuation(world);
            var actor=new ActorState {id="player:durability",character="durability",player=true,connected=true,owner="durability-owner",level="hub",x=4,y=.8f};
            actor.inventory.Add(new ItemStack {id="item:durability-grant",definition="caps",count=3,
                extensions=new Dictionary<string,JToken> {{"durability-mod",new JObject {{"unknown",new JArray(7,"сохранить")}}}}});
            actor.inventoryAliases["item:durability-source"]="item:durability-grant";
            world.actors.Add(actor);world.containers.Add(new ContainerState {id="container:durability",items=new List<ItemStack>()});
            world.completedTransactions.Add("transaction:durability-grant");
            return RecoverySnapshot.FromWorld(world);
        }
        private static void CheckPair(SaveStore store,RecoverySnapshot snapshot,ActorState actor)
        {
            Require(File.ReadAllText(Archive(store,snapshot))==snapshot.Json,"Receipt lacks exact durable immutable world");
            var saved=StateJson.FromJson<ActorState>(store.ReadCharacterSource());
            Require(saved.id==actor.id && saved.character==actor.character && saved.inventory.Count==1 && saved.inventory[0].count==3 &&
                saved.inventoryAliases["item:durability-source"]=="item:durability-grant" &&
                saved.inventory[0].extensions["durability-mod"]["unknown"][1].Value<string>()=="сохранить","Receipt lacks exact captured profile/alias/mod fields");
        }
        private static string Archive(SaveStore store,RecoverySnapshot snapshot)=>Path.Combine(store.WorldDirectoryPath,"Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
        private static void Wait(ManualResetEventSlim signal,string stage) {Require(signal.Wait(TimeSpan.FromSeconds(10)),"Timeout: "+stage);}
        private static void Drain(OrderedSaveWrites queue)
        {
            var time=Stopwatch.StartNew();while(queue.PendingCount>0 && time.Elapsed.TotalSeconds<10) {queue.Pump();if(queue.PendingCount>0) Thread.Sleep(1);}
            Require(queue.PendingCount==0,"Queue did not finish controlled writes");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Recovery durability: "+reason);}
    }
}